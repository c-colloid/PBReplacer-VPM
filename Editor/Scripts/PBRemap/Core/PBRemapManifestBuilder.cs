using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
    /// <summary>
    /// PBRemap配下のVRCコンポーネントを走査し、外部参照をマニフェストとして記述する。
    /// 「移植元にいる間」に呼ばれることを前提とし、参照先Transformが生きている場合のみ有効なデータを作る。
    /// </summary>
    public static class PBRemapManifestBuilder
    {
        /// <summary>
        /// PBRemap配下の外部参照の状態。
        /// </summary>
        public enum ReferenceState
        {
            /// <summary>外部参照が無い（対象コンポーネントが無い、または全て内部参照）</summary>
            NoExternalReferences,
            /// <summary>外部参照が生きていて、単一のルートに属している</summary>
            Live,
            /// <summary>外部参照の一部/全部が null（Prefab化・シーン跨ぎ後）</summary>
            Broken,
        }

        public class ScanResult
        {
            public ReferenceState State;
            public RootInfo SourceRoot;
            public List<ContextInfo> Contexts = new List<ContextInfo>();
            public List<(Component component, string propertyPath, UnityEngine.Object target)> ExternalRefs = new List<(Component, string, UnityEngine.Object)>();
            public int NullRefs;
            public int InternalRefs;
            public List<string> Warnings = new List<string>();
        }

        /// <summary>
        /// PBRemap配下の外部参照を走査し、移植元ルートとコンテキストを特定する。
        /// 複数のルートに参照が分散している場合は、最も多く参照されるルートを採用し警告を出す。
        /// </summary>
        public static ScanResult Scan(PBRemap definition)
        {
            var result = new ScanResult();
            var definitionRoot = definition.transform;

            var rootCounts = new Dictionary<GameObject, int>();
            var rootInfos = new Dictionary<GameObject, RootInfo>();

            // マニフェストに記録された参照キー（null になっていれば「失われた参照」と判定する根拠）
            var manifestKeys = new HashSet<string>();
            if (definition.Manifest != null)
                foreach (var r in definition.Manifest.refs) manifestKeys.Add(r.Key);

            foreach (var component in CollectVRCComponents(definitionRoot))
            {
                var componentPath = BoneMapper.GetRelativePath(component.transform, definitionRoot) ?? "";
                var so = new SerializedObject(component);
                var prop = so.GetIterator();
                while (prop.Next(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (prop.propertyPath.StartsWith("m_")) continue;
                    var obj = prop.objectReferenceValue;
                    if (obj == null)
                    {
                        // マニフェストに存在するキーが null → Prefab化/シーン跨ぎで失われた参照
                        if (manifestKeys.Contains(componentPath + "." + prop.propertyPath))
                            result.NullRefs++;
                        continue;
                    }
                    Transform t = obj as Transform ?? (obj as Component)?.transform;
                    if (t == null) continue;
                    if (t == definitionRoot || t.IsChildOf(definitionRoot)) { result.InternalRefs++; continue; }

                    result.ExternalRefs.Add((component, prop.propertyPath, obj));
                    var ri = PBRemapContextResolver.FindRoot(t, excludeSelf: false);
                    var key = ri.IsFound ? ri.Root : t.root.gameObject;
                    rootCounts[key] = rootCounts.TryGetValue(key, out var c) ? c + 1 : 1;
                    if (!rootInfos.ContainsKey(key)) rootInfos[key] = ri.IsFound ? ri : new RootInfo { Root = key, Kind = RootKind.Generic, Method = AvatarDetectionMethod.Root, Reason = "fallback:transform.root" };
                }
            }

            if (result.ExternalRefs.Count == 0)
            {
                // 外部参照が無い: マニフェストがあるのに全て null なら Broken、そうでなければ参照なし
                if (result.NullRefs > 0) result.State = ReferenceState.Broken;
                else if (manifestKeys.Count > 0 && result.InternalRefs == 0 && HasAnyNullRootTransform(definitionRoot)) result.State = ReferenceState.Broken;
                else result.State = ReferenceState.NoExternalReferences;
                return result;
            }

            var winner = rootCounts.OrderByDescending(kv => kv.Value).First().Key;
            result.SourceRoot = rootInfos[winner];
            if (rootCounts.Count > 1)
            {
                var others = rootCounts.Where(kv => kv.Key != winner).Select(kv => $"{kv.Key.name}({kv.Value}件)");
                result.Warnings.Add($"参照の一部が '{winner.name}' 以外のオブジェクトを指しています: {string.Join(", ", others)}" +
                    "（Constraintの対象や別アバターのボーン等）。これらは移植先で解決できない場合、未解決として残ります。");
            }
            result.Contexts = PBRemapContextResolver.BuildContexts(winner);
            // 一部が失われていても、生きている参照があれば Live として扱う（失われた分はマニフェストで補う）
            result.State = ReferenceState.Live;
            if (result.NullRefs > 0)
                result.Warnings.Add($"{result.NullRefs} 件の参照が失われています（マニフェストから解決を試みます）。");
            return result;
        }

        /// <summary>rootTransform/TargetTransform が null の VRC コンポーネントがあるか（Prefab化で失われた可能性）</summary>
        private static bool HasAnyNullRootTransform(Transform definitionRoot)
        {
            foreach (var c in CollectVRCComponents(definitionRoot))
            {
                switch (c)
                {
                    case VRCPhysBoneBase pb when pb.rootTransform == null: return true;
                    case VRCPhysBoneColliderBase pbc when pbc.rootTransform == null: return true;
                    case ContactBase ct when ct.rootTransform == null: return true;
                    case VRCConstraintBase cs when cs.TargetTransform == null: return true;
                }
            }
            return false;
        }

        /// <summary>
        /// null が「自分自身の Transform」を意味するプロパティか。
        /// </summary>
        public static bool IsNullMeaningfulProperty(string propertyPath)
        {
            return propertyPath == "rootTransform" || propertyPath == "TargetTransform";
        }

        /// <summary>
        /// マニフェストを生成する。外部参照が Live でない場合は null を返す。
        /// </summary>
        public static PBRemapManifest Build(PBRemap definition, ScanResult scan = null)
        {
            scan ??= Scan(definition);
            if (scan.State != ReferenceState.Live || scan.SourceRoot == null || !scan.SourceRoot.IsFound)
                return null;

            var root = scan.SourceRoot.Root.transform;
            var definitionRoot = definition.transform;
            var contexts = scan.Contexts;
            var manifest = new PBRemapManifest
            {
                version = PBRemapManifest.CurrentVersion,
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                sourceRootName = root.name,
                sourceRootKind = scan.SourceRoot.Kind.ToString(),
                sourceRootInstanceId = root.gameObject.GetInstanceID(),
            };
            foreach (var c in contexts)
                manifest.contexts.Add(PBRemapContextResolver.ToSerializable(c, root));

            // Humanoid map（本体コンテキスト）
            var humanoidMap = new Dictionary<Transform, HumanBodyBones>();
            var mainCtx = contexts.FirstOrDefault(c => c.Kind == BoneContextKind.Main && c.IsHumanoid);
            if (mainCtx != null)
            {
                foreach (HumanBodyBones id in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (id == HumanBodyBones.LastBone) continue;
                    var b = mainCtx.Animator.GetBoneTransform(id);
                    if (b != null && !humanoidMap.ContainsKey(b)) humanoidMap[b] = id;
                }
                var hips = mainCtx.Animator.GetBoneTransform(HumanBodyBones.Hips);
                var head = mainCtx.Animator.GetBoneTransform(HumanBodyBones.Head);
                if (hips != null && head != null)
                    manifest.scaleReference.hipsToHead = Vector3.Distance(hips.position, head.position);
                manifest.scaleReference.armatureLossyScaleY = mainCtx.Armature.lossyScale.y;
            }
            else
            {
                var main = contexts.FirstOrDefault(c => c.Kind == BoneContextKind.Main) ?? contexts[0];
                manifest.scaleReference.armatureLossyScaleY = main.Armature.lossyScale.y;
            }

            var skinnedBones = BoneMapper.CollectSkinnedBones(root.gameObject);

            foreach (var (component, propertyPath, target) in scan.ExternalRefs)
            {
                Transform t = target as Transform ?? (target as Component)?.transform;
                if (t == null) continue;
                // ルート外（別アバター等）の参照はコンテキスト無し（Generic 0 で pathFromRoot 空）として記録
                var ctx = t.IsChildOf(root) ? PBRemapContextResolver.ClassifyBone(t, contexts) : null;
                var boneRef = new BoneRef
                {
                    componentPath = BoneMapper.GetRelativePath(component.transform, definitionRoot) ?? "",
                    componentType = component.GetType().Name,
                    propertyPath = propertyPath,
                    contextId = ctx != null ? ctx.Id : -1,
                    relPath = ctx != null ? (BoneMapper.GetRelativePath(t, ctx.Armature) ?? "") : "",
                    boneName = t.name,
                    isSkeletonBone = BoneMapper.IsSkeletonBone(t, skinnedBones),
                    localPosition = t.localPosition,
                    localRotation = t.localRotation,
                    localScale = t.localScale,
                    lossyScale = t.lossyScale,
                    targetComponentType = target is Transform ? "" : target.GetType().Name,
                    pathFromRoot = t.IsChildOf(root) ? (BoneMapper.GetRelativePath(t, root) ?? "") : "",
                };
                if (humanoidMap.TryGetValue(t, out var hb)) boneRef.humanBone = hb;

                // 最寄りHumanoid祖先
                var segs = new List<string> { t.name };
                for (var a = t.parent; a != null && a != root; a = a.parent)
                {
                    if (humanoidMap.TryGetValue(a, out var ab))
                    {
                        boneRef.nearestHumanoidAncestor = ab;
                        segs.Reverse();
                        boneRef.pathFromAncestor = string.Join("/", segs);
                        break;
                    }
                    segs.Add(a.name);
                }

                manifest.refs.Add(boneRef);
                manifest.scaleReference.boneParentDistances.Add(t.parent != null ? Vector3.Distance(t.position, t.parent.position) : 0f);
            }

            // 元値
            foreach (var component in CollectVRCComponents(definitionRoot))
            {
                var o = new OriginalValues
                {
                    componentPath = BoneMapper.GetRelativePath(component.transform, definitionRoot) ?? "",
                    componentType = component.GetType().Name,
                };
                Transform rootBone = null;
                switch (component)
                {
                    case VRCPhysBoneBase pb:
                        o.radius = pb.radius; o.endpointPosition = pb.endpointPosition; rootBone = pb.rootTransform != null ? pb.rootTransform : pb.transform; break;
                    case VRCPhysBoneColliderBase pbc:
                        o.radius = pbc.radius; o.height = pbc.height; o.position = pbc.position; rootBone = pbc.rootTransform != null ? pbc.rootTransform : pbc.transform; break;
                    case ContactBase ct:
                        o.radius = ct.radius; o.height = ct.height; o.position = ct.position; rootBone = ct.rootTransform != null ? ct.rootTransform : ct.transform; break;
                }
                o.rootLossyScaleMax = rootBone != null ? MaxComponent(rootBone.lossyScale) : 1f;
                manifest.originals.Add(o);
            }

            return manifest;
        }

        public static float MaxComponent(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

        /// <summary>
        /// PBRemap配下の全VRCコンポーネントを収集する。
        /// </summary>
        public static List<Component> CollectVRCComponents(Transform root)
        {
            var components = new List<Component>();
            components.AddRange(root.GetComponentsInChildren<VRCPhysBoneBase>(true));
            components.AddRange(root.GetComponentsInChildren<VRCPhysBoneColliderBase>(true));
            components.AddRange(root.GetComponentsInChildren<VRCConstraintBase>(true));
            components.AddRange(root.GetComponentsInChildren<ContactBase>(true));
            return components;
        }

        /// <summary>
        /// 旧形式（serializedBoneReferences）からマニフェストへ移行する。
        /// </summary>
        public static PBRemapManifest MigrateLegacy(IReadOnlyList<SerializedBoneReference> legacy, float sourceAvatarScale)
        {
            if (legacy == null || legacy.Count == 0) return null;
            var m = new PBRemapManifest
            {
                version = PBRemapManifest.CurrentVersion,
                capturedAtUtc = "",
                sourceRootName = "(legacy)",
                sourceRootKind = "Legacy",
            };
            m.contexts.Add(new BoneContext { id = 0, kind = BoneContextKind.Generic });
            m.contexts.Add(new BoneContext { id = 1, kind = BoneContextKind.Main, isHumanoid = legacy.Any(r => r.humanBodyBone != HumanBodyBones.LastBone || r.nearestHumanoidAncestor != HumanBodyBones.LastBone) });
            foreach (var r in legacy)
            {
                var segs = (r.boneRelativePath ?? "").Split('/');
                m.refs.Add(new BoneRef
                {
                    componentPath = r.componentObjectPath,
                    componentType = r.componentTypeName,
                    propertyPath = r.propertyPath,
                    contextId = 1,
                    relPath = r.boneRelativePath ?? "",
                    boneName = segs.Length > 0 ? segs[segs.Length - 1] : "",
                    humanBone = r.humanBodyBone,
                    nearestHumanoidAncestor = r.nearestHumanoidAncestor,
                    pathFromAncestor = r.pathFromHumanoidAncestor ?? "",
                    isSkeletonBone = r.isSkeletonBone,
                });
            }
            m.scaleReference.hipsToHead = sourceAvatarScale;
            return m;
        }
    }
}
