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
    /// アバタールートの検出方法を示す列挙体。
    /// UI のバッジ表示などで、どの経路で検出されたかをユーザーに伝えるために用いる。
    /// </summary>
    public enum AvatarDetectionMethod
    {
        /// <summary>未検出</summary>
        None,
        /// <summary>手動指定 (SourceRootOverride / DestinationRootOverride)</summary>
        Manual,
        /// <summary>VRC_AvatarDescriptor（標準パターン）</summary>
        VRCAvatarDescriptor,
        /// <summary>ModularAvatar MergeArmature（衣装ルート）</summary>
        MergeArmature,
        /// <summary>Prefab 境界 (Prefab Instance Root)（旧・互換）</summary>
        PrefabBoundary,
        /// <summary>Animator（FBX 直置き等）</summary>
        Animator,
        /// <summary>汎用オブジェクト（SkinnedMeshRendererを持つ小物等）</summary>
        Root,
    }

    /// <summary>
    /// PBRemapの配置状態から移植元・移植先を判定する（互換API）。
    /// 実体は <see cref="PBRemapper.Inspect"/> / <see cref="PBRemapContextResolver"/>。
    /// </summary>
    public static class SourceDetector
    {
        /// <summary>
        /// 検出結果を格納するデータクラス
        /// </summary>
        public class DetectionResult
        {
            /// <summary>ソースアバターのGameObject（Transform参照から逆引き）。参照が失われている場合はnull</summary>
            public GameObject SourceAvatar { get; set; }

            /// <summary>デスティネーションアバターのGameObject（親階層から検出）</summary>
            public GameObject DestinationAvatar { get; set; }

            /// <summary>ソースアバターのAvatarData。検出できた場合のみ非null</summary>
            public AvatarData SourceAvatarData { get; set; }

            /// <summary>デスティネーションアバターのAvatarData。検出できた場合のみ非null</summary>
            public AvatarData DestAvatarData { get; set; }

            /// <summary>同一シーンモードか（Transform参照が生きていて、別ルートを指している）</summary>
            public bool IsLiveMode { get; set; }

            /// <summary>検出に関する警告メッセージ</summary>
            public List<string> Warnings { get; set; } = new List<string>();

            /// <summary>子コンポーネントの参照がデスティネーション自身を指している（ホーム／適用済み）</summary>
            public bool IsReferencingDestination { get; set; }

            /// <summary>デスティネーションがどの検出経路で特定されたか</summary>
            public AvatarDetectionMethod DestinationDetectionMethod { get; set; }

            /// <summary>ソースがどの検出経路で特定されたか</summary>
            public AvatarDetectionMethod SourceDetectionMethod { get; set; }

            /// <summary>新設計の状況オブジェクト</summary>
            public PBRemapSituation Situation { get; set; }

            /// <summary>状態</summary>
            public PBRemapState State => Situation?.State ?? PBRemapState.NoReferences;

            /// <summary>マニフェスト（移植元の参照情報）を持っているか</summary>
            public bool HasManifest => Situation?.HasManifest ?? false;
        }

        /// <summary>
        /// PBRemapの配置と子コンポーネントのTransform参照からアバターを検出する。
        /// </summary>
        public static Result<DetectionResult, string> Detect(PBRemap definition)
        {
            if (definition == null)
                return Result<DetectionResult, string>.Failure("PBRemapがnullです");

            PBRemapper.MigrateLegacyIfNeeded(definition);
            var s = PBRemapper.Inspect(definition);
            var result = new DetectionResult { Situation = s };
            result.Warnings.AddRange(s.Warnings);

            if (s.DestinationRoot != null)
            {
                result.DestinationAvatar = s.DestinationRoot;
                result.DestinationDetectionMethod = s.Destination.Method;
                result.DestAvatarData = SafeAvatarData(s.DestinationRoot, result.Warnings, "デスティネーション");
            }

            switch (s.State)
            {
                case PBRemapState.AtHome:
                    result.IsReferencingDestination = true;
                    result.IsLiveMode = false;
                    break;
                case PBRemapState.Displaced:
                    result.IsLiveMode = true;
                    if (s.SourceRoot != null)
                    {
                        result.SourceAvatar = s.SourceRoot;
                        result.SourceDetectionMethod = s.Source.Method;
                        result.SourceAvatarData = SafeAvatarData(s.SourceRoot, result.Warnings, "ソース");
                    }
                    break;
                default:
                    result.IsLiveMode = false;
                    break;
            }

            return Result<DetectionResult, string>.Success(result);
        }

        private static AvatarData SafeAvatarData(GameObject root, List<string> warnings, string label)
        {
            try { return new AvatarData(root); }
            catch (System.Exception ex)
            {
                warnings.Add($"{label}の解析に失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// PBRemap配下の全VRCコンポーネントから外部Transform参照を収集する。
        /// 内部参照（PBRemapの子孫オブジェクトへの参照）は除外する。
        /// </summary>
        public static List<Transform> CollectExternalTransformReferences(PBRemap definition)
        {
            var result = new List<Transform>();
            var definitionRoot = definition.transform;

            foreach (var pb in definitionRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                AddIfExternal(result, pb.rootTransform, definitionRoot);
                if (pb.rootTransform == null)
                    AddIfExternal(result, pb.transform.parent, definitionRoot);
            }
            foreach (var pbc in definitionRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true))
            {
                AddIfExternal(result, pbc.rootTransform, definitionRoot);
                if (pbc.rootTransform == null)
                    AddIfExternal(result, pbc.transform.parent, definitionRoot);
            }
            foreach (var constraint in definitionRoot.GetComponentsInChildren<VRCConstraintBase>(true))
            {
                AddIfExternal(result, constraint.TargetTransform, definitionRoot);
                var so = new SerializedObject(constraint);
                var sourcesProp = so.FindProperty("Sources");
                if (sourcesProp != null)
                {
                    var it = sourcesProp.Copy();
                    var end = it.GetEndProperty();
                    while (it.NextVisible(true) && !SerializedProperty.EqualContents(it, end))
                    {
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.name == "SourceTransform" && it.objectReferenceValue is Transform srcTransform)
                            AddIfExternal(result, srcTransform, definitionRoot);
                    }
                }
            }
            foreach (var contact in definitionRoot.GetComponentsInChildren<ContactBase>(true))
            {
                AddIfExternal(result, contact.rootTransform, definitionRoot);
                if (contact.rootTransform == null)
                    AddIfExternal(result, contact.transform.parent, definitionRoot);
            }

            return result;
        }

        private static void AddIfExternal(List<Transform> list, Transform target, Transform definitionRoot)
        {
            if (target != null && !target.IsChildOf(definitionRoot))
                list.Add(target);
        }
    }
}
