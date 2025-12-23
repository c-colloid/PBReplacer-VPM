using System;
using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// Transform参照とパス文字列の変換を行うユーティリティ
    /// </summary>
    public static class PathResolver
    {
        /// <summary>
        /// Transformから相対パスを取得（指定されたルートからの相対パス）
        /// </summary>
        /// <param name="target">対象のTransform</param>
        /// <param name="root">ルートTransform</param>
        /// <returns>相対パス（rootを含まない）</returns>
        public static string GetRelativePath(Transform target, Transform root)
        {
            if (target == null || root == null)
                return string.Empty;

            if (target == root)
                return string.Empty;

            // targetがrootの子孫かどうか確認
            if (!IsDescendantOf(target, root))
                return string.Empty;

            var pathParts = new List<string>();
            Transform current = target;

            while (current != null && current != root)
            {
                pathParts.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", pathParts);
        }

        /// <summary>
        /// Transformから絶対パスを取得（アバタールートからの相対パス）
        /// </summary>
        /// <param name="target">対象のTransform</param>
        /// <param name="avatarRoot">アバターのルートTransform</param>
        /// <returns>絶対パス（avatarRootを含まない）</returns>
        public static string GetAbsolutePath(Transform target, Transform avatarRoot)
        {
            return GetRelativePath(target, avatarRoot);
        }

        /// <summary>
        /// TransformPathReferenceを生成
        /// </summary>
        /// <param name="target">対象のTransform</param>
        /// <param name="armature">ArmatureのTransform</param>
        /// <param name="avatarRoot">アバターのルートTransform</param>
        /// <param name="animator">Animatorコンポーネント（Humanoidボーン検出用）</param>
        /// <returns>TransformPathReference</returns>
        public static TransformPathReference CreatePathReference(
            Transform target,
            Transform armature,
            Transform avatarRoot,
            Animator animator = null)
        {
            if (target == null)
                return null;

            var reference = new TransformPathReference
            {
                relativePath = GetRelativePath(target, armature),
                absolutePath = GetAbsolutePath(target, avatarRoot),
                humanoidBoneHint = GetHumanoidBoneHint(target, animator)
            };

            return reference;
        }

        /// <summary>
        /// パスからTransformを解決
        /// </summary>
        /// <param name="reference">パス参照</param>
        /// <param name="armature">ArmatureのTransform</param>
        /// <param name="avatarRoot">アバターのルートTransform</param>
        /// <param name="animator">Animatorコンポーネント（Humanoidボーン検出用）</param>
        /// <param name="mode">マッチングモード</param>
        /// <returns>解決されたTransform（見つからない場合はnull）</returns>
        public static Transform ResolvePath(
            TransformPathReference reference,
            Transform armature,
            Transform avatarRoot,
            Animator animator = null,
            PathMatchingMode mode = PathMatchingMode.FlexibleWithWarning)
        {
            if (reference == null || !reference.IsValid)
                return null;

            Transform result = null;

            // 1. Humanoidボーンヒントで検索
            if (!string.IsNullOrEmpty(reference.humanoidBoneHint) && animator != null && animator.isHuman)
            {
                if (Enum.TryParse<HumanBodyBones>(reference.humanoidBoneHint, out var bone))
                {
                    result = animator.GetBoneTransform(bone);
                    if (result != null)
                        return result;
                }
            }

            // 2. 相対パス（Armatureから）で検索
            if (!string.IsNullOrEmpty(reference.relativePath) && armature != null)
            {
                result = armature.Find(reference.relativePath);
                if (result != null)
                    return result;
            }

            // 3. 絶対パス（アバタールートから）で検索
            if (!string.IsNullOrEmpty(reference.absolutePath) && avatarRoot != null)
            {
                result = avatarRoot.Find(reference.absolutePath);
                if (result != null)
                    return result;
            }

            // 4. 柔軟なマッチング：名前ベースで検索
            if (mode != PathMatchingMode.Strict)
            {
                result = FindByNameFlexible(reference, armature, avatarRoot);
                if (result != null)
                {
                    if (mode == PathMatchingMode.FlexibleWithWarning)
                    {
                        Debug.LogWarning($"[PBReplacer] パス '{reference.relativePath ?? reference.absolutePath}' の厳密な一致が見つからず、" +
                            $"'{GetAbsolutePath(result, avatarRoot)}' に柔軟マッチしました。");
                    }
                    return result;
                }
            }

            return null;
        }

        /// <summary>
        /// 名前ベースの柔軟なマッチング
        /// </summary>
        private static Transform FindByNameFlexible(TransformPathReference reference, Transform armature, Transform avatarRoot)
        {
            // パスの最後の部分（オブジェクト名）を取得
            string targetName = null;
            if (!string.IsNullOrEmpty(reference.relativePath))
            {
                var parts = reference.relativePath.Split('/');
                targetName = parts[parts.Length - 1];
            }
            else if (!string.IsNullOrEmpty(reference.absolutePath))
            {
                var parts = reference.absolutePath.Split('/');
                targetName = parts[parts.Length - 1];
            }

            if (string.IsNullOrEmpty(targetName))
                return null;

            // Armature内で名前で検索
            if (armature != null)
            {
                var found = FindChildByNameRecursive(armature, targetName);
                if (found != null)
                    return found;
            }

            // アバター全体で検索
            if (avatarRoot != null)
            {
                var found = FindChildByNameRecursive(avatarRoot, targetName);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// 再帰的に子オブジェクトを名前で検索
        /// </summary>
        private static Transform FindChildByNameRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;

                var found = FindChildByNameRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Humanoidボーンのヒント名を取得
        /// </summary>
        private static string GetHumanoidBoneHint(Transform target, Animator animator)
        {
            if (animator == null || !animator.isHuman || target == null)
                return null;

            // 全てのHumanBodyBonesをチェック
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                    continue;

                try
                {
                    var boneTransform = animator.GetBoneTransform(bone);
                    if (boneTransform == target)
                        return bone.ToString();
                }
                catch
                {
                    // 無効なボーンはスキップ
                }
            }

            return null;
        }

        /// <summary>
        /// targetがrootの子孫かどうかを確認
        /// </summary>
        private static bool IsDescendantOf(Transform target, Transform root)
        {
            Transform current = target;
            while (current != null)
            {
                if (current == root)
                    return true;
                current = current.parent;
            }
            return false;
        }

        /// <summary>
        /// AvatarのArmatureを検出
        /// </summary>
        public static Transform FindArmature(GameObject avatar)
        {
            if (avatar == null)
                return null;

            var animator = avatar.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                // Humanoidボーンからヒップを取得してその親を探す
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null && hips.parent != null && hips.parent != avatar.transform)
                {
                    return hips.parent;
                }
            }

            // 名前ベースで検索
            var armatureNames = new[] { "Armature", "armature", "Skeleton", "skeleton", "Root", "root" };
            foreach (var name in armatureNames)
            {
                var armature = avatar.transform.Find(name);
                if (armature != null)
                    return armature;
            }

            // 最大の子階層を持つオブジェクトを返す
            Transform largest = null;
            int maxDepth = 0;

            foreach (Transform child in avatar.transform)
            {
                int depth = GetHierarchyDepth(child);
                if (depth > maxDepth)
                {
                    maxDepth = depth;
                    largest = child;
                }
            }

            return largest;
        }

        /// <summary>
        /// 階層の深さを取得
        /// </summary>
        private static int GetHierarchyDepth(Transform transform)
        {
            int depth = 0;
            foreach (Transform child in transform)
            {
                depth = Math.Max(depth, GetHierarchyDepth(child) + 1);
            }
            return depth;
        }
    }
}
