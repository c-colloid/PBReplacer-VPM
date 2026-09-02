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
    ///
    /// 参照は「現在のホーム（PBRemapが属する最も近い単位）またはその外側の単位に整合しているか」で分類する。
    /// - 整合: ホーム自身 / ホーム配下の下位単位（アバター内の衣装など） / 外側の単位（衣装ホームに対するアバター）
    /// - 外れ: それ以外（別のアバター、外側の中の兄弟衣装 など）→ 移植元と見なす
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
            /// <summary>外部参照が生きている</summary>
            Live,
            /// <summary>外部参照の全部が null（Prefab化・シーン跨ぎ後）</summary>
            Broken,
        }

        public class ScanResult
        {
            public ReferenceState State;
            /// <summary>PBRemap の現在のホーム（= 移植先）</summary>
            public RootInfo Home;
            /// <summary>参照が指す移植元（外れた参照の多数決。全て整合していればホーム自身）</summary>
            public RootInfo SourceRoot;
            public List<ContextInfo> Contexts = new List<ContextInfo>();
            public List<(Component component, string propertyPath, UnityEngine.Object target)> ExternalRefs = new List<(Component, string, UnityEngine.Object)>();
            public int NullRefs;
            public int InternalRefs;
            /// <summary>現在のホーム/外側に整合している参照数</summary>
            public int ConsistentRefs;
            /// <summary>現在のホーム/外側から外れている参照数（= 移植が必要な参照）</summary>
            public int ForeignRefs;
            /// <summary>外れているが、マニフェスト取得時点で既に移植元の外にあった参照数（判定材料にしない）</summary>
            public int KnownExternalRefs;
            /// <summary>マニフェストにあるが現在 null の参照キー（Prefab化などで失われた参照）</summary>
            public List<string> LostKeys = new List<string>();
            public List<string> Warnings = new List<string>();

            public bool HasForeign => ForeignRefs > 0;
        }

        /// <summary>
        /// PBRemap配下の外部参照を走査し、移植元ルートとコンテキストを特定する。
        /// </summary>
        /// <param name="definition">PBRemap</param>
        /// <param name="home">現在のホーム（省略時は PBRemap の位置から検出）</param>
        public static ScanResult Scan(PBRemap definition, RootInfo home = null)
        {
            var result = new ScanResult();
            var definitionRoot = definition.transform;
            if (home == null)
            {
                home = definition.DestinationRootOverride != null
                    ? RootInfo.For(definition.DestinationRootOverride)
                    : PBRemapContextResolver.FindRoot(definitionRoot, excludeSelf: true);
            }
            result.Home = home;

            var rootCounts = new Dictionary<GameObject, int>();
            var rootInfos = new Dictionary<GameObject, RootInfo>();
            var rootByTransform = new Dictionary<Transform, RootInfo>();
            var classifyCache = new Dictionary<Transform, RootKind>();

            // マニフェストに記録された参照キー（null になっていれば「失われた参照」と判定する根拠）
            // および、取得時点で既に移植元の外にあった参照（contextId < 0）。後者は移植の判定材料にしない
            // （例: 移植後も移植元アバターの小物を指したままの Constraint。これを「外れ」と数えると適用済みなのに Displaced になる）
            var manifestKeys = new HashSet<string>();
            var knownExternalKeys = new HashSet<string>();
            if (definition.Manifest != null)
                foreach (var r in definition.Manifest.refs)
                {
                    manifestKeys.Add(r.Key);
                    if (r.contextId < 0) knownExternalKeys.Add(r.Key);
                }

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
                        var key = componentPath + "." + prop.propertyPath;
                        if (manifestKeys.Contains(key)) { result.NullRefs++; result.LostKeys.Add(key); }
                        continue;
                    }
                    Transform t = obj as Transform ?? (obj as Component)?.transform;
                    if (t == null) continue;
                    if (t == definitionRoot || t.IsChildOf(definitionRoot)) { result.InternalRefs++; continue; }

                    result.ExternalRefs.Add((component, prop.propertyPath, obj));
                    if (!rootByTransform.TryGetValue(t, out var ri))
                    {
                        ri = PBRemapContextResolver.FindRoot(t, excludeSelf: false, classifyCache);
                        rootByTransform[t] = ri;
                    }
                    if (IsConsistentWithHome(t, ri, home)) { result.ConsistentRefs++; continue; }
                    if (knownExternalKeys.Contains(componentPath + "." + prop.propertyPath)) { result.KnownExternalRefs++; continue; }

                    result.ForeignRefs++;
                    var rootGo = ri.IsFound ? ri.Root : t.root.gameObject;
                    rootCounts[rootGo] = rootCounts.TryGetValue(rootGo, out var c) ? c + 1 : 1;
                    if (!rootInfos.ContainsKey(rootGo)) rootInfos[rootGo] = ri.IsFound ? ri : new RootInfo { Root = rootGo, Kind = RootKind.Generic, Method = AvatarDetectionMethod.Root, Reason = "fallback:transform.root" };
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

            if (rootCounts.Count == 0)
            {
                // 全ての参照が現在のホーム/外側に整合している（移植元にいる／適用済み）
                result.SourceRoot = home != null && home.IsFound ? home : rootByTransform.Values.FirstOrDefault(v => v.IsFound);
                if (result.SourceRoot == null)
                {
                    var first = result.ExternalRefs[0].target;
                    var ft = first as Transform ?? (first as Component)?.transform;
                    result.SourceRoot = new RootInfo { Root = ft != null ? ft.root.gameObject : null, Kind = RootKind.Generic, Method = AvatarDetectionMethod.Root, Reason = "fallback:transform.root" };
                }
                result.Contexts = PBRemapContextResolver.BuildContexts(result.SourceRoot);
                result.State = ReferenceState.Live;
                if (result.NullRefs > 0)
                    result.Warnings.Add($"{result.NullRefs} 件の参照が失われています（参照情報から解決を試みます）。");
                return result;
            }

            var winner = SelectSourceRoot(rootCounts, definition.Manifest, home);
            result.SourceRoot = rootInfos[winner];
            if (rootCounts.Count > 1)
            {
                var others = rootCounts.Where(kv => kv.Key != winner).Select(kv => $"{kv.Key.name}({kv.Value}件)");
                result.Warnings.Add($"参照の一部が '{winner.name}' 以外のオブジェクトを指しています: {string.Join(", ", others)}" +
                    "（Constraintの対象や別アバターのボーン等）。これらは移植先で解決できない場合、未解決として残ります。");
            }
            result.Contexts = PBRemapContextResolver.BuildContexts(result.SourceRoot);
            // 一部が失われていても、生きている参照があれば Live として扱う（失われた分はマニフェストで補う）
            result.State = ReferenceState.Live;
            if (result.NullRefs > 0)
                result.Warnings.Add($"{result.NullRefs} 件の参照が失われています（参照情報から解決を試みます）。");
            return result;
        }

        /// <summary>
        /// 参照先 t（属する単位 ri）が、現在のホーム/外側に整合しているか。
        /// 整合 = ホーム自身 / ホーム配下の単位 / 外側の単位 / 単位に属さないがホームまたは外側の配下にあるオブジェクト。
        /// 外側の中の別単位（兄弟衣装など）は「外れ」。
        /// </summary>
        public static bool IsConsistentWithHome(Transform t, RootInfo ri, RootInfo home)
        {
            if (home == null || home.Root == null || t == null) return false;
            var homeT = home.Root.transform;
            if (ri != null && ri.IsFound)
            {
                if (ri.Root == home.Root) return true;
                if (ri.Root.transform.IsChildOf(homeT)) return true;
                if (home.Outer != null && ri.Root == home.Outer) return true;
                return false;
            }
            if (t == homeT || t.IsChildOf(homeT)) return true;
            if (home.Outer != null && t.IsChildOf(home.Outer.transform)) return true;
            return false;
        }

        /// <summary>
        /// 外れた参照の指す複数ルートから移植元を選ぶ。
        /// 1. マニフェストが記録している移植元 2. ホームが衣装なら衣装 3. 入れ子なら外側 4. 多数決
        /// </summary>
        private static GameObject SelectSourceRoot(Dictionary<GameObject, int> rootCounts, PBRemapManifest manifest, RootInfo home)
        {
            var roots = rootCounts.Keys.ToList();
            if (roots.Count == 1) return roots[0];
            if (manifest != null && !manifest.IsEmpty)
            {
                var m = roots.FirstOrDefault(r => r.GetInstanceID() == manifest.sourceRootInstanceId);
                if (m != null) return m;
            }
            if (home != null && home.Kind == RootKind.MACostume)
            {
                var costume = roots.Where(r => PBRemapContextResolver.IsCostumeRoot(r.transform)).OrderByDescending(r => rootCounts[r]).FirstOrDefault();
                if (costume != null) return costume;
            }
            var outermost = roots.Where(r => !roots.Any(o => o != r && r.transform.IsChildOf(o.transform))).ToList();
            return (outermost.Count > 0 ? outermost : roots).OrderByDescending(r => rootCounts[r]).First();
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
        /// 失われた参照（LostKeys）があり既存マニフェストに記録があれば、その分を引き継ぐ。
        /// </summary>
        public static PBRemapManifest Build(PBRemap definition, ScanResult scan = null)
        {
            scan ??= Scan(definition);
            if (scan.State != ReferenceState.Live || scan.SourceRoot == null || !scan.SourceRoot.IsFound)
                return null;

            var source = scan.SourceRoot;
            var root = source.Root.transform;
            var outerT = source.Outer != null ? source.Outer.transform : null;
            var definitionRoot = definition.transform;
            var contexts = scan.Contexts;
            var manifest = new PBRemapManifest
            {
                version = PBRemapManifest.CurrentVersion,
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                sourceRootName = root.name,
                sourceRootKind = source.Kind.ToString(),
                sourceRootInstanceId = root.gameObject.GetInstanceID(),
                outerRootName = outerT != null ? outerT.name : "",
                outerRootKind = outerT != null ? source.OuterKind.ToString() : "",
                outerRootInstanceId = outerT != null ? outerT.gameObject.GetInstanceID() : 0,
            };
            foreach (var c in contexts)
                manifest.contexts.Add(PBRemapContextResolver.ToSerializable(c, root));

            // Humanoid map（ホーム/外側の Humanoid 本体コンテキスト）
            var humanoidMap = new Dictionary<Transform, HumanBodyBones>();
            foreach (var mainCtx in contexts.Where(c => c.Kind == BoneContextKind.Main && c.IsHumanoid))
            {
                foreach (HumanBodyBones id in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (id == HumanBodyBones.LastBone) continue;
                    var b = mainCtx.Animator.GetBoneTransform(id);
                    if (b != null && !humanoidMap.ContainsKey(b)) humanoidMap[b] = id;
                }
                var hips = mainCtx.Animator.GetBoneTransform(HumanBodyBones.Hips);
                var head = mainCtx.Animator.GetBoneTransform(HumanBodyBones.Head);
                float hh = hips != null && head != null ? Vector3.Distance(hips.position, head.position) : 0f;
                if (mainCtx.Scope == ContextScope.Self) manifest.scaleReference.hipsToHead = hh;
                else manifest.scaleReference.outerHipsToHead = hh;
            }
            {
                var selfMain = contexts.FirstOrDefault(c => c.Scope == ContextScope.Self && c.Kind == BoneContextKind.Main) ?? contexts[0];
                manifest.scaleReference.armatureLossyScaleY = selfMain.Armature != null ? selfMain.Armature.lossyScale.y : 1f;
            }

            var skinnedBones = BoneMapper.CollectSkinnedBones(root.gameObject);
            if (outerT != null) skinnedBones.UnionWith(BoneMapper.CollectSkinnedBones(outerT.gameObject));

            foreach (var (component, propertyPath, target) in scan.ExternalRefs)
            {
                Transform t = target as Transform ?? (target as Component)?.transform;
                if (t == null) continue;
                // ホーム/外側のどちらにも属さない参照（別アバター等）はコンテキスト無し（-1）として記録
                var ctx = PBRemapContextResolver.ClassifyBone(t, contexts);
                var ctxRoot = ctx != null ? ctx.RootTransform : null;
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
                    pathFromRoot = ctxRoot != null ? (BoneMapper.GetRelativePath(t, ctxRoot) ?? "") : "",
                };
                if (humanoidMap.TryGetValue(t, out var hb)) boneRef.humanBone = hb;

                // 最寄りHumanoid祖先（コンテキストのルートまで）
                var segs = new List<string> { t.name };
                for (var a = t.parent; a != null && a != ctxRoot; a = a.parent)
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

            // 失われた参照は既存マニフェストから引き継ぐ（衣装Prefabにアバターボーン参照が含まれていた場合など）
            if (scan.LostKeys.Count > 0 && definition.Manifest != null && !definition.Manifest.IsEmpty)
                MergeLostRefs(manifest, definition.Manifest, scan.LostKeys);

            return manifest;
        }

        /// <summary>
        /// 現在 null になっている参照（lostKeys）を、以前のマニフェストから新しいマニフェストへ引き継ぐ。
        /// コンテキストは同等のものがあればそれを使い、無ければ複製して追加する。
        /// </summary>
        public static int MergeLostRefs(PBRemapManifest fresh, PBRemapManifest old, IList<string> lostKeys)
        {
            if (fresh == null || old == null || lostKeys == null || lostKeys.Count == 0) return 0;
            var lost = new HashSet<string>(lostKeys);
            var idMap = new Dictionary<int, int>();
            int merged = 0;
            for (int i = 0; i < old.refs.Count; i++)
            {
                var r = old.refs[i];
                if (!lost.Contains(r.Key) || fresh.refs.Any(x => x.Key == r.Key)) continue;
                var copy = JsonUtility.FromJson<BoneRef>(JsonUtility.ToJson(r));
                if (copy.contextId >= 0)
                {
                    if (!idMap.TryGetValue(copy.contextId, out var nid))
                    {
                        // 以前のコンテキストは複製して追加する（スケール基準 hipsToHead が移植元のものである必要があるため、
                        // 現在の同種コンテキストとは共有しない）
                        var oc = old.GetContext(copy.contextId);
                        if (oc != null)
                        {
                            var cc = JsonUtility.FromJson<BoneContext>(JsonUtility.ToJson(oc));
                            cc.id = fresh.contexts.Count == 0 ? 0 : fresh.contexts.Max(c => c.id) + 1;
                            fresh.contexts.Add(cc);
                            nid = cc.id;
                        }
                        else nid = -1;
                        idMap[copy.contextId] = nid;
                    }
                    copy.contextId = nid;
                }
                fresh.refs.Add(copy);
                fresh.scaleReference.boneParentDistances.Add(i < old.scaleReference.boneParentDistances.Count ? old.scaleReference.boneParentDistances[i] : 0f);
                merged++;
            }
            if (merged > 0)
            {
                // 失われた参照の元の移植元（表示用）。既に引き継ぎ済みならそれを維持
                fresh.lostSourceName = !string.IsNullOrEmpty(old.lostSourceName) ? old.lostSourceName
                    : (old.sourceRootInstanceId != fresh.sourceRootInstanceId || old.sourceRootName != fresh.sourceRootName ? old.SourceDisplayName : "");
                if (fresh.scaleReference.hipsToHead <= 1e-6f && old.scaleReference.hipsToHead > 1e-6f) fresh.scaleReference.hipsToHead = old.scaleReference.hipsToHead;
                if (fresh.scaleReference.outerHipsToHead <= 1e-6f && old.scaleReference.outerHipsToHead > 1e-6f) fresh.scaleReference.outerHipsToHead = old.scaleReference.outerHipsToHead;
                if (string.IsNullOrEmpty(fresh.outerRootName) && !string.IsNullOrEmpty(old.outerRootName)) { fresh.outerRootName = old.outerRootName; fresh.outerRootKind = old.outerRootKind; }
                foreach (var o in old.originals)
                    if (!fresh.originals.Any(x => x.componentPath == o.componentPath && x.componentType == o.componentType))
                        fresh.originals.Add(JsonUtility.FromJson<OriginalValues>(JsonUtility.ToJson(o)));
            }
            return merged;
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
            // ネストした別の PBRemap の配下は、その PBRemap の管理対象なので除外する
            var nested = root.GetComponentsInChildren<PBRemap>(true).Where(p => p.transform != root).Select(p => p.transform).ToList();
            if (nested.Count > 0)
                components.RemoveAll(c => nested.Any(n => c.transform == n || c.transform.IsChildOf(n)));
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
