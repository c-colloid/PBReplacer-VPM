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
    /// 検出優先順位: 手動指定 → Prefab境界(PBRemap自身の最内Prefabは除外)
    ///   → MA(MergeArmature) → VRC_AvatarDescriptor → Animator → root
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

            // ソース検出: 手動指定 → 自動検出 → Prefabモード候補
            GameObject sourceAvatar = definition.SourceRootOverride != null
                ? definition.SourceRootOverride
                : DetectSourceFromChildComponents(definition);

            if (sourceAvatar != null)
            {
                // ソースとデスティネーションが同じアバターの場合は移植済み状態
                if (result.DestinationAvatar != null && sourceAvatar == result.DestinationAvatar)
                {
                    result.IsReferencingDestination = true;
                }
                else
                {
                    AssignSource(sourceAvatar, result);
                }
            }
            else
            {
                // 参照もoverrideもない → Prefabモードの可能性
                result.IsLiveMode = false;
                if (definition.SerializedBoneReferences.Count == 0)
                {
                    result.Warnings.Add("ソースアバターを検出できません。" +
                        "子コンポーネントのTransform参照がないか、シリアライズデータがありません。" +
                        "手動で移植元を指定するか、Inspectorを開いてボーン情報を取得してください。");
                }
            }

            return Result<DetectionResult, string>.Success(result);
        }

        /// <summary>
        /// デスティネーションアバターを検出する。
        /// 優先順位: 手動指定 → Prefab境界(PBRemap自身の最内Prefabは除外)
        ///   → MA(MergeArmature) → VRC_AvatarDescriptor → Animator → root
        /// </summary>
        private static void DetectDestination(PBRemap definition, DetectionResult result)
        {
            if (definition.DestinationRootOverride != null)
            {
                AssignDestination(definition.DestinationRootOverride, result);
                return;
            }

            var destRoot = FindAvatarRoot(definition.transform, includeSelf: false);
            if (destRoot != null)
                AssignDestination(destRoot, result);
        }

        /// <summary>
        /// 検出したデスティネーションアバターを DetectionResult に設定し、AvatarData を構築する。
        /// </summary>
        private static void AssignDestination(GameObject avatar, DetectionResult result)
        {
            result.DestinationAvatar = avatar;
            result.DestinationHasDescriptor =
                avatar.GetComponent<VRC_AvatarDescriptor>() != null;
            try
            {
                result.DestAvatarData = new AvatarData(avatar);
            }
            catch (System.Exception ex)
            {
                result.Warnings.Add($"デスティネーションアバターの解析に失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 検出したソースアバターを DetectionResult に設定し、AvatarData を構築する。
        /// </summary>
        private static void AssignSource(GameObject avatar, DetectionResult result)
        {
            result.SourceAvatar = avatar;
            result.SourceHasDescriptor =
                avatar.GetComponent<VRC_AvatarDescriptor>() != null;
            result.IsLiveMode = true;
            try
            {
                result.SourceAvatarData = new AvatarData(avatar);
            }
            catch (System.Exception ex)
            {
                result.Warnings.Add($"ソースアバターの解析に失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// Transform を起点に祖先を走査してアバタールートを返す。
        /// 優先順位:
        /// 1. Prefab 境界 (PBRemap 自身の最内 Prefab はスキップし、その外側の Prefab から探索)
        /// 2. ModularAvatarMergeArmature (祖先自身またはその子孫に存在すれば、その祖先を返す)
        /// 3. VRC_AvatarDescriptor (祖先走査)
        /// 4. Animator（最上位を優先。FBX 直移植対応）
        /// 5. transform.root （最終手段）
        /// </summary>
        /// <param name="start">走査開始 Transform</param>
        /// <param name="includeSelf">
        ///   start 自身も候補に含めるか。
        ///   Destination 検出は PBRemap 自身を除外するため false、
        ///   Source 検出は参照 Transform がアバタールートを指す可能性があるため true。
        /// </param>
        private static GameObject FindAvatarRoot(Transform start, bool includeSelf)
        {
            if (start == null)
                return null;

            // 1. Prefab 境界（Destination: PBRemap 自身の最内 Prefab を候補から除外）
            // AvatarDynamics 自身が Prefab 化されてネストしているケースで、
            // 内側の AvatarDynamics Prefab を誤検知するのを避ける。
            Transform prefabScanStart = includeSelf ? start : SkipOwnPrefab(start);
            for (Transform scan = prefabScanStart; scan != null; scan = scan.parent)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(scan.gameObject))
                    return scan.gameObject;
            }

            Transform scanStart = includeSelf ? start : start.parent;

            // 2. ModularAvatarMergeArmature
            // MA は通常 衣装 → Armature(MergeArmature) の形で衣装の子孫に付くため、
            // 祖先を辿りつつ、各祖先の子孫全体に MA が含まれるかチェックする。
            // PBRemap に最も近い祖先から順に見るので、衣装 root が最内側で確定する。
            // (Unpack 済み階層や中間コンテナが挟まる階層にも対応)
            #if MODULAR_AVATAR
            for (Transform scan = scanStart; scan != null; scan = scan.parent)
            {
                if (scan.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null)
                    return scan.gameObject;
            }
            #endif

            // 3. VRC_AvatarDescriptor
            for (Transform scan = scanStart; scan != null; scan = scan.parent)
            {
                if (scan.GetComponent<VRC_AvatarDescriptor>() != null)
                    return scan.gameObject;
            }

            // 4. Animator（最上位）
            GameObject animatorRoot = null;
            for (Transform scan = scanStart; scan != null; scan = scan.parent)
            {
                if (scan.GetComponent<Animator>() != null)
                    animatorRoot = scan.gameObject;
            }
            if (animatorRoot != null)
                return animatorRoot;

            // 5. root
            return scanStart != null ? scanStart.root.gameObject : null;
        }

        /// <summary>
        /// Destination 検出用の Prefab 境界走査開始点を返す。
        /// PBRemap を含む最内 Prefab が存在すればその親から走査を始め、
        /// AvatarDynamics 自身が Prefab 化されているケースで内側 Prefab を誤検知しないようにする。
        /// </summary>
        private static Transform SkipOwnPrefab(Transform start)
        {
            var ownPrefab = PrefabUtility.GetNearestPrefabInstanceRoot(start.gameObject);
            if (ownPrefab != null)
                return ownPrefab.transform.parent;
            return start.parent;
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
                var avatar = FindAvatarRoot(t, includeSelf: true);
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
    }
}
