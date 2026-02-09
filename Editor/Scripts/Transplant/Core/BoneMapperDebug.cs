using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// BoneMapperのデバッグ用MenuItemを提供するクラス。
    /// </summary>
    public static class BoneMapperDebug
    {
        [MenuItem("PBReplacer/Debug/Test BoneMapper")]
        private static void TestBoneMapper()
        {
            GameObject[] selected = Selection.gameObjects;
            if (selected.Length < 2)
            {
                Debug.LogWarning("[BoneMapper] ヒエラルキーで2つのアバターを選択してください。");
                return;
            }

            GameObject sourceObj = selected[0];
            GameObject destObj = selected[1];

            Debug.Log($"[BoneMapper] Source: {sourceObj.name}, Destination: {destObj.name}");

            Animator sourceAnimator = sourceObj.GetComponent<Animator>();
            Animator destAnimator = destObj.GetComponent<Animator>();

            Transform sourceArmature = FindArmature(sourceObj, sourceAnimator);
            Transform destArmature = FindArmature(destObj, destAnimator);

            if (sourceArmature == null || destArmature == null)
            {
                Debug.LogError("[BoneMapper] アーマチュアが見つかりません。" +
                    $" Source: {(sourceArmature != null ? sourceArmature.name : "null")}," +
                    $" Dest: {(destArmature != null ? destArmature.name : "null")}");
                return;
            }

            Debug.Log($"[BoneMapper] Source Armature: {sourceArmature.name}, Dest Armature: {destArmature.name}");

            // Humanoidボーンマップのテスト
            TestHumanoidBoneMap(sourceAnimator, destAnimator);

            // 全ソースボーンの解決テスト（リマップなし）
            TestBoneResolution(sourceArmature, destArmature, sourceAnimator, destAnimator);

            // リマップルール付きテスト
            TestBoneResolutionWithRemap(sourceArmature, destArmature, sourceAnimator, destAnimator);
        }

        private static Transform FindArmature(GameObject avatar, Animator animator)
        {
            if (animator != null && animator.isHuman)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null && hips.parent != null)
                    return hips.parent;
            }

            // フォールバック: 最大子階層を持つ子オブジェクトを探す
            Transform largest = null;
            int maxCount = 0;
            foreach (Transform child in avatar.transform)
            {
                int count = child.GetComponentsInChildren<Transform>().Length;
                if (count > maxCount)
                {
                    maxCount = count;
                    largest = child;
                }
            }
            return largest;
        }

        private static void TestHumanoidBoneMap(Animator source, Animator dest)
        {
            var map = BoneMapper.BuildHumanoidBoneMap(source, dest);
            Debug.Log($"[BoneMapper] Humanoidボーンマップ: {map.Count} ペア");

            if (map.Count > 0)
            {
                var sb = new StringBuilder();
                int shown = 0;
                foreach (var kvp in map)
                {
                    sb.AppendLine($"  {kvp.Key.name} -> {kvp.Value.name}");
                    if (++shown >= 5)
                    {
                        sb.AppendLine($"  ... 他 {map.Count - shown} ペア");
                        break;
                    }
                }
                Debug.Log($"[BoneMapper] Humanoidマッピング例:\n{sb}");
            }
        }

        private static void TestBoneResolution(
            Transform sourceArmature, Transform destArmature,
            Animator sourceAnimator, Animator destAnimator)
        {
            Transform[] sourceBones = sourceArmature.GetComponentsInChildren<Transform>();
            int resolved = 0;
            int failed = 0;
            var failures = new List<string>();

            foreach (Transform bone in sourceBones)
            {
                var result = BoneMapper.ResolveBone(
                    bone, sourceArmature, destArmature,
                    sourceAnimator, destAnimator);

                if (result.IsSuccess)
                {
                    resolved++;
                }
                else
                {
                    failed++;
                    if (failures.Count < 10)
                        failures.Add(result.Error);
                }
            }

            Debug.Log($"[BoneMapper] リマップなし解決結果: {resolved} 成功, {failed} 失敗 / {sourceBones.Length} 合計");

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (string f in failures)
                    sb.AppendLine($"  {f}");
                if (failed > failures.Count)
                    sb.AppendLine($"  ... 他 {failed - failures.Count} 件");
                Debug.LogWarning($"[BoneMapper] 解決失敗例:\n{sb}");
            }
        }

        private static void TestBoneResolutionWithRemap(
            Transform sourceArmature, Transform destArmature,
            Animator sourceAnimator, Animator destAnimator)
        {
            // テスト用リマップルール
            var rules = new List<PathRemapRule>
            {
                new PathRemapRule
                {
                    mode = PathRemapRule.RemapMode.PrefixReplace,
                    sourcePattern = "J_Bip_C_",
                    destinationPattern = "",
                    enabled = true
                },
                new PathRemapRule
                {
                    mode = PathRemapRule.RemapMode.CharacterSubstitution,
                    sourcePattern = "_L",
                    destinationPattern = ".L",
                    enabled = true
                },
                new PathRemapRule
                {
                    mode = PathRemapRule.RemapMode.CharacterSubstitution,
                    sourcePattern = "_R",
                    destinationPattern = ".R",
                    enabled = true
                }
            };

            // ApplyRemapRulesのテスト
            string testPath = "J_Bip_C_Hips/J_Bip_C_Spine/J_Bip_L_UpperArm";
            string remapped = BoneMapper.ApplyRemapRules(testPath, rules);
            Debug.Log($"[BoneMapper] リマップテスト: '{testPath}' -> '{remapped}'");

            // 全ボーンをリマップ付きで解決
            Transform[] sourceBones = sourceArmature.GetComponentsInChildren<Transform>();
            int resolved = 0;
            int failed = 0;
            int improvedByRemap = 0;
            var failures = new List<string>();

            foreach (Transform bone in sourceBones)
            {
                var directResult = BoneMapper.ResolveBone(
                    bone, sourceArmature, destArmature,
                    sourceAnimator, destAnimator);

                var remapResult = BoneMapper.ResolveBoneWithRemap(
                    bone, sourceArmature, destArmature, rules,
                    sourceAnimator, destAnimator);

                if (remapResult.IsSuccess)
                {
                    resolved++;
                    if (directResult.IsFailure)
                        improvedByRemap++;
                }
                else
                {
                    failed++;
                    if (failures.Count < 10)
                        failures.Add(remapResult.Error);
                }
            }

            Debug.Log($"[BoneMapper] リマップ付き解決結果: {resolved} 成功, {failed} 失敗 / {sourceBones.Length} 合計" +
                $" (リマップで追加解決: {improvedByRemap})");

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (string f in failures)
                    sb.AppendLine($"  {f}");
                if (failed > failures.Count)
                    sb.AppendLine($"  ... 他 {failed - failures.Count} 件");
                Debug.LogWarning($"[BoneMapper] リマップ付き解決失敗例:\n{sb}");
            }
        }
    }
}
