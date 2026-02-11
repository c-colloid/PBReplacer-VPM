using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 移植プレビューの結果データ
    /// </summary>
    public class TransplantPreviewData
    {
        public List<BoneMapping> BoneMappings { get; set; } = new List<BoneMapping>();
        public int TotalPhysBones { get; set; }
        public int TotalPhysBoneColliders { get; set; }
        public int TotalConstraints { get; set; }
        public int TotalContacts { get; set; }
        public int ResolvedBones { get; set; }
        public int UnresolvedBones { get; set; }
        public float CalculatedScaleFactor { get; set; } = 1.0f;
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// 移植プレビュー生成ロジック。
    /// コンポーネントを一切作成せず、読み取りのみでボーンマッピング結果と
    /// コンポーネント数を算出する。
    /// </summary>
    public static class TransplantPreview
    {
        /// <summary>
        /// TransplantDefinitionとDetectionResultに基づき移植プレビューを生成する。
        /// 副作用は一切ない。
        /// </summary>
        /// <param name="definition">移植定義</param>
        /// <param name="detection">SourceDetectorの検出結果</param>
        /// <returns>プレビューデータ</returns>
        public static TransplantPreviewData GeneratePreview(
            TransplantDefinition definition,
            SourceDetector.DetectionResult detection)
        {
            var preview = new TransplantPreviewData();

            if (definition == null)
            {
                preview.Warnings.Add("TransplantDefinitionがnullです");
                return preview;
            }

            if (detection == null)
            {
                preview.Warnings.Add("検出結果がnullです");
                return preview;
            }

            if (detection.DestinationAvatar == null || detection.DestAvatarData == null)
            {
                preview.Warnings.Add("デスティネーションアバターが検出できません");
                return preview;
            }

            var definitionRoot = definition.transform;
            var remapRules = definition.PathRemapRules?.ToList();

            // コンポーネント収集
            var physBones = definitionRoot.GetComponentsInChildren<VRCPhysBone>(true);
            var physBoneColliders = definitionRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true);
            var constraints = definitionRoot.GetComponentsInChildren<VRCConstraintBase>(true);
            var contacts = definitionRoot.GetComponentsInChildren<ContactBase>(true);

            preview.TotalPhysBones = physBones.Length;
            preview.TotalPhysBoneColliders = physBoneColliders.Length;
            preview.TotalConstraints = constraints.Length;
            preview.TotalContacts = contacts.Length;

            if (detection.IsLiveMode)
            {
                GenerateLiveModePreview(preview, definition, detection, definitionRoot, remapRules);
            }
            else
            {
                GeneratePrefabModePreview(preview, definition, detection);
            }

            // 未解決ボーンの警告
            if (preview.UnresolvedBones > 0)
            {
                preview.Warnings.Add(
                    $"{preview.UnresolvedBones} 個のボーンが解決できませんでした。" +
                    "パスリマップルールの追加を検討してください。");
            }

            return preview;
        }

        /// <summary>
        /// 同一シーンモードのプレビュー。Transform参照が生きている状態で解決を試みる。
        /// </summary>
        private static void GenerateLiveModePreview(
            TransplantPreviewData preview,
            TransplantDefinition definition,
            SourceDetector.DetectionResult detection,
            Transform definitionRoot,
            List<PathRemapRule> remapRules)
        {
            if (detection.SourceAvatarData == null)
            {
                preview.Warnings.Add("ソースアバターのデータを取得できません");
                return;
            }

            var sourceData = detection.SourceAvatarData;
            var destData = detection.DestAvatarData;

            Transform sourceArmature = sourceData.Armature.transform;
            Transform destArmature = destData.Armature.transform;
            Animator sourceAnimator = sourceData.AvatarAnimator;
            Animator destAnimator = destData.AvatarAnimator;

            // スケールファクター算出
            if (definition.AutoCalculateScale)
            {
                preview.CalculatedScaleFactor = ScaleCalculator.CalculateScaleFactor(
                    sourceArmature, destArmature, sourceAnimator, destAnimator);
            }
            else
            {
                preview.CalculatedScaleFactor = definition.ScaleFactor;
            }

            // スケール差異の警告
            if (preview.CalculatedScaleFactor > 3.0f || preview.CalculatedScaleFactor < 0.33f)
            {
                preview.Warnings.Add(
                    $"スケール差異が大きいです (x{preview.CalculatedScaleFactor:F2})。" +
                    "移植後のパラメータを確認してください。");
            }

            // 外部Transform参照を収集してボーン解決を試みる
            var processedPaths = new HashSet<string>();
            var externalRefs = SourceDetector.CollectExternalTransformReferences(definition);

            foreach (var bone in externalRefs)
            {
                string sourcePath = BoneMapper.GetRelativePath(bone, sourceArmature);
                string pathKey = sourcePath ?? bone.name;

                if (!processedPaths.Add(pathKey))
                    continue;

                var mapping = new BoneMapping
                {
                    sourceBonePath = sourcePath ?? bone.name
                };

                var resolveResult = (remapRules != null && remapRules.Count > 0)
                    ? BoneMapper.ResolveBoneWithRemap(
                        bone, sourceArmature, destArmature,
                        remapRules, sourceAnimator, destAnimator)
                    : BoneMapper.ResolveBone(
                        bone, sourceArmature, destArmature,
                        sourceAnimator, destAnimator);

                if (resolveResult.IsSuccess)
                {
                    mapping.resolved = true;
                    mapping.destinationBonePath = BoneMapper.GetRelativePath(
                        resolveResult.Value, destArmature) ?? resolveResult.Value.name;
                    preview.ResolvedBones++;
                }
                else
                {
                    mapping.resolved = false;
                    mapping.errorMessage = resolveResult.Error;
                    mapping.destinationBonePath = "";
                    preview.UnresolvedBones++;
                }

                preview.BoneMappings.Add(mapping);
            }
        }

        /// <summary>
        /// Prefabモードのプレビュー。シリアライズ済みボーンデータから解決を試みる。
        /// </summary>
        private static void GeneratePrefabModePreview(
            TransplantPreviewData preview,
            TransplantDefinition definition,
            SourceDetector.DetectionResult detection)
        {
            if (definition.SerializedBoneReferences.Count == 0)
            {
                preview.Warnings.Add("シリアライズ済みボーン参照データがありません");
                return;
            }

            var destData = detection.DestAvatarData;
            var destArmature = destData.Armature.transform;
            var destAnimator = destData.AvatarAnimator;
            var remapRules = definition.PathRemapRules?.ToList();

            // スケールファクター算出
            if (definition.AutoCalculateScale && definition.SourceAvatarScale > 0)
            {
                float destScale = TransplantRemapper.CalculateAvatarScale(destData);
                preview.CalculatedScaleFactor = destScale / definition.SourceAvatarScale;
            }
            else
            {
                preview.CalculatedScaleFactor = definition.ScaleFactor;
            }

            // 重複排除してボーン解決を試みる
            var processedPaths = new HashSet<string>();

            foreach (var boneRef in definition.SerializedBoneReferences)
            {
                if (!processedPaths.Add(boneRef.boneRelativePath))
                    continue;

                var mapping = new BoneMapping
                {
                    sourceBonePath = boneRef.boneRelativePath
                };

                // 4段階解決戦略を試す
                Transform resolved = ResolveBoneFromSerialized(
                    boneRef, destArmature, destAnimator, remapRules);

                if (resolved != null)
                {
                    mapping.resolved = true;
                    mapping.destinationBonePath = BoneMapper.GetRelativePath(
                        resolved, destArmature) ?? resolved.name;
                    preview.ResolvedBones++;
                }
                else
                {
                    mapping.resolved = false;
                    mapping.errorMessage = $"ボーン '{boneRef.boneRelativePath}' を解決できません";
                    mapping.destinationBonePath = "";
                    preview.UnresolvedBones++;
                }

                preview.BoneMappings.Add(mapping);
            }
        }

        /// <summary>
        /// シリアライズデータからボーンを解決する（TransplantRemapperと同一ロジック）。
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
    }
}
