using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;
using VRC.SDKBase;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
    /// <summary>
    /// PBRemapの配置状態からソースアバター・デスティネーションアバターを自動検出する。
    /// AvatarDescriptorが無い場合のフォールバック検出にも対応する。
    /// 検出優先順位: VRC_AvatarDescriptor → Animator → MAコンポーネント → Prefab → ルートGameObject → 手動指定
    /// </summary>
    public static class SourceDetector
    {
        /// <summary>
        /// 検出結果を格納するデータクラス
        /// </summary>
        public class DetectionResult
        {
            /// <summary>ソースアバターのGameObject（Transform参照から逆引き）。Prefab時はnull</summary>
            public GameObject SourceAvatar { get; set; }

            /// <summary>デスティネーションアバターのGameObject（親階層から検出）</summary>
            public GameObject DestinationAvatar { get; set; }

            /// <summary>ソースアバターのAvatarData。検出できた場合のみ非null</summary>
            public AvatarData SourceAvatarData { get; set; }

            /// <summary>デスティネーションアバターのAvatarData。検出できた場合のみ非null</summary>
            public AvatarData DestAvatarData { get; set; }

            /// <summary>同一シーンモードか（Transform参照が生きている）</summary>
            public bool IsLiveMode { get; set; }

            /// <summary>検出に関する警告メッセージ</summary>
            public List<string> Warnings { get; set; } = new();

            /// <summary>子コンポーネントの参照がデスティネーションアバター自身を指している（移植済み状態）</summary>
            public bool IsReferencingDestination { get; set; }

            /// <summary>デスティネーションがVRC_AvatarDescriptorで検出されたか（フォールバックか）</summary>
            public bool DestinationHasDescriptor { get; set; }

            /// <summary>ソースがVRC_AvatarDescriptorで検出されたか（フォールバックか）</summary>
            public bool SourceHasDescriptor { get; set; }
        }

        /// <summary>
        /// PBRemapの配置と子コンポーネントのTransform参照からアバターを検出する。
        /// </summary>
        /// <param name="definition">PBRemap</param>
        /// <returns>検出結果</returns>
        public static Result<DetectionResult, string> Detect(PBRemap definition)
        {
            if (definition == null)
                return Result<DetectionResult, string>.Failure("PBRemapがnullです");

            var result = new DetectionResult();

            // デスティネーション検出
            DetectDestination(definition, result);

            // ソース検出: 子コンポーネントのTransform参照から逆引き
            var sourceAvatar = DetectSourceFromChildComponents(definition);
            if (sourceAvatar != null)
            {
                // ソースとデスティネーションが同じアバターの場合は移植済み状態
                if (result.DestinationAvatar != null && sourceAvatar == result.DestinationAvatar)
                {
                    result.IsReferencingDestination = true;
                }
                else
                {
                    result.SourceAvatar = sourceAvatar;
                    result.SourceHasDescriptor =
                        sourceAvatar.GetComponent<VRC_AvatarDescriptor>() != null;
                    result.IsLiveMode = true;
                    try
                    {
                        result.SourceAvatarData = new AvatarData(result.SourceAvatar);
                    }
                    catch (System.Exception ex)
                    {
                        result.Warnings.Add($"ソースアバターの解析に失敗: {ex.Message}");
                    }
                }
            }
            else
            {
                // ソースの手動指定を確認
                if (definition.SourceRootOverride != null)
                {
                    result.SourceAvatar = definition.SourceRootOverride;
                    result.SourceHasDescriptor =
                        definition.SourceRootOverride.GetComponent<VRC_AvatarDescriptor>() != null;
                    result.IsLiveMode = true;
                    try
                    {
                        result.SourceAvatarData = new AvatarData(result.SourceAvatar);
                    }
                    catch (System.Exception ex)
                    {
                        result.Warnings.Add($"手動指定ソースアバターの解析に失敗: {ex.Message}");
                    }
                }
                else
                {
                    // Transform参照がない → Prefabモードの可能性
                    result.IsLiveMode = false;
                    if (definition.SerializedBoneReferences.Count > 0)
                    {
                        // シリアライズデータあり → Prefabモードとして動作可能
                    }
                    else
                    {
                        result.Warnings.Add("ソースアバターを検出できません。" +
                            "子コンポーネントのTransform参照がないか、シリアライズデータがありません。" +
                            "手動で移植元を指定するか、Inspectorを開いてボーン情報を取得してください。");
                    }
                }
            }

            return Result<DetectionResult, string>.Success(result);
        }

        /// <summary>
        /// デスティネーションアバターを検出する。
        /// 優先順位: 手動指定 → VRC_AvatarDescriptor → Animator → MAコンポーネント → Prefab → ルートGameObject
        /// </summary>
        private static void DetectDestination(PBRemap definition, DetectionResult result)
        {
            // 手動指定がある場合はそれを使用
            if (definition.DestinationRootOverride != null)
            {
                result.DestinationAvatar = definition.DestinationRootOverride;
                result.DestinationHasDescriptor =
                    definition.DestinationRootOverride.GetComponent<VRC_AvatarDescriptor>() != null;
                try
                {
                    result.DestAvatarData = new AvatarData(result.DestinationAvatar);
                }
                catch (System.Exception ex)
                {
                    result.Warnings.Add($"手動指定デスティネーションアバターの解析に失敗: {ex.Message}");
                }
                return;
            }

            // 自動検出: 親階層を辿ってアバタールートを探す
            var destRoot = FindAvatarRootInParent(definition.transform);
            if (destRoot != null)
            {
                result.DestinationAvatar = destRoot;
                result.DestinationHasDescriptor =
                    destRoot.GetComponent<VRC_AvatarDescriptor>() != null;
                try
                {
                    result.DestAvatarData = new AvatarData(result.DestinationAvatar);
                }
                catch (System.Exception ex)
                {
                    result.Warnings.Add($"デスティネーションアバターの解析に失敗: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 親階層を辿ってアバタールートとなるGameObjectを探す。
        /// PBRemap自身のGameObjectは除外する。
        /// 検出優先順位:
        /// 1. VRC_AvatarDescriptor（VRCアバター）
        /// 2. Animator（FBXインポート等）
        /// 3. ModularAvatarコンポーネント（MA衣装）
        /// 4. PrefabInstanceRoot（Prefabインスタンス）
        /// 5. ルートGameObject（最終手段）
        /// </summary>
        private static GameObject FindAvatarRootInParent(Transform current)
        {
            Transform parent = current.parent;

            // 第1段階: VRC_AvatarDescriptorを探す
            Transform scan = parent;
            while (scan != null)
            {
                if (scan.GetComponent<VRC_AvatarDescriptor>() != null)
                    return scan.gameObject;
                scan = scan.parent;
            }

            // 第2段階: Animatorを探す（最も上位にあるAnimatorを返す）
            scan = parent;
            GameObject animatorRoot = null;
            while (scan != null)
            {
                if (scan.GetComponent<Animator>() != null)
                    animatorRoot = scan.gameObject;
                scan = scan.parent;
            }
            if (animatorRoot != null)
                return animatorRoot;

            // 第3段階: ModularAvatarコンポーネントを探す
            #if MODULAR_AVATAR
            scan = parent;
            while (scan != null)
            {
                if (scan.GetComponent<ModularAvatarMergeArmature>() != null)
                    return scan.gameObject;
                scan = scan.parent;
            }
            #endif

            // 第4段階: Prefabインスタンスルートを探す
            if (parent != null)
            {
                var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(parent);
                if (prefabRoot != null)
                    return prefabRoot;
            }

            // 第5段階: ルートGameObject
            if (parent != null)
                return parent.root.gameObject;

            return null;
        }

        /// <summary>
        /// 子コンポーネントの外部Transform参照からソースアバターを検出する。
        /// 最も多くの参照が指しているアバタールートを返す。
        /// </summary>
        private static GameObject DetectSourceFromChildComponents(PBRemap definition)
        {
            var externalTransforms = CollectExternalTransformReferences(definition);
            if (externalTransforms.Count == 0)
                return null;

            // 各Transform参照の親を辿り、アバタールートを集計
            var avatarCounts = new Dictionary<GameObject, int>();
            foreach (var t in externalTransforms)
            {
                var avatar = FindAvatarRoot(t);
                if (avatar != null)
                {
                    avatarCounts.TryGetValue(avatar, out int count);
                    avatarCounts[avatar] = count + 1;
                }
            }

            if (avatarCounts.Count == 0)
                return null;

            // 最多のアバターを返す
            return avatarCounts.OrderByDescending(kvp => kvp.Value).First().Key;
        }

        /// <summary>
        /// PBRemap配下の全VRCコンポーネントから外部Transform参照を収集する。
        /// 内部参照（PBRemapの子孫オブジェクトへの参照）は除外する。
        /// </summary>
        public static List<Transform> CollectExternalTransformReferences(PBRemap definition)
        {
            var result = new List<Transform>();
            var definitionRoot = definition.transform;

            // PhysBone
            foreach (var pb in definitionRoot.GetComponentsInChildren<VRCPhysBone>(true))
            {
                AddIfExternal(result, pb.rootTransform, definitionRoot);
                // rootTransformがnullの場合、親ボーンが暗黙のroot
                if (pb.rootTransform == null)
                    AddIfExternal(result, pb.transform.parent, definitionRoot);
            }

            // PhysBoneCollider
            foreach (var pbc in definitionRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true))
            {
                AddIfExternal(result, pbc.rootTransform, definitionRoot);
                if (pbc.rootTransform == null)
                    AddIfExternal(result, pbc.transform.parent, definitionRoot);
            }

            // Constraint
            foreach (var constraint in definitionRoot.GetComponentsInChildren<VRCConstraintBase>(true))
            {
                AddIfExternal(result, constraint.TargetTransform, definitionRoot);

                // Sources 内の SourceTransform も収集
                var so = new SerializedObject(constraint);
                var sourcesProp = so.FindProperty("Sources");
                if (sourcesProp != null && sourcesProp.isArray)
                {
                    for (int i = 0; i < sourcesProp.arraySize; i++)
                    {
                        var element = sourcesProp.GetArrayElementAtIndex(i);
                        var srcTransformProp = element.FindPropertyRelative("SourceTransform");
                        if (srcTransformProp?.objectReferenceValue is Transform srcTransform)
                            AddIfExternal(result, srcTransform, definitionRoot);
                    }
                }
            }

            // Contact
            foreach (var contact in definitionRoot.GetComponentsInChildren<ContactBase>(true))
            {
                AddIfExternal(result, contact.rootTransform, definitionRoot);
                if (contact.rootTransform == null)
                    AddIfExternal(result, contact.transform.parent, definitionRoot);
            }

            return result;
        }

        /// <summary>
        /// Transformが外部参照（definitionRootの子孫ではない）かつnullでない場合にリストに追加する。
        /// </summary>
        private static void AddIfExternal(List<Transform> list, Transform target, Transform definitionRoot)
        {
            if (target != null && !target.IsChildOf(definitionRoot))
                list.Add(target);
        }

        /// <summary>
        /// TransformからアバタールートGameObjectを探す。
        /// 検出優先順位:
        /// 1. VRC_AvatarDescriptor（VRCアバター）
        /// 2. Animator（FBXインポート等）
        /// 3. ModularAvatarコンポーネント（MA衣装）
        /// 4. PrefabInstanceRoot（Prefabインスタンス）
        /// 5. ルートGameObject（最終手段）
        /// </summary>
        private static GameObject FindAvatarRoot(Transform bone)
        {
            // 第1段階: VRC_AvatarDescriptorを探す
            Transform current = bone;
            while (current != null)
            {
                if (current.GetComponent<VRC_AvatarDescriptor>() != null)
                    return current.gameObject;
                current = current.parent;
            }

            // 第2段階: Animatorを探す（最も上位にあるAnimatorを返す）
            current = bone;
            GameObject animatorRoot = null;
            while (current != null)
            {
                if (current.GetComponent<Animator>() != null)
                    animatorRoot = current.gameObject;
                current = current.parent;
            }
            if (animatorRoot != null)
                return animatorRoot;

            // 第3段階: ModularAvatarコンポーネントを探す
            #if MODULAR_AVATAR
            current = bone;
            while (current != null)
            {
                if (current.GetComponent<ModularAvatarMergeArmature>() != null)
                    return current.gameObject;
                current = current.parent;
            }
            #endif

            // 第4段階: Prefabインスタンスルートを探す
            var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(bone);
            if (prefabRoot != null)
                return prefabRoot;

            // 第5段階: ルートGameObject
            return bone.root.gameObject;
        }
    }
}
