using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace colloid.PBReplacer.NDMF
{
    /// <summary>
    /// AvatarDynamicsConfigの参照を解決するクラス
    /// </summary>
    public static class AvatarDynamicsResolver
    {
        /// <summary>
        /// AvatarDynamicsConfigの参照を解決
        /// </summary>
        public static void ResolveReferences(AvatarDynamicsConfig config, GameObject avatarRoot)
        {
            if (config == null || avatarRoot == null)
                return;

            var armature = PathResolver.FindArmature(avatarRoot);
            var animator = avatarRoot.GetComponent<Animator>();
            var dynamicsRoot = config.gameObject;

            // 参照解決用のキャッシュ
            var colliderCache = new Dictionary<string, VRCPhysBoneCollider>();

            // まずColliderを収集（PhysBoneのcolliders参照解決に必要）
            var colliders = dynamicsRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true);
            foreach (var collider in colliders)
            {
                var key = collider.gameObject.name;
                if (!colliderCache.ContainsKey(key))
                {
                    colliderCache[key] = collider;
                }
            }

            // 各コンポーネントの参照を解決
            foreach (var refData in config.componentReferences)
            {
                switch (refData.componentType)
                {
                    case DynamicsComponentType.PhysBone:
                        ResolvePhysBoneReferences(refData, dynamicsRoot, armature, avatarRoot.transform, animator, colliderCache, config);
                        break;

                    case DynamicsComponentType.PhysBoneCollider:
                        ResolvePhysBoneColliderReferences(refData, dynamicsRoot, armature, avatarRoot.transform, animator, config);
                        break;

                    case DynamicsComponentType.ContactSender:
                    case DynamicsComponentType.ContactReceiver:
                        ResolveContactReferences(refData, dynamicsRoot, armature, avatarRoot.transform, animator, config);
                        break;

                    case DynamicsComponentType.PositionConstraint:
                    case DynamicsComponentType.RotationConstraint:
                    case DynamicsComponentType.ScaleConstraint:
                    case DynamicsComponentType.ParentConstraint:
                    case DynamicsComponentType.LookAtConstraint:
                    case DynamicsComponentType.AimConstraint:
                        ResolveConstraintReferences(refData, dynamicsRoot, armature, avatarRoot.transform, animator, config);
                        break;
                }
            }
        }

        /// <summary>
        /// PhysBoneの参照を解決
        /// </summary>
        private static void ResolvePhysBoneReferences(
            ComponentReferenceData refData,
            GameObject dynamicsRoot,
            Transform armature,
            Transform avatarRoot,
            Animator animator,
            Dictionary<string, VRCPhysBoneCollider> colliderCache,
            AvatarDynamicsConfig config)
        {
            var componentObj = FindComponentObject(dynamicsRoot, refData.gameObjectName);
            if (componentObj == null)
            {
                LogMissingComponent(refData.gameObjectName, config);
                return;
            }

            var physBone = componentObj.GetComponent<VRCPhysBone>();
            if (physBone == null)
            {
                LogMissingComponent(refData.gameObjectName, config);
                return;
            }

            // RootTransformを解決
            if (refData.rootTransformPath != null && refData.rootTransformPath.IsValid)
            {
                var resolvedRoot = PathResolver.ResolvePath(
                    refData.rootTransformPath, armature, avatarRoot, animator, config.pathMatchingMode);

                if (resolvedRoot != null)
                {
                    physBone.rootTransform = resolvedRoot;
                }
                else
                {
                    LogMissingReference("RootTransform", refData.rootTransformPath.relativePath, config);
                }
            }

            // IgnoreTransformsを解決
            if (refData.additionalTransformPaths != null && refData.additionalTransformPaths.Count > 0)
            {
                var ignoreList = new List<Transform>();
                foreach (var pathRef in refData.additionalTransformPaths)
                {
                    if (pathRef != null && pathRef.IsValid)
                    {
                        var resolved = PathResolver.ResolvePath(
                            pathRef, armature, avatarRoot, animator, config.pathMatchingMode);

                        if (resolved != null)
                        {
                            ignoreList.Add(resolved);
                        }
                        else
                        {
                            LogMissingReference("IgnoreTransform", pathRef.relativePath, config);
                        }
                    }
                }
                physBone.ignoreTransforms = ignoreList;
            }

            // Collidersを解決
            if (refData.colliderReferences != null && refData.colliderReferences.Count > 0)
            {
                var colliderList = new List<VRCPhysBoneCollider>();
                foreach (var colRef in refData.colliderReferences)
                {
                    if (colRef.pathReference != null && colRef.pathReference.IsValid)
                    {
                        // まずキャッシュから名前で検索
                        var objName = GetObjectNameFromPath(colRef.pathReference.relativePath ?? colRef.pathReference.absolutePath);
                        if (!string.IsNullOrEmpty(objName) && colliderCache.TryGetValue(objName, out var cachedCollider))
                        {
                            colliderList.Add(cachedCollider);
                        }
                        else
                        {
                            // パスで検索
                            var resolved = PathResolver.ResolvePath(
                                colRef.pathReference, armature, avatarRoot, animator, config.pathMatchingMode);

                            if (resolved != null)
                            {
                                var collider = resolved.GetComponent<VRCPhysBoneCollider>();
                                if (collider != null)
                                {
                                    colliderList.Add(collider);
                                }
                            }
                            else
                            {
                                LogMissingReference("Collider", colRef.pathReference.relativePath, config);
                            }
                        }
                    }
                }
                physBone.colliders = colliderList;
            }
        }

        /// <summary>
        /// PhysBoneColliderの参照を解決
        /// </summary>
        private static void ResolvePhysBoneColliderReferences(
            ComponentReferenceData refData,
            GameObject dynamicsRoot,
            Transform armature,
            Transform avatarRoot,
            Animator animator,
            AvatarDynamicsConfig config)
        {
            var componentObj = FindComponentObject(dynamicsRoot, refData.gameObjectName);
            if (componentObj == null)
            {
                LogMissingComponent(refData.gameObjectName, config);
                return;
            }

            var collider = componentObj.GetComponent<VRCPhysBoneCollider>();
            if (collider == null)
            {
                LogMissingComponent(refData.gameObjectName, config);
                return;
            }

            // RootTransformを解決
            if (refData.rootTransformPath != null && refData.rootTransformPath.IsValid)
            {
                var resolvedRoot = PathResolver.ResolvePath(
                    refData.rootTransformPath, armature, avatarRoot, animator, config.pathMatchingMode);

                if (resolvedRoot != null)
                {
                    collider.rootTransform = resolvedRoot;
                }
                else
                {
                    LogMissingReference("RootTransform", refData.rootTransformPath.relativePath, config);
                }
            }
        }

        /// <summary>
        /// Contact系の参照を解決
        /// </summary>
        private static void ResolveContactReferences(
            ComponentReferenceData refData,
            GameObject dynamicsRoot,
            Transform armature,
            Transform avatarRoot,
            Animator animator,
            AvatarDynamicsConfig config)
        {
            var componentObj = FindComponentObject(dynamicsRoot, refData.gameObjectName);
            if (componentObj == null)
            {
                LogMissingComponent(refData.gameObjectName, config);
                return;
            }

            VRC.Dynamics.ContactBase contact = null;

            if (refData.componentType == DynamicsComponentType.ContactSender)
            {
                contact = componentObj.GetComponent<VRCContactSender>();
            }
            else
            {
                contact = componentObj.GetComponent<VRCContactReceiver>();
            }

            if (contact == null)
            {
                LogMissingComponent(refData.gameObjectName, config);
                return;
            }

            // RootTransformを解決
            if (refData.rootTransformPath != null && refData.rootTransformPath.IsValid)
            {
                var resolvedRoot = PathResolver.ResolvePath(
                    refData.rootTransformPath, armature, avatarRoot, animator, config.pathMatchingMode);

                if (resolvedRoot != null)
                {
                    contact.rootTransform = resolvedRoot;
                }
                else
                {
                    LogMissingReference("RootTransform", refData.rootTransformPath.relativePath, config);
                }
            }
        }

        /// <summary>
        /// Constraintの参照を解決
        /// </summary>
        private static void ResolveConstraintReferences(
            ComponentReferenceData refData,
            GameObject dynamicsRoot,
            Transform armature,
            Transform avatarRoot,
            Animator animator,
            AvatarDynamicsConfig config)
        {
            var componentObj = FindComponentObject(dynamicsRoot, refData.gameObjectName);
            if (componentObj == null)
            {
                LogMissingComponent(refData.gameObjectName, config);
                return;
            }

            VRCConstraintBase constraint = null;

            switch (refData.componentType)
            {
                case DynamicsComponentType.PositionConstraint:
                    constraint = componentObj.GetComponent<VRCPositionConstraint>();
                    break;
                case DynamicsComponentType.RotationConstraint:
                    constraint = componentObj.GetComponent<VRCRotationConstraint>();
                    break;
                case DynamicsComponentType.ScaleConstraint:
                    constraint = componentObj.GetComponent<VRCScaleConstraint>();
                    break;
                case DynamicsComponentType.ParentConstraint:
                    constraint = componentObj.GetComponent<VRCParentConstraint>();
                    break;
                case DynamicsComponentType.LookAtConstraint:
                    constraint = componentObj.GetComponent<VRCLookAtConstraint>();
                    break;
                case DynamicsComponentType.AimConstraint:
                    constraint = componentObj.GetComponent<VRCAimConstraint>();
                    break;
            }

            if (constraint == null)
            {
                LogMissingComponent(refData.gameObjectName, config);
                return;
            }

            // TargetTransformを解決
            if (refData.targetTransformPath != null && refData.targetTransformPath.IsValid)
            {
                var resolvedTarget = PathResolver.ResolvePath(
                    refData.targetTransformPath, armature, avatarRoot, animator, config.pathMatchingMode);

                if (resolvedTarget != null)
                {
                    constraint.TargetTransform = resolvedTarget;
                }
                else
                {
                    LogMissingReference("TargetTransform", refData.targetTransformPath.relativePath, config);
                }
            }

            // ソースTransformを解決
            if (refData.sourceTransformPaths != null && refData.sourceTransformPaths.Count > 0)
            {
                var sources = constraint.Sources;
                for (int i = 0; i < refData.sourceTransformPaths.Count && i < sources.Count; i++)
                {
                    var pathRef = refData.sourceTransformPaths[i];
                    if (pathRef != null && pathRef.IsValid)
                    {
                        var resolved = PathResolver.ResolvePath(
                            pathRef, armature, avatarRoot, animator, config.pathMatchingMode);

                        if (resolved != null)
                        {
                            var source = sources[i];
                            source.SourceTransform = resolved;
                            sources[i] = source;
                        }
                        else
                        {
                            LogMissingReference("SourceTransform", pathRef.relativePath, config);
                        }
                    }
                }
                constraint.Sources = sources;
            }
        }

        /// <summary>
        /// 名前でコンポーネントオブジェクトを検索
        /// </summary>
        private static GameObject FindComponentObject(GameObject root, string name)
        {
            if (root.name == name)
                return root;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                    return child.gameObject;
            }

            return null;
        }

        /// <summary>
        /// パスからオブジェクト名を取得
        /// </summary>
        private static string GetObjectNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            return parts[parts.Length - 1];
        }

        /// <summary>
        /// 見つからないコンポーネントのログ
        /// </summary>
        private static void LogMissingComponent(string name, AvatarDynamicsConfig config)
        {
            switch (config.missingReferenceHandling)
            {
                case MissingReferenceHandling.Error:
                    Debug.LogError($"[PBReplacer] コンポーネントが見つかりません: {name}");
                    break;
                case MissingReferenceHandling.LogWarning:
                    Debug.LogWarning($"[PBReplacer] コンポーネントが見つかりません: {name}");
                    break;
            }
        }

        /// <summary>
        /// 見つからない参照のログ
        /// </summary>
        private static void LogMissingReference(string refType, string path, AvatarDynamicsConfig config)
        {
            switch (config.missingReferenceHandling)
            {
                case MissingReferenceHandling.Error:
                    Debug.LogError($"[PBReplacer] {refType}の参照を解決できません: {path}");
                    break;
                case MissingReferenceHandling.LogWarning:
                    Debug.LogWarning($"[PBReplacer] {refType}の参照を解決できません: {path}");
                    break;
            }
        }
    }
}
