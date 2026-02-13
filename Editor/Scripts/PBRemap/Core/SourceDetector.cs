using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;
using VRC.SDKBase;

namespace colloid.PBReplacer
{
    /// <summary>
    /// PBRemapの配置状態からソースアバター・デスティネーションアバターを自動検出する。
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

            // デスティネーション検出: 親階層を辿ってVRC_AvatarDescriptorを探す
            var destDescriptor = FindAvatarDescriptorInParent(definition.transform);
            if (destDescriptor != null)
            {
                result.DestinationAvatar = destDescriptor.gameObject;
                try
                {
                    result.DestAvatarData = new AvatarData(result.DestinationAvatar);
                }
                catch (System.Exception ex)
                {
                    result.Warnings.Add($"デスティネーションアバターの解析に失敗: {ex.Message}");
                }
            }

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
                        "Inspectorを開いてボーン情報を取得してください。");
                }
            }

            return Result<DetectionResult, string>.Success(result);
        }

        /// <summary>
        /// 親階層を辿ってVRC_AvatarDescriptorを持つGameObjectを探す。
        /// PBRemap自身のGameObjectは除外する。
        /// </summary>
        private static VRC_AvatarDescriptor FindAvatarDescriptorInParent(Transform current)
        {
            Transform parent = current.parent;
            while (parent != null)
            {
                var descriptor = parent.GetComponent<VRC_AvatarDescriptor>();
                if (descriptor != null)
                    return descriptor;
                parent = parent.parent;
            }
            return null;
        }

        /// <summary>
        /// 子コンポーネントの外部Transform参照からソースアバターを検出する。
        /// 最も多くの参照が指しているVRC_AvatarDescriptor配下のアバターを返す。
        /// </summary>
        private static GameObject DetectSourceFromChildComponents(PBRemap definition)
        {
            var externalTransforms = CollectExternalTransformReferences(definition);
            if (externalTransforms.Count == 0)
                return null;

            // 各Transform参照の親を辿り、VRC_AvatarDescriptorを持つアバターを集計
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
        /// TransformからVRC_AvatarDescriptorを持つアバタールートを探す。
        /// </summary>
        private static GameObject FindAvatarRoot(Transform bone)
        {
            Transform current = bone;
            while (current != null)
            {
                if (current.GetComponent<VRC_AvatarDescriptor>() != null)
                    return current.gameObject;
                current = current.parent;
            }
            return null;
        }
    }
}
