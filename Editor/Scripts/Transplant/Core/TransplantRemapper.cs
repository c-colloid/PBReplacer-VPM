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
    /// 移植リマップ結果データ
    /// </summary>
    public class RemapResult
    {
        public int RemappedComponentCount { get; set; }
        public int RemappedReferenceCount { get; set; }
        public int UnresolvedReferenceCount { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// 既にHierarchy上に存在するコンポーネントのTransform参照をリマップする。
    /// 既存コンポーネントの参照書き換えに特化しており、
    /// Live（同一シーン）モードとPrefab（シリアライズデータ）モードの両方に対応する。
    /// </summary>
    public static class TransplantRemapper
    {
        /// <summary>
        /// TransplantDefinition配下のコンポーネントの外部Transform参照を
        /// デスティネーションアバターのボーンにリマップする。
        /// </summary>
        /// <param name="definition">TransplantDefinition</param>
        /// <returns>リマップ結果またはエラーメッセージ</returns>
        public static Result<RemapResult, string> Remap(TransplantDefinition definition)
        {
            // 検出
            var detectResult = SourceDetector.Detect(definition);
            if (detectResult.IsFailure)
                return Result<RemapResult, string>.Failure(detectResult.Error);

            var detection = detectResult.Value;

            if (detection.DestinationAvatar == null)
                return Result<RemapResult, string>.Failure(
                    "デスティネーションアバターが検出できません。" +
                    "TransplantDefinitionをアバターの子階層に配置してください。");

            if (detection.DestAvatarData == null)
                return Result<RemapResult, string>.Failure(
                    "デスティネーションアバターのArmatureを検出できません。");

            // モード分岐
            if (detection.IsLiveMode)
                return RemapLiveMode(definition, detection);
            else
                return RemapPrefabMode(definition, detection);
        }

        /// <summary>
        /// 同一シーンモード: 子コンポーネントのTransform参照が生きている場合。
        /// ソースアバターのAnimator/Armatureを直接利用してボーンマップを構築する。
        /// </summary>
        private static Result<RemapResult, string> RemapLiveMode(
            TransplantDefinition definition,
            SourceDetector.DetectionResult detection)
        {
            if (detection.SourceAvatarData == null)
                return Result<RemapResult, string>.Failure("ソースアバターのデータを取得できません。");

            var sourceData = detection.SourceAvatarData;
            var destData = detection.DestAvatarData;
            var remapRules = definition.PathRemapRules?.ToList();

            // スケールファクター算出
            float scaleFactor = CalculateScaleFactor(definition, sourceData, destData);

            // ボーンマップ構築（既存BoneMapperを利用）
            var boneMap = BuildBoneMap(sourceData, destData, remapRules);

            // リマップ実行
            return ExecuteRemap(definition, boneMap, scaleFactor);
        }

        /// <summary>
        /// Prefabモード: SerializedBoneReferenceからリマップする。
        /// Transform参照がnullのため、シリアライズ済みボーンパスとHumanoid情報から解決する。
        /// </summary>
        private static Result<RemapResult, string> RemapPrefabMode(
            TransplantDefinition definition,
            SourceDetector.DetectionResult detection)
        {
            if (definition.SerializedBoneReferences.Count == 0)
                return Result<RemapResult, string>.Failure(
                    "シリアライズされたボーン参照データがありません。" +
                    "ソースアバターのシーンでInspectorを開いてからPrefab化してください。");

            var destData = detection.DestAvatarData;

            // スケールファクター算出（Prefab: ソース側はシリアライズ値を使用）
            float scaleFactor;
            if (definition.AutoCalculateScale && definition.SourceAvatarScale > 0)
            {
                float destScale = CalculateAvatarScale(destData);
                scaleFactor = destScale / definition.SourceAvatarScale;
            }
            else
            {
                scaleFactor = definition.ScaleFactor;
            }

            // シリアライズデータからのリマップ
            return ExecuteRemapFromSerialized(definition, destData, scaleFactor);
        }

        #region ボーンマップ構築

        /// <summary>
        /// ソースとデスティネーション間の完全なボーンマップを構築する。
        /// </summary>
        private static Dictionary<Transform, Transform> BuildBoneMap(
            AvatarData sourceData, AvatarData destData, List<PathRemapRule> remapRules)
        {
            var boneMap = new Dictionary<Transform, Transform>();
            Transform sourceArmature = sourceData.Armature.transform;
            Transform destArmature = destData.Armature.transform;
            Animator sourceAnimator = sourceData.AvatarAnimator;
            Animator destAnimator = destData.AvatarAnimator;

            // Humanoidボーンマップを先に構築
            if (sourceAnimator != null && destAnimator != null
                && sourceAnimator.isHuman && destAnimator.isHuman)
            {
                var humanoidMap = BoneMapper.BuildHumanoidBoneMap(sourceAnimator, destAnimator);
                foreach (var kvp in humanoidMap)
                    boneMap[kvp.Key] = kvp.Value;
            }

            // ソースArmature配下の全Transformをリマップ付きで解決
            var allSourceBones = sourceArmature.GetComponentsInChildren<Transform>(true);
            foreach (var srcBone in allSourceBones)
            {
                if (boneMap.ContainsKey(srcBone))
                    continue;

                var resolveResult = (remapRules != null && remapRules.Count > 0)
                    ? BoneMapper.ResolveBoneWithRemap(
                        srcBone, sourceArmature, destArmature,
                        remapRules, sourceAnimator, destAnimator)
                    : BoneMapper.ResolveBone(
                        srcBone, sourceArmature, destArmature,
                        sourceAnimator, destAnimator);

                if (resolveResult.IsSuccess)
                    boneMap[srcBone] = resolveResult.Value;
            }

            return boneMap;
        }

        #endregion

        #region リマップ実行（同一シーンモード）

        /// <summary>
        /// ボーンマップを使って全コンポーネントの外部Transform参照をリマップする。
        /// </summary>
        private static Result<RemapResult, string> ExecuteRemap(
            TransplantDefinition definition,
            Dictionary<Transform, Transform> boneMap,
            float scaleFactor)
        {
            var result = new RemapResult();
            var definitionRoot = definition.transform;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Transplant Remap");

            try
            {
                // 全VRCコンポーネントをリマップ
                var allComponents = CollectVRCComponents(definitionRoot);
                foreach (var component in allComponents)
                {
                    int remapped = RemapComponentReferences(component, boneMap, definitionRoot);
                    if (remapped > 0)
                    {
                        result.RemappedComponentCount++;
                        result.RemappedReferenceCount += remapped;
                    }

                    // rootTransformがnull && 親が外部参照の場合、明示的に設定
                    ResolveNullRootTransform(component, boneMap, definitionRoot);

                    // スケール適用
                    ApplyScaleFactor(component, scaleFactor);
                }

                // Constraint Sources: RemapComponentReferences の汎用走査で基本的にリマップ済みだが、
                // VRC SDK固有のシリアライズ形式のフォールバックとして明示的にも実行する。
                foreach (var constraint in definitionRoot.GetComponentsInChildren<VRCConstraintBase>(true))
                {
                    RemapConstraintSources(constraint, boneMap);
                }
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                return Result<RemapResult, string>.Failure($"リマップ中にエラーが発生しました: {ex.Message}");
            }

            Undo.CollapseUndoOperations(undoGroup);
            return Result<RemapResult, string>.Success(result);
        }

        /// <summary>
        /// 単一コンポーネントの外部Transform参照をリマップする。
        /// 内部参照（TransplantDefinitionの子孫への参照）はスキップする。
        /// </summary>
        /// <returns>リマップした参照数</returns>
        private static int RemapComponentReferences(
            Component component,
            Dictionary<Transform, Transform> boneMap,
            Transform definitionRoot)
        {
            var so = new SerializedObject(component);
            SerializedProperty prop = so.GetIterator();
            int remapCount = 0;

            while (prop.Next(true))
            {
                if (prop.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                var objRef = prop.objectReferenceValue as Transform;
                if (objRef == null)
                    continue;

                // 内部参照はスキップ
                if (objRef.IsChildOf(definitionRoot))
                    continue;

                if (boneMap.TryGetValue(objRef, out Transform mapped))
                {
                    Undo.RecordObject(component, "Remap Transform Reference");
                    prop.objectReferenceValue = mapped;
                    remapCount++;
                }
            }

            if (remapCount > 0)
                so.ApplyModifiedProperties();

            return remapCount;
        }

        #endregion

        #region リマップ実行（Prefabモード）

        /// <summary>
        /// シリアライズデータからリマップを実行する。
        /// Humanoid祖先 + 相対パス → フルパス + リマップルール → 名前マッチの4段階で解決する。
        /// </summary>
        private static Result<RemapResult, string> ExecuteRemapFromSerialized(
            TransplantDefinition definition,
            AvatarData destData,
            float scaleFactor)
        {
            var result = new RemapResult();
            var definitionRoot = definition.transform;
            var destArmature = destData.Armature.transform;
            var destAnimator = destData.AvatarAnimator;
            var remapRules = definition.PathRemapRules?.ToList();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Transplant Remap (Prefab)");

            try
            {
                foreach (var boneRef in definition.SerializedBoneReferences)
                {
                    // コンポーネントを特定
                    var targetTransform = definitionRoot.Find(boneRef.componentObjectPath);
                    if (targetTransform == null)
                    {
                        result.Warnings.Add($"オブジェクト '{boneRef.componentObjectPath}' が見つかりません");
                        result.UnresolvedReferenceCount++;
                        continue;
                    }

                    var component = FindComponentByTypeName(targetTransform.gameObject, boneRef.componentTypeName);
                    if (component == null)
                    {
                        result.Warnings.Add($"コンポーネント '{boneRef.componentTypeName}' が " +
                            $"'{boneRef.componentObjectPath}' に見つかりません");
                        result.UnresolvedReferenceCount++;
                        continue;
                    }

                    // ボーン解決（4段階戦略）
                    Transform resolvedBone = ResolveBoneFromSerialized(
                        boneRef, destArmature, destAnimator, remapRules);

                    if (resolvedBone != null)
                    {
                        // SerializedPropertyで該当フィールドに設定
                        var so = new SerializedObject(component);
                        var prop = so.FindProperty(boneRef.propertyPath);
                        if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            Undo.RecordObject(component, "Remap Bone Reference");
                            prop.objectReferenceValue = resolvedBone;
                            so.ApplyModifiedProperties();
                            result.RemappedReferenceCount++;
                            result.RemappedComponentCount++;
                        }
                    }
                    else
                    {
                        result.Warnings.Add($"ボーン '{boneRef.boneRelativePath}' を解決できません " +
                            $"({boneRef.componentObjectPath}/{boneRef.componentTypeName}.{boneRef.propertyPath})");
                        result.UnresolvedReferenceCount++;
                    }
                }

                // スケール適用
                var allComponents = CollectVRCComponents(definitionRoot);
                foreach (var component in allComponents)
                {
                    ApplyScaleFactor(component, scaleFactor);
                }

                // Constraint Sources (TargetTransform, Sources[i].SourceTransform) は
                // ScanComponentReferences の汎用プロパティ走査で既にシリアライズされており、
                // 上記の SerializedBoneReference ループ内で自動的にリマップされる。
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                return Result<RemapResult, string>.Failure($"リマップ中にエラーが発生しました: {ex.Message}");
            }

            Undo.CollapseUndoOperations(undoGroup);
            return Result<RemapResult, string>.Success(result);
        }

        /// <summary>
        /// シリアライズされたボーン情報からデスティネーションのボーンを解決する。
        /// 4段階戦略: 直接Humanoid → Humanoid祖先+相対パス → フルパス+リマップ → 名前マッチ
        /// </summary>
        private static Transform ResolveBoneFromSerialized(
            SerializedBoneReference boneRef,
            Transform destArmature,
            Animator destAnimator,
            List<PathRemapRule> remapRules)
        {
            // 戦略1: 直接Humanoidマッピング
            if (boneRef.humanBodyBone != HumanBodyBones.LastBone
                && destAnimator != null && destAnimator.isHuman)
            {
                var bone = destAnimator.GetBoneTransform(boneRef.humanBodyBone);
                if (bone != null)
                    return bone;
            }

            // 戦略2: Humanoid祖先 + 相対パス
            if (boneRef.nearestHumanoidAncestor != HumanBodyBones.LastBone
                && !string.IsNullOrEmpty(boneRef.pathFromHumanoidAncestor)
                && destAnimator != null && destAnimator.isHuman)
            {
                var ancestorBone = destAnimator.GetBoneTransform(boneRef.nearestHumanoidAncestor);
                if (ancestorBone != null)
                {
                    var resolved = ancestorBone.Find(boneRef.pathFromHumanoidAncestor);
                    if (resolved != null)
                        return resolved;
                }
            }

            // 戦略3: フルパスマッチ + PathRemapRules
            if (!string.IsNullOrEmpty(boneRef.boneRelativePath))
            {
                // まず直接パスマッチ
                var directMatch = BoneMapper.FindBoneByRelativePath(boneRef.boneRelativePath, destArmature);
                if (directMatch != null)
                    return directMatch;

                // リマップルール適用
                if (remapRules != null && remapRules.Count > 0)
                {
                    string remappedPath = BoneMapper.ApplyRemapRules(boneRef.boneRelativePath, remapRules);
                    var remappedMatch = BoneMapper.FindBoneByRelativePath(remappedPath, destArmature);
                    if (remappedMatch != null)
                        return remappedMatch;
                }
            }

            // 戦略4: 名前マッチ（フォールバック）
            if (!string.IsNullOrEmpty(boneRef.boneRelativePath))
            {
                string[] segments = boneRef.boneRelativePath.Split('/');
                string boneName = segments[segments.Length - 1];

                // リマップルール適用後の名前でもマッチ
                if (remapRules != null && remapRules.Count > 0)
                {
                    foreach (var rule in remapRules)
                        boneName = rule.Apply(boneName);
                }

                var allDestBones = destArmature.GetComponentsInChildren<Transform>(true);
                var nameMatch = allDestBones.FirstOrDefault(t => t.name == boneName);
                if (nameMatch != null)
                    return nameMatch;
            }

            return null;
        }

        /// <summary>
        /// 型名からコンポーネントを検索する。
        /// </summary>
        private static Component FindComponentByTypeName(GameObject obj, string typeName)
        {
            foreach (var component in obj.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name == typeName)
                    return component;
            }
            return null;
        }

        #endregion

        #region 共通ヘルパー

        /// <summary>
        /// TransplantDefinition配下の全VRCコンポーネントを収集する。
        /// </summary>
        private static List<Component> CollectVRCComponents(Transform root)
        {
            var components = new List<Component>();
            components.AddRange(root.GetComponentsInChildren<VRCPhysBone>(true));
            components.AddRange(root.GetComponentsInChildren<VRCPhysBoneCollider>(true));
            components.AddRange(root.GetComponentsInChildren<VRCConstraintBase>(true));
            components.AddRange(root.GetComponentsInChildren<ContactBase>(true));
            return components;
        }

        /// <summary>
        /// rootTransformがnullのコンポーネントに対し、ボーンマップから適切なボーンを設定する。
        /// VRC SDKではrootTransformがnullの場合、コンポーネントの親がrootとして扱われるが、
        /// 移植後はAvatarDynamics配下に配置されるため明示的な設定が必要。
        /// </summary>
        private static void ResolveNullRootTransform(
            Component component, Dictionary<Transform, Transform> boneMap, Transform definitionRoot)
        {
            Transform parentBone = component.transform.parent;
            // 親がdefinitionRoot配下（= AvatarDynamicsフォルダ内）の場合は
            // 元々のソースでもフォルダ内だったので、外部参照の解決は不要
            if (parentBone == null || parentBone.IsChildOf(definitionRoot))
                return;

            if (!boneMap.TryGetValue(parentBone, out Transform destBone))
                return;

            switch (component)
            {
                case VRCPhysBone pb when pb.rootTransform == null:
                    Undo.RecordObject(pb, "Set rootTransform");
                    pb.rootTransform = destBone;
                    break;
                case VRCPhysBoneCollider pbc when pbc.rootTransform == null:
                    Undo.RecordObject(pbc, "Set rootTransform");
                    pbc.rootTransform = destBone;
                    break;
                case ContactBase contact when contact.rootTransform == null:
                    Undo.RecordObject(contact, "Set rootTransform");
                    contact.rootTransform = destBone;
                    break;
            }
        }

        /// <summary>
        /// VRCConstraintBaseのSources内のSourceTransformとTargetTransformをリマップする。
        /// </summary>
        private static void RemapConstraintSources(
            VRCConstraintBase constraint,
            Dictionary<Transform, Transform> boneMap)
        {
            if (constraint == null) return;

            Undo.RecordObject(constraint, "Remap Constraint Sources");

            // TargetTransformの置換
            if (constraint.TargetTransform != null
                && boneMap.TryGetValue(constraint.TargetTransform, out Transform newTarget))
            {
                constraint.TargetTransform = newTarget;
            }

            // Sources内のSourceTransformを置換
            var so = new SerializedObject(constraint);
            var sourcesProp = so.FindProperty("Sources");
            if (sourcesProp != null && sourcesProp.isArray)
            {
                bool changed = false;
                for (int i = 0; i < sourcesProp.arraySize; i++)
                {
                    var element = sourcesProp.GetArrayElementAtIndex(i);
                    var srcTransformProp = element.FindPropertyRelative("SourceTransform");
                    if (srcTransformProp != null
                        && srcTransformProp.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var srcTransform = srcTransformProp.objectReferenceValue as Transform;
                        if (srcTransform != null && boneMap.TryGetValue(srcTransform, out Transform mapped))
                        {
                            srcTransformProp.objectReferenceValue = mapped;
                            changed = true;
                        }
                    }
                }

                if (changed)
                    so.ApplyModifiedProperties();
            }
        }

        /// <summary>
        /// スケールファクターをコンポーネントのパラメータに適用する。
        /// </summary>
        private static void ApplyScaleFactor(Component component, float scaleFactor)
        {
            if (Mathf.Approximately(scaleFactor, 1.0f))
                return;

            Undo.RecordObject(component, "Apply Scale Factor");

            switch (component)
            {
                case VRCPhysBone pb:
                    pb.radius = ScaleCalculator.ScaleValue(pb.radius, scaleFactor);
                    pb.endpointPosition = ScaleCalculator.ScaleVector3(pb.endpointPosition, scaleFactor);
                    break;
                case VRCPhysBoneCollider pbc:
                    pbc.radius = ScaleCalculator.ScaleValue(pbc.radius, scaleFactor);
                    pbc.height = ScaleCalculator.ScaleValue(pbc.height, scaleFactor);
                    pbc.position = ScaleCalculator.ScaleVector3(pbc.position, scaleFactor);
                    break;
                case ContactBase contact:
                    contact.radius = ScaleCalculator.ScaleValue(contact.radius, scaleFactor);
                    contact.height = ScaleCalculator.ScaleValue(contact.height, scaleFactor);
                    contact.position = ScaleCalculator.ScaleVector3(contact.position, scaleFactor);
                    break;
            }
        }

        /// <summary>
        /// TransplantDefinitionの設定に基づいてスケールファクターを算出する。
        /// </summary>
        private static float CalculateScaleFactor(
            TransplantDefinition definition, AvatarData sourceData, AvatarData destData)
        {
            if (!definition.AutoCalculateScale)
                return definition.ScaleFactor;

            return ScaleCalculator.CalculateScaleFactor(
                sourceData.Armature.transform,
                destData.Armature.transform,
                sourceData.AvatarAnimator,
                destData.AvatarAnimator);
        }

        /// <summary>
        /// AvatarDataからスケール基準値（Hips→Head距離 or lossyScale.y）を算出する。
        /// </summary>
        public static float CalculateAvatarScale(AvatarData avatarData)
        {
            var animator = avatarData.AvatarAnimator;
            if (animator != null && animator.isHuman)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (hips != null && head != null)
                {
                    float distance = Vector3.Distance(hips.position, head.position);
                    if (distance > 1e-6f)
                        return distance;
                }
            }

            return avatarData.Armature.transform.lossyScale.y;
        }

        #endregion
    }
}
