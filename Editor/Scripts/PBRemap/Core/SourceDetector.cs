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
        /// <summary>ModularAvatar MergeArmature</summary>
        MergeArmature,
        /// <summary>Prefab 境界 (Prefab Instance Root)</summary>
        PrefabBoundary,
        /// <summary>Animator（FBX 直置き等）</summary>
        Animator,
        /// <summary>transform.root（最終フォールバック）</summary>
        Root,
    }

    /// <summary>
    /// PBRemapの配置状態からソースアバター・デスティネーションアバターを自動検出する。
    /// AvatarDescriptorが無い場合のフォールバック検出にも対応する。
    /// 検出優先順位: 手動指定 → MA(MergeArmature) → Prefab境界(PBRemap自身の最内Prefabは除外)
    ///   → VRC_AvatarDescriptor → Animator → root
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

            /// <summary>デスティネーションアバターがどの検出経路で特定されたか</summary>
            public AvatarDetectionMethod DestinationDetectionMethod { get; set; }

            /// <summary>ソースアバターがどの検出経路で特定されたか</summary>
            public AvatarDetectionMethod SourceDetectionMethod { get; set; }
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
            GameObject sourceAvatar;
            AvatarDetectionMethod sourceMethod;
            if (definition.SourceRootOverride != null)
            {
                sourceAvatar = definition.SourceRootOverride;
                sourceMethod = AvatarDetectionMethod.Manual;
            }
            else
            {
                sourceAvatar = DetectSourceFromChildComponents(definition, out sourceMethod);
            }

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
                    result.SourceDetectionMethod = sourceMethod;
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
        /// 優先順位: 手動指定 → MA(MergeArmature) → Prefab境界(PBRemap自身の最内Prefabは除外)
        ///   → VRC_AvatarDescriptor → Animator → root
        /// </summary>
        private static void DetectDestination(PBRemap definition, DetectionResult result)
        {
            if (definition.DestinationRootOverride != null)
            {
                AssignDestination(definition.DestinationRootOverride, result);
                result.DestinationDetectionMethod = AvatarDetectionMethod.Manual;
                return;
            }

            var destRoot = FindAvatarRoot(definition.transform, includeSelf: false, out var method);
            if (destRoot != null)
            {
                AssignDestination(destRoot, result);
                result.DestinationDetectionMethod = method;
            }
        }

        /// <summary>
        /// 検出したデスティネーションアバターを DetectionResult に設定し、AvatarData を構築する。
        /// DestinationDetectionMethod は呼び出し側で設定する。
        /// </summary>
        private static void AssignDestination(GameObject avatar, DetectionResult result)
        {
            result.DestinationAvatar = avatar;
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
        /// SourceDetectionMethod は呼び出し側で設定する。
        /// </summary>
        private static void AssignSource(GameObject avatar, DetectionResult result)
        {
            result.SourceAvatar = avatar;
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
        /// 1. ModularAvatarMergeArmature (祖先自身またはその子孫に存在すれば、その祖先を返す)
        /// 2. Prefab 境界 (PBRemap 自身の最内 Prefab はスキップし、その外側の Prefab から探索)
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
        /// <param name="method">検出に使われたメソッドを out で返す</param>
        private static GameObject FindAvatarRoot(Transform start, bool includeSelf, out AvatarDetectionMethod method)
        {
            method = AvatarDetectionMethod.None;
            if (start == null)
                return null;

            Transform scanStart = includeSelf ? start : start.parent;

            // 1. ModularAvatarMergeArmature
            // MA は通常 衣装 → Armature(MergeArmature) の形で衣装の子孫に付くため、
            // 祖先を辿りつつ、各祖先の子孫全体に MA が含まれるかチェックする。
            // PBRemap に最も近い祖先から順に見るので、衣装 root が最内側で確定する。
            // Prefab 境界より先に見る: Avatar(Prefab) 内にネストした MA 衣装 Prefab に
            // PBRemap がある場合、Prefab 境界優先では外側 Avatar が返ってしまうため。
            #if MODULAR_AVATAR
            for (Transform scan = scanStart; scan != null; scan = scan.parent)
            {
                if (scan.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null)
                {
                    method = AvatarDetectionMethod.MergeArmature;
                    return scan.gameObject;
                }
            }
            #endif

            // 2. Prefab 境界（Destination: PBRemap 自身の最内 Prefab を候補から除外）
            // AvatarDynamics 自身が Prefab 化されてネストしているケースで、
            // 内側の AvatarDynamics Prefab を誤検知するのを避ける。
            Transform prefabScanStart = includeSelf ? start : SkipOwnPrefab(start);
            for (Transform scan = prefabScanStart; scan != null; scan = scan.parent)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(scan.gameObject))
                {
                    method = AvatarDetectionMethod.PrefabBoundary;
                    return scan.gameObject;
                }
            }

            // 3. VRC_AvatarDescriptor
            for (Transform scan = scanStart; scan != null; scan = scan.parent)
            {
                if (scan.GetComponent<VRC_AvatarDescriptor>() != null)
                {
                    method = AvatarDetectionMethod.VRCAvatarDescriptor;
                    return scan.gameObject;
                }
            }

            // 4. Animator（最上位）
            GameObject animatorRoot = null;
            for (Transform scan = scanStart; scan != null; scan = scan.parent)
            {
                if (scan.GetComponent<Animator>() != null)
                    animatorRoot = scan.gameObject;
            }
            if (animatorRoot != null)
            {
                method = AvatarDetectionMethod.Animator;
                return animatorRoot;
            }

            // 5. root
            if (scanStart != null)
            {
                method = AvatarDetectionMethod.Root;
                return scanStart.root.gameObject;
            }

            return null;
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
        /// 最も多くの参照が指しているアバタールートを返す。検出経路も out で返す。
        /// </summary>
        private static GameObject DetectSourceFromChildComponents(PBRemap definition, out AvatarDetectionMethod method)
        {
            method = AvatarDetectionMethod.None;
            var externalTransforms = CollectExternalTransformReferences(definition);
            if (externalTransforms.Count == 0)
                return null;

            // 各Transform参照の親を辿り、アバタールートとその検出経路を集計
            var avatarCounts = new Dictionary<GameObject, int>();
            var avatarMethods = new Dictionary<GameObject, AvatarDetectionMethod>();
            foreach (var t in externalTransforms)
            {
                var avatar = FindAvatarRoot(t, includeSelf: true, out var m);
                if (avatar != null)
                {
                    avatarCounts.TryGetValue(avatar, out int count);
                    avatarCounts[avatar] = count + 1;
                    // 同一アバターに複数の経路で辿り着いた場合、最初の経路を採用
                    if (!avatarMethods.ContainsKey(avatar))
                        avatarMethods[avatar] = m;
                }
            }

            if (avatarCounts.Count == 0)
                return null;

            // 最多のアバターを返す
            var winner = avatarCounts.OrderByDescending(kvp => kvp.Value).First().Key;
            method = avatarMethods[winner];
            return winner;
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
