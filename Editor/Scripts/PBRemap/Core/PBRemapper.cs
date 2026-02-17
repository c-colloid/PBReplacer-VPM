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
        public int AutoCreatedObjectCount { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// 既にHierarchy上に存在するコンポーネントのTransform参照をリマップする。
    /// 既存コンポーネントの参照書き換えに特化しており、
    /// Live（同一シーン）モードとPrefab（シリアライズデータ）モードの両方に対応する。
    /// </summary>
    public static class PBRemapper
    {
        /// <summary>
        /// PBRemap配下のコンポーネントの外部Transform参照を
        /// デスティネーションアバターのボーンにリマップする。
        /// </summary>
        /// <param name="definition">PBRemap</param>
        /// <returns>リマップ結果またはエラーメッセージ</returns>
        public static Result<RemapResult, string> Remap(PBRemap definition)
        {
            // 検出
            var detectResult = SourceDetector.Detect(definition);
            if (detectResult.IsFailure)
                return Result<RemapResult, string>.Failure(detectResult.Error);

            var detection = detectResult.Value;

            if (detection.DestinationAvatar == null)
                return Result<RemapResult, string>.Failure(
                    "デスティネーションアバターが検出できません。" +
                    "PBRemapをアバターの子階層に配置してください。");

            if (detection.DestAvatarData == null)
                return Result<RemapResult, string>.Failure(
                    "デスティネーションアバターのArmatureを検出できません。");

            // モード分岐
            if (detection.IsLiveMode)
                return RemapLiveMode(definition, detection, detection.SourceAvatar);
            else
                return RemapPrefabMode(definition, detection);
        }

        /// <summary>
        /// 同一シーンモード: 子コンポーネントのTransform参照が生きている場合。
        /// ソースアバターのAnimator/Armatureを直接利用してボーンマップを構築する。
        /// </summary>
        private static Result<RemapResult, string> RemapLiveMode(
            PBRemap definition,
            SourceDetector.DetectionResult detection,
            GameObject sourceAvatar)
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

            // ヘルパーオブジェクトの自動作成
            int autoCreated = AutoCreateHelperObjects(
                definition, sourceAvatar, sourceData, destData, boneMap, remapRules);

            // リマップ実行
            var result = ExecuteRemap(definition, boneMap, scaleFactor);
            if (result.IsSuccess)
                result.Value.AutoCreatedObjectCount = autoCreated;
            return result;
        }

        /// <summary>
        /// Prefabモード: SerializedBoneReferenceからリマップする。
        /// Transform参照がnullのため、シリアライズ済みボーンパスとHumanoid情報から解決する。
        /// </summary>
        private static Result<RemapResult, string> RemapPrefabMode(
            PBRemap definition,
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

        /// <summary>
        /// 未解決のヘルパーオブジェクト（スケルトンボーンでないもの）を
        /// デスティネーション側に自動作成し、ボーンマップに追加する。
        /// </summary>
        /// <returns>自動作成したオブジェクト数</returns>
        private static int AutoCreateHelperObjects(
            PBRemap definition,
            GameObject sourceAvatar,
            AvatarData sourceData,
            AvatarData destData,
            Dictionary<Transform, Transform> boneMap,
            List<PathRemapRule> remapRules)
        {
            var sourceArmature = sourceData.Armature.transform;
            var destArmature = destData.Armature.transform;
            var skinnedBones = BoneMapper.CollectSkinnedBones(sourceAvatar);

            // 外部参照を収集し、深さ順（浅い方から）にソートして親子関係を保持
            var externalRefs = SourceDetector.CollectExternalTransformReferences(definition);
            var unresolvedBones = new List<Transform>();
            var processed = new HashSet<Transform>();

            foreach (var bone in externalRefs)
            {
                if (processed.Contains(bone))
                    continue;
                processed.Add(bone);

                // 既にboneMapで解決済みならスキップ
                if (boneMap.ContainsKey(bone))
                    continue;

                // スケルトンボーンは自動作成対象外
                if (BoneMapper.IsSkeletonBone(bone, skinnedBones))
                    continue;

                unresolvedBones.Add(bone);
            }

            // 深さ順にソート（浅い方から処理し、作成した親を後続で参照可能にする）
            unresolvedBones.Sort((a, b) => GetDepth(a, sourceArmature) - GetDepth(b, sourceArmature));

            int autoCreated = 0;

            foreach (var bone in unresolvedBones)
            {
                // 前のイテレーションで作成された親により、既にboneMapに入っている場合
                if (boneMap.ContainsKey(bone))
                    continue;

                // 親がソースArmature配下であること
                if (bone.parent == null || !bone.parent.IsChildOf(sourceArmature))
                    continue;

                // 親がboneMapで解決可能か
                if (!boneMap.TryGetValue(bone.parent, out Transform destParent))
                    continue;

                // destParent配下に既に同名の子がある場合はそれを使う
                Transform existing = destParent.Find(bone.name);
                if (existing != null)
                {
                    boneMap[bone] = existing;
                    continue;
                }

                // 新規GameObjectを作成
                var newObj = new GameObject(bone.name);
                newObj.transform.SetParent(destParent, false);
                Undo.RegisterCreatedObjectUndo(newObj, "Auto-create helper object");

                boneMap[bone] = newObj.transform;
                autoCreated++;
            }

            return autoCreated;
        }

        /// <summary>
        /// armatureRootからの深さを返す。
        /// </summary>
        private static int GetDepth(Transform bone, Transform armatureRoot)
        {
            int depth = 0;
            Transform current = bone;
            while (current != null && current != armatureRoot)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        #endregion

        #region リマップ実行（同一シーンモード）

        /// <summary>
        /// ボーンマップを使って全コンポーネントの外部Transform参照をリマップする。
        /// </summary>
        private static Result<RemapResult, string> ExecuteRemap(
            PBRemap definition,
            Dictionary<Transform, Transform> boneMap,
            float scaleFactor)
        {
            var result = new RemapResult();
            var definitionRoot = definition.transform;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("PBRemap");

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
        /// 内部参照（PBRemapの子孫への参照）はスキップする。
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
            PBRemap definition,
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
            Undo.SetCurrentGroupName("PBRemap (Prefab)");

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
                        // 自動作成を試みる
                        Transform autoCreated = TryAutoCreateFromSerialized(
                            boneRef, destArmature, destAnimator, remapRules);

                        if (autoCreated != null)
                        {
                            var so = new SerializedObject(component);
                            var prop = so.FindProperty(boneRef.propertyPath);
                            if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                Undo.RecordObject(component, "Remap Bone Reference (Auto-created)");
                                prop.objectReferenceValue = autoCreated;
                                so.ApplyModifiedProperties();
                                result.RemappedReferenceCount++;
                                result.RemappedComponentCount++;
                                result.AutoCreatedObjectCount++;
                            }
                        }
                        else
                        {
                            result.Warnings.Add($"ボーン '{boneRef.boneRelativePath}' を解決できません " +
                                $"({boneRef.componentObjectPath}/{boneRef.componentTypeName}.{boneRef.propertyPath})");
                            result.UnresolvedReferenceCount++;
                        }
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

            // 戦略3: フルパスマッチ + PathRemapRules（順方向）
            if (!string.IsNullOrEmpty(boneRef.boneRelativePath))
            {
                // まず直接パスマッチ
                var directMatch = BoneMapper.FindBoneByRelativePath(boneRef.boneRelativePath, destArmature);
                if (directMatch != null)
                    return directMatch;

                if (remapRules != null && remapRules.Count > 0)
                {
                    // 順方向リマップ
                    string remappedPath = BoneMapper.ApplyRemapRules(boneRef.boneRelativePath, remapRules);
                    var remappedMatch = BoneMapper.FindBoneByRelativePath(remappedPath, destArmature);
                    if (remappedMatch != null)
                        return remappedMatch;

                    // 逆方向リマップ（双方向対応）
                    string reverseRemappedPath = BoneMapper.ApplyRemapRulesReverse(boneRef.boneRelativePath, remapRules);
                    if (reverseRemappedPath != remappedPath)
                    {
                        var reverseMatch = BoneMapper.FindBoneByRelativePath(reverseRemappedPath, destArmature);
                        if (reverseMatch != null)
                            return reverseMatch;
                    }
                }
            }

            // 戦略4: 名前マッチ（フォールバック、双方向リマップ対応）
            if (!string.IsNullOrEmpty(boneRef.boneRelativePath))
            {
                string[] segments = boneRef.boneRelativePath.Split('/');
                string boneName = segments[segments.Length - 1];
                var allDestBones = destArmature.GetComponentsInChildren<Transform>(true);

                if (remapRules != null && remapRules.Count > 0)
                {
                    // 順方向リマップ後の名前でマッチ
                    string forwardName = boneName;
                    foreach (var rule in remapRules)
                        forwardName = rule.Apply(forwardName);
                    var forwardMatch = allDestBones.FirstOrDefault(t => t.name == forwardName);
                    if (forwardMatch != null)
                        return forwardMatch;

                    // 逆方向リマップ後の名前でマッチ
                    string reverseName = boneName;
                    foreach (var rule in remapRules)
                        reverseName = rule.ApplyReverse(reverseName);
                    if (reverseName != forwardName)
                    {
                        var reverseMatch = allDestBones.FirstOrDefault(t => t.name == reverseName);
                        if (reverseMatch != null)
                            return reverseMatch;
                    }
                }
                else
                {
                    var nameMatch = allDestBones.FirstOrDefault(t => t.name == boneName);
                    if (nameMatch != null)
                        return nameMatch;
                }
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

        /// <summary>
        /// シリアライズデータからヘルパーオブジェクトの自動作成を試みる。
        /// スケルトンボーンの場合や親が解決できない場合はnullを返す。
        /// </summary>
        private static Transform TryAutoCreateFromSerialized(
            SerializedBoneReference boneRef,
            Transform destArmature,
            Animator destAnimator,
            List<PathRemapRule> remapRules)
        {
            // スケルトンボーンは自動作成対象外
            if (boneRef.isSkeletonBone)
                return null;

            // パスが必要
            if (string.IsNullOrEmpty(boneRef.boneRelativePath) || !boneRef.boneRelativePath.Contains("/"))
                return null;

            int lastSlash = boneRef.boneRelativePath.LastIndexOf('/');
            string parentPath = boneRef.boneRelativePath.Substring(0, lastSlash);
            string boneName = boneRef.boneRelativePath.Substring(lastSlash + 1);

            // 親を解決
            Transform destParent = ResolveParentForAutoCreate(
                parentPath, destArmature, destAnimator, remapRules);
            if (destParent == null)
                return null;

            // destParent配下に既に同名の子がある場合はそれを使う
            Transform existing = destParent.Find(boneName);
            if (existing != null)
                return existing;

            // 新規GameObjectを作成
            var newObj = new GameObject(boneName);
            newObj.transform.SetParent(destParent, false);
            Undo.RegisterCreatedObjectUndo(newObj, "Auto-create helper object");

            return newObj.transform;
        }

        /// <summary>
        /// 親パスをデスティネーション側で解決する。
        /// 直接パスマッチ → リマップルール → 名前マッチの順で試行する。
        /// </summary>
        private static Transform ResolveParentForAutoCreate(
            string parentPath,
            Transform destArmature,
            Animator destAnimator,
            List<PathRemapRule> remapRules)
        {
            if (string.IsNullOrEmpty(parentPath))
                return destArmature;

            // 直接パスマッチ
            var directMatch = BoneMapper.FindBoneByRelativePath(parentPath, destArmature);
            if (directMatch != null)
                return directMatch;

            // リマップルール適用
            if (remapRules != null && remapRules.Count > 0)
            {
                string remappedPath = BoneMapper.ApplyRemapRules(parentPath, remapRules);
                var remappedMatch = BoneMapper.FindBoneByRelativePath(remappedPath, destArmature);
                if (remappedMatch != null)
                    return remappedMatch;

                string reverseRemappedPath = BoneMapper.ApplyRemapRulesReverse(parentPath, remapRules);
                if (reverseRemappedPath != remappedPath)
                {
                    var reverseMatch = BoneMapper.FindBoneByRelativePath(reverseRemappedPath, destArmature);
                    if (reverseMatch != null)
                        return reverseMatch;
                }
            }

            // 名前マッチ（親パスの末尾セグメント）
            string[] segments = parentPath.Split('/');
            string parentBoneName = segments[segments.Length - 1];
            var allDestBones = destArmature.GetComponentsInChildren<Transform>(true);
            var nameMatch = allDestBones.FirstOrDefault(t => t.name == parentBoneName);
            if (nameMatch != null)
                return nameMatch;

            return null;
        }

        #endregion

        #region 共通ヘルパー

        /// <summary>
        /// PBRemap配下の全VRCコンポーネントを収集する。
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
        /// PBRemapの設定に基づいてスケールファクターを算出する。
        /// </summary>
        private static float CalculateScaleFactor(
            PBRemap definition, AvatarData sourceData, AvatarData destData)
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
