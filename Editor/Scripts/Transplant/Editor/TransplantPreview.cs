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
        /// TransplantDefinitionに基づき移植プレビューを生成する。
        /// ソース側のAvatarDynamics配下を走査し、各コンポーネントのボーン参照を
        /// デスティネーションで解決可能か確認する。副作用は一切ない。
        /// </summary>
        /// <param name="definition">移植定義</param>
        /// <returns>プレビューデータ。エラー時はnull（warningsにエラー内容を格納して返す場合あり）</returns>
        public static TransplantPreviewData GeneratePreview(TransplantDefinition definition)
        {
            var preview = new TransplantPreviewData();

            if (definition == null)
            {
                preview.Warnings.Add("TransplantDefinitionがnullです");
                return preview;
            }

            if (definition.SourceAvatar == null || definition.DestinationAvatar == null)
            {
                preview.Warnings.Add("ソースまたはデスティネーションアバターが未設定です");
                return preview;
            }

            // AvatarData取得
            AvatarData sourceData;
            AvatarData destData;
            try
            {
                sourceData = new AvatarData(definition.SourceAvatar);
            }
            catch (Exception ex)
            {
                preview.Warnings.Add($"ソースアバターの解析に失敗: {ex.Message}");
                return preview;
            }

            try
            {
                destData = new AvatarData(definition.DestinationAvatar);
            }
            catch (Exception ex)
            {
                preview.Warnings.Add($"デスティネーションアバターの解析に失敗: {ex.Message}");
                return preview;
            }

            Transform sourceArmature = sourceData.Armature.transform;
            Transform destArmature = destData.Armature.transform;
            Animator sourceAnimator = sourceData.AvatarAnimator;
            Animator destAnimator = destData.AvatarAnimator;
            var remapRules = definition.PathRemapRules?.ToList();

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

            // スケール差異が大きい場合の警告
            if (preview.CalculatedScaleFactor > 3.0f || preview.CalculatedScaleFactor < 0.33f)
            {
                preview.Warnings.Add(
                    $"スケール差異が大きいです (x{preview.CalculatedScaleFactor:F2})。" +
                    "移植後のパラメータを確認してください");
            }

            // ソースのAvatarDynamicsルートを検索
            var settings = PBReplacerSettings.Load();
            Transform sourceRoot = definition.SourceAvatar.transform.Find(settings.RootObjectName);
            if (sourceRoot == null)
            {
                preview.Warnings.Add(
                    $"ソースアバターに '{settings.RootObjectName}' が見つかりません");
                return preview;
            }

            // コンポーネント収集
            var physBones = sourceRoot.GetComponentsInChildren<VRCPhysBone>(true);
            var physBoneColliders = sourceRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true);
            var constraints = sourceRoot.GetComponentsInChildren<VRCConstraintBase>(true);
            var contacts = sourceRoot.GetComponentsInChildren<ContactBase>(true);

            preview.TotalPhysBones = physBones.Length;
            preview.TotalPhysBoneColliders = physBoneColliders.Length;
            preview.TotalConstraints = constraints.Length;
            preview.TotalContacts = contacts.Length;

            // 重複排除用のセット（同じボーンパスを複数回登録しない）
            var processedPaths = new HashSet<string>();

            // PhysBone の rootTransform を解決
            foreach (var pb in physBones)
            {
                Transform rootBone = pb.rootTransform != null
                    ? pb.rootTransform
                    : pb.transform.parent;
                AddBoneMapping(preview, rootBone, sourceArmature, destArmature,
                    remapRules, sourceAnimator, destAnimator, processedPaths);
            }

            // PhysBoneCollider の rootTransform を解決
            foreach (var pbc in physBoneColliders)
            {
                Transform rootBone = pbc.rootTransform != null
                    ? pbc.rootTransform
                    : pbc.transform.parent;
                AddBoneMapping(preview, rootBone, sourceArmature, destArmature,
                    remapRules, sourceAnimator, destAnimator, processedPaths);
            }

            // Constraint の TargetTransform と Sources を解決
            foreach (var constraint in constraints)
            {
                if (constraint.TargetTransform != null)
                {
                    AddBoneMapping(preview, constraint.TargetTransform,
                        sourceArmature, destArmature,
                        remapRules, sourceAnimator, destAnimator, processedPaths);
                }
            }

            // Contact の rootTransform を解決
            foreach (var contact in contacts)
            {
                Transform rootBone = contact.rootTransform != null
                    ? contact.rootTransform
                    : contact.transform.parent;
                AddBoneMapping(preview, rootBone, sourceArmature, destArmature,
                    remapRules, sourceAnimator, destAnimator, processedPaths);
            }

            // 未解決ボーンの警告
            if (preview.UnresolvedBones > 0)
            {
                preview.Warnings.Add(
                    $"{preview.UnresolvedBones} 個のボーンが解決できませんでした。" +
                    "パスリマップルールの追加を検討してください");
            }

            return preview;
        }

        /// <summary>
        /// 指定ボーンの解決を試み、BoneMappingリストに追加する。
        /// </summary>
        private static void AddBoneMapping(
            TransplantPreviewData preview,
            Transform sourceBone,
            Transform sourceArmature,
            Transform destArmature,
            List<PathRemapRule> remapRules,
            Animator sourceAnimator,
            Animator destAnimator,
            HashSet<string> processedPaths)
        {
            if (sourceBone == null)
                return;

            string sourcePath = BoneMapper.GetRelativePath(sourceBone, sourceArmature);
            string pathKey = sourcePath ?? sourceBone.name;

            // 重複チェック
            if (!processedPaths.Add(pathKey))
                return;

            var mapping = new BoneMapping
            {
                sourceBonePath = sourcePath ?? sourceBone.name
            };

            var resolveResult = (remapRules != null && remapRules.Count > 0)
                ? BoneMapper.ResolveBoneWithRemap(
                    sourceBone, sourceArmature, destArmature,
                    remapRules, sourceAnimator, destAnimator)
                : BoneMapper.ResolveBone(
                    sourceBone, sourceArmature, destArmature,
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
}
