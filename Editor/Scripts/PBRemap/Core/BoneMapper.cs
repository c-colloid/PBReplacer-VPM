using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// ボーンマッピングのコアロジック。
    /// ソースアバターのボーン参照をデスティネーションアバターの対応するボーンに解決する。
    /// </summary>
    public static class BoneMapper
    {
        /// <summary>
        /// armatureRootからboneまでの相対パスを返す。
        /// </summary>
        /// <param name="bone">対象のボーン</param>
        /// <param name="armatureRoot">アーマチュアのルート</param>
        /// <returns>相対パス。boneがarmatureRootと同一なら空文字。子孫でない場合はnull。</returns>
        public static string GetRelativePath(Transform bone, Transform armatureRoot)
        {
            if (bone == null || armatureRoot == null)
                return null;

            if (bone == armatureRoot)
                return "";

            var segments = new List<string>();
            Transform current = bone;

            while (current != null && current != armatureRoot)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            if (current != armatureRoot)
                return null;

            segments.Reverse();
            return string.Join("/", segments);
        }

        /// <summary>
        /// armatureRoot配下で相対パスに一致するTransformを返す。
        /// </summary>
        /// <param name="relativePath">相対パス</param>
        /// <param name="armatureRoot">アーマチュアのルート</param>
        /// <returns>見つかったTransform。見つからない場合はnull。</returns>
        public static Transform FindBoneByRelativePath(string relativePath, Transform armatureRoot)
        {
            if (armatureRoot == null)
                return null;

            if (string.IsNullOrEmpty(relativePath))
                return armatureRoot;

            return armatureRoot.Find(relativePath);
        }

        /// <summary>
        /// Humanoidボーンマップを構築する。
        /// 両方のAnimatorのHumanoidボーンを列挙し、対応するTransformのマップを作成する。
        /// </summary>
        /// <param name="source">ソース側のAnimator</param>
        /// <param name="destination">デスティネーション側のAnimator</param>
        /// <returns>ソースボーン→デスティネーションボーンの辞書</returns>
        public static Dictionary<Transform, Transform> BuildHumanoidBoneMap(
            Animator source, Animator destination)
        {
            var map = new Dictionary<Transform, Transform>();

            if (source == null || destination == null ||
                !source.isHuman || !destination.isHuman)
                return map;

            foreach (HumanBodyBones boneId in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (boneId == HumanBodyBones.LastBone)
                    continue;

                Transform srcBone = source.GetBoneTransform(boneId);
                Transform dstBone = destination.GetBoneTransform(boneId);

                if (srcBone != null && dstBone != null)
                    map[srcBone] = dstBone;
            }

            return map;
        }

        /// <summary>
        /// ソースボーンをデスティネーションアバターの対応するボーンに解決する。
        /// 3段階の戦略で解決を試みる: Humanoidマッピング → 相対パスマッチ → 名前マッチ。
        /// </summary>
        /// <param name="sourceBone">解決対象のソースボーン</param>
        /// <param name="sourceArmature">ソース側のアーマチュアルート</param>
        /// <param name="destArmature">デスティネーション側のアーマチュアルート</param>
        /// <param name="sourceAnimator">ソース側のAnimator（Humanoidマッピング用、省略可）</param>
        /// <param name="destAnimator">デスティネーション側のAnimator（Humanoidマッピング用、省略可）</param>
        /// <returns>解決されたTransformまたはエラーメッセージ</returns>
        public static Result<Transform, string> ResolveBone(
            Transform sourceBone,
            Transform sourceArmature,
            Transform destArmature,
            Animator sourceAnimator = null,
            Animator destAnimator = null)
        {
            if (sourceBone == null)
                return Result<Transform, string>.Failure("ソースボーンがnullです");
            if (sourceArmature == null)
                return Result<Transform, string>.Failure("ソースアーマチュアがnullです");
            if (destArmature == null)
                return Result<Transform, string>.Failure("デスティネーションアーマチュアがnullです");

            // 戦略1: Humanoidマッピング
            if (sourceAnimator != null && destAnimator != null &&
                sourceAnimator.isHuman && destAnimator.isHuman)
            {
                var humanoidMap = BuildHumanoidBoneMap(sourceAnimator, destAnimator);
                if (humanoidMap.TryGetValue(sourceBone, out Transform humanoidMatch))
                    return Result<Transform, string>.Success(humanoidMatch);
            }

            // 戦略2: 相対パスマッチ
            string relativePath = GetRelativePath(sourceBone, sourceArmature);
            if (relativePath != null)
            {
                Transform pathMatch = FindBoneByRelativePath(relativePath, destArmature);
                if (pathMatch != null)
                    return Result<Transform, string>.Success(pathMatch);
            }

            // 戦略3: 名前マッチ
            string boneName = sourceBone.name;
            Transform[] allDestBones = destArmature.GetComponentsInChildren<Transform>();
            Transform nameMatch = allDestBones.FirstOrDefault(t => t.name == boneName);
            if (nameMatch != null)
                return Result<Transform, string>.Success(nameMatch);

            return Result<Transform, string>.Failure(
                $"ボーン '{sourceBone.name}' に対応するデスティネーションボーンが見つかりません");
        }

        /// <summary>
        /// パスの各セグメントにリマップルールを順方向で適用する。
        /// </summary>
        /// <param name="relativePath">変換対象の相対パス</param>
        /// <param name="rules">適用するリマップルール</param>
        /// <returns>変換後の相対パス</returns>
        public static string ApplyRemapRules(string relativePath, List<PathRemapRule> rules)
        {
            if (string.IsNullOrEmpty(relativePath) || rules == null || rules.Count == 0)
                return relativePath;

            string[] segments = relativePath.Split('/');

            for (int i = 0; i < segments.Length; i++)
            {
                foreach (var rule in rules)
                {
                    segments[i] = rule.Apply(segments[i]);
                }
            }

            return string.Join("/", segments);
        }

        /// <summary>
        /// パスの各セグメントにリマップルールを逆方向で適用する。
        /// 双方向リマップにより、ルール1つで両方向の移植に対応する。
        /// </summary>
        /// <param name="relativePath">変換対象の相対パス</param>
        /// <param name="rules">適用するリマップルール</param>
        /// <returns>変換後の相対パス</returns>
        public static string ApplyRemapRulesReverse(string relativePath, List<PathRemapRule> rules)
        {
            if (string.IsNullOrEmpty(relativePath) || rules == null || rules.Count == 0)
                return relativePath;

            string[] segments = relativePath.Split('/');

            for (int i = 0; i < segments.Length; i++)
            {
                foreach (var rule in rules)
                {
                    segments[i] = rule.ApplyReverse(segments[i]);
                }
            }

            return string.Join("/", segments);
        }

        /// <summary>
        /// リマップルールを考慮してソースボーンをデスティネーションボーンに解決する。
        /// 解決戦略（優先度順）: 完全一致 → リマップ後パスマッチ → リマップ後名前マッチ。
        /// </summary>
        /// <param name="sourceBone">解決対象のソースボーン</param>
        /// <param name="sourceArmature">ソース側のアーマチュアルート</param>
        /// <param name="destArmature">デスティネーション側のアーマチュアルート</param>
        /// <param name="rules">パスリマップルール</param>
        /// <param name="sourceAnimator">ソース側のAnimator（省略可）</param>
        /// <param name="destAnimator">デスティネーション側のAnimator（省略可）</param>
        /// <returns>解決されたTransformまたはエラーメッセージ</returns>
        public static Result<Transform, string> ResolveBoneWithRemap(
            Transform sourceBone,
            Transform sourceArmature,
            Transform destArmature,
            List<PathRemapRule> rules,
            Animator sourceAnimator = null,
            Animator destAnimator = null)
        {
            // 戦略1: リマップなしで完全一致を試みる
            var directResult = ResolveBone(
                sourceBone, sourceArmature, destArmature,
                sourceAnimator, destAnimator);
            if (directResult.IsSuccess)
                return directResult;

            // リマップルールがない場合はここで終了
            if (rules == null || rules.Count == 0)
                return directResult;

            // 相対パスを取得
            string relativePath = GetRelativePath(sourceBone, sourceArmature);
            if (relativePath == null)
                return Result<Transform, string>.Failure(
                    $"ボーン '{sourceBone.name}' はソースアーマチュアの子孫ではありません");

            // 戦略2: 順方向リマップルール適用後のパスで検索
            string remappedPath = ApplyRemapRules(relativePath, rules);
            Transform remappedMatch = FindBoneByRelativePath(remappedPath, destArmature);
            if (remappedMatch != null)
                return Result<Transform, string>.Success(remappedMatch);

            // 戦略3: 順方向リマップ後の末尾ボーン名で名前マッチ
            string[] remappedSegments = remappedPath.Split('/');
            string remappedBoneName = remappedSegments[remappedSegments.Length - 1];
            Transform[] allDestBones = destArmature.GetComponentsInChildren<Transform>();
            Transform nameMatch = allDestBones.FirstOrDefault(t => t.name == remappedBoneName);
            if (nameMatch != null)
                return Result<Transform, string>.Success(nameMatch);

            // 戦略4: 逆方向リマップルール適用後のパスで検索
            string reverseRemappedPath = ApplyRemapRulesReverse(relativePath, rules);
            if (reverseRemappedPath != remappedPath)
            {
                Transform reverseMatch = FindBoneByRelativePath(reverseRemappedPath, destArmature);
                if (reverseMatch != null)
                    return Result<Transform, string>.Success(reverseMatch);

                // 逆方向リマップ後の末尾ボーン名で名前マッチ
                string[] reverseSegments = reverseRemappedPath.Split('/');
                string reverseBoneName = reverseSegments[reverseSegments.Length - 1];
                Transform reverseNameMatch = allDestBones.FirstOrDefault(t => t.name == reverseBoneName);
                if (reverseNameMatch != null)
                    return Result<Transform, string>.Success(reverseNameMatch);
            }

            return Result<Transform, string>.Failure(
                $"ボーン '{sourceBone.name}' に対応するデスティネーションボーンが見つかりません");
        }
    }
}
