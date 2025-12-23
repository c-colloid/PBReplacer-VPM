using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
    /// <summary>
    /// AvatarDynamicsオブジェクトをエクスポートするクラス
    /// </summary>
    public static class AvatarDynamicsExporter
    {
        private const string EXPORT_VERSION = "1.0.0";
        private const string ADCR_EXTENSION = ".adcr";

        /// <summary>
        /// エクスポート結果
        /// </summary>
        public class ExportResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string ExportPath { get; set; }
            public GameObject ExportedPrefab { get; set; }
        }

        /// <summary>
        /// AvatarDynamicsオブジェクトをPrefabとしてエクスポート
        /// </summary>
        /// <param name="avatarDynamicsRoot">エクスポート対象のAvatarDynamicsルートオブジェクト</param>
        /// <param name="avatarRoot">アバターのルートオブジェクト</param>
        /// <param name="exportPath">エクスポート先のパス（.prefab）</param>
        /// <returns>エクスポート結果</returns>
        public static ExportResult ExportAsPrefab(GameObject avatarDynamicsRoot, GameObject avatarRoot, string exportPath)
        {
            var result = new ExportResult();

            try
            {
                if (avatarDynamicsRoot == null)
                {
                    result.Success = false;
                    result.Message = "AvatarDynamicsオブジェクトが指定されていません";
                    return result;
                }

                // ディレクトリを確認・作成
                var directory = Path.GetDirectoryName(exportPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 複製を作成
                var clone = UnityEngine.Object.Instantiate(avatarDynamicsRoot);
                clone.name = avatarDynamicsRoot.name;

                try
                {
                    // メタデータコンポーネントを追加
                    var config = clone.GetComponent<AvatarDynamicsConfig>();
                    if (config == null)
                    {
                        config = clone.AddComponent<AvatarDynamicsConfig>();
                    }

                    // メタデータを収集
                    PopulateConfigMetadata(config, clone, avatarRoot);

                    // Prefabとして保存
                    var prefab = PrefabUtility.SaveAsPrefabAsset(clone, exportPath);

                    result.Success = true;
                    result.Message = $"Prefabをエクスポートしました: {exportPath}";
                    result.ExportPath = exportPath;
                    result.ExportedPrefab = prefab;
                }
                finally
                {
                    // クローンを削除
                    UnityEngine.Object.DestroyImmediate(clone);
                }

                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"エクスポート中にエラーが発生しました: {ex.Message}";
                Debug.LogError($"[PBReplacer] エクスポートエラー: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// .adcrファイルとしてエクスポート（Prefab + メタデータのZIP）
        /// </summary>
        public static ExportResult ExportAsAdcr(GameObject avatarDynamicsRoot, GameObject avatarRoot, string exportPath)
        {
            var result = new ExportResult();

            try
            {
                if (!exportPath.EndsWith(ADCR_EXTENSION))
                {
                    exportPath += ADCR_EXTENSION;
                }

                // 一時ディレクトリを作成
                var tempDir = Path.Combine(Path.GetTempPath(), $"PBReplacer_Export_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Prefabを一時ディレクトリにエクスポート
                    var prefabPath = Path.Combine(tempDir, $"{avatarDynamicsRoot.name}.prefab");

                    // 一時的にAssets内に保存（PrefabUtility用）
                    var tempAssetPath = $"Assets/Temp_PBReplacer_Export_{Guid.NewGuid()}.prefab";
                    var prefabResult = ExportAsPrefab(avatarDynamicsRoot, avatarRoot, tempAssetPath);

                    if (!prefabResult.Success)
                    {
                        result.Success = false;
                        result.Message = prefabResult.Message;
                        return result;
                    }

                    // メタデータJSONを作成
                    var config = prefabResult.ExportedPrefab.GetComponent<AvatarDynamicsConfig>();
                    var metadataJson = CreateMetadataJson(config, avatarRoot.name);
                    var metadataPath = Path.Combine(tempDir, "metadata.json");
                    File.WriteAllText(metadataPath, metadataJson);

                    // Prefabファイルをコピー
                    var prefabFullPath = Path.GetFullPath(tempAssetPath);
                    File.Copy(prefabFullPath, prefabPath);

                    // .metaファイルもコピー
                    var metaPath = prefabFullPath + ".meta";
                    if (File.Exists(metaPath))
                    {
                        File.Copy(metaPath, prefabPath + ".meta");
                    }

                    // ZIPファイルを作成
                    if (File.Exists(exportPath))
                    {
                        File.Delete(exportPath);
                    }
                    ZipFile.CreateFromDirectory(tempDir, exportPath);

                    // 一時Prefabを削除
                    AssetDatabase.DeleteAsset(tempAssetPath);

                    result.Success = true;
                    result.Message = $".adcrファイルをエクスポートしました: {exportPath}";
                    result.ExportPath = exportPath;
                }
                finally
                {
                    // 一時ディレクトリを削除
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"エクスポート中にエラーが発生しました: {ex.Message}";
                Debug.LogError($"[PBReplacer] エクスポートエラー: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// UnityPackageとしてエクスポート
        /// </summary>
        public static ExportResult ExportAsUnityPackage(GameObject avatarDynamicsRoot, GameObject avatarRoot, string exportPath)
        {
            var result = new ExportResult();

            try
            {
                if (!exportPath.EndsWith(".unitypackage"))
                {
                    exportPath += ".unitypackage";
                }

                // 一時的にPrefabをAssets内に保存
                var tempAssetPath = $"Assets/Temp_PBReplacer_Export_{avatarDynamicsRoot.name}.prefab";
                var prefabResult = ExportAsPrefab(avatarDynamicsRoot, avatarRoot, tempAssetPath);

                if (!prefabResult.Success)
                {
                    result.Success = false;
                    result.Message = prefabResult.Message;
                    return result;
                }

                try
                {
                    // UnityPackageとしてエクスポート
                    AssetDatabase.ExportPackage(tempAssetPath, exportPath, ExportPackageOptions.Default);

                    result.Success = true;
                    result.Message = $"UnityPackageをエクスポートしました: {exportPath}";
                    result.ExportPath = exportPath;
                }
                finally
                {
                    // 一時Prefabを削除
                    AssetDatabase.DeleteAsset(tempAssetPath);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"エクスポート中にエラーが発生しました: {ex.Message}";
                Debug.LogError($"[PBReplacer] エクスポートエラー: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// AvatarDynamicsConfigにメタデータを設定
        /// </summary>
        private static void PopulateConfigMetadata(AvatarDynamicsConfig config, GameObject dynamicsRoot, GameObject avatarRoot)
        {
            config.configName = dynamicsRoot.name;
            config.sourceAvatarName = avatarRoot?.name ?? "Unknown";
            config.exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            config.exporterVersion = EXPORT_VERSION;
            config.componentReferences.Clear();

            var armature = PathResolver.FindArmature(avatarRoot);
            var animator = avatarRoot?.GetComponent<Animator>();

            // PhysBoneを収集
            var physBones = dynamicsRoot.GetComponentsInChildren<VRCPhysBone>(true);
            foreach (var pb in physBones)
            {
                var refData = CreateComponentReferenceData(pb, armature, avatarRoot.transform, animator);
                config.componentReferences.Add(refData);
            }

            // PhysBoneColliderを収集
            var colliders = dynamicsRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true);
            foreach (var col in colliders)
            {
                var refData = CreateColliderReferenceData(col, armature, avatarRoot.transform, animator);
                config.componentReferences.Add(refData);
            }

            // ContactSenderを収集
            var senders = dynamicsRoot.GetComponentsInChildren<VRCContactSender>(true);
            foreach (var sender in senders)
            {
                var refData = CreateContactReferenceData(sender, DynamicsComponentType.ContactSender, armature, avatarRoot.transform, animator);
                config.componentReferences.Add(refData);
            }

            // ContactReceiverを収集
            var receivers = dynamicsRoot.GetComponentsInChildren<VRCContactReceiver>(true);
            foreach (var receiver in receivers)
            {
                var refData = CreateContactReferenceData(receiver, DynamicsComponentType.ContactReceiver, armature, avatarRoot.transform, animator);
                config.componentReferences.Add(refData);
            }

            // Constraintsを収集
            CollectConstraints<VRCPositionConstraint>(dynamicsRoot, config, DynamicsComponentType.PositionConstraint, armature, avatarRoot.transform, animator);
            CollectConstraints<VRCRotationConstraint>(dynamicsRoot, config, DynamicsComponentType.RotationConstraint, armature, avatarRoot.transform, animator);
            CollectConstraints<VRCScaleConstraint>(dynamicsRoot, config, DynamicsComponentType.ScaleConstraint, armature, avatarRoot.transform, animator);
            CollectConstraints<VRCParentConstraint>(dynamicsRoot, config, DynamicsComponentType.ParentConstraint, armature, avatarRoot.transform, animator);
            CollectConstraints<VRCLookAtConstraint>(dynamicsRoot, config, DynamicsComponentType.LookAtConstraint, armature, avatarRoot.transform, animator);
            CollectConstraints<VRCAimConstraint>(dynamicsRoot, config, DynamicsComponentType.AimConstraint, armature, avatarRoot.transform, animator);
        }

        /// <summary>
        /// PhysBoneの参照データを作成
        /// </summary>
        private static ComponentReferenceData CreateComponentReferenceData(
            VRCPhysBone physBone,
            Transform armature,
            Transform avatarRoot,
            Animator animator)
        {
            var refData = new ComponentReferenceData(physBone.gameObject.name, DynamicsComponentType.PhysBone);

            // RootTransformの参照
            var rootTransform = physBone.rootTransform ?? physBone.transform;
            refData.rootTransformPath = PathResolver.CreatePathReference(rootTransform, armature, avatarRoot, animator);

            // IgnoreTransformsの参照
            if (physBone.ignoreTransforms != null)
            {
                foreach (var ignore in physBone.ignoreTransforms)
                {
                    if (ignore != null)
                    {
                        var ignorePath = PathResolver.CreatePathReference(ignore, armature, avatarRoot, animator);
                        refData.additionalTransformPaths.Add(ignorePath);
                    }
                }
            }

            // Collidersの参照
            if (physBone.colliders != null)
            {
                for (int i = 0; i < physBone.colliders.Count; i++)
                {
                    var collider = physBone.colliders[i];
                    if (collider != null)
                    {
                        var colliderPath = PathResolver.CreatePathReference(collider.transform, armature, avatarRoot, animator);
                        refData.colliderReferences.Add(new ColliderReference(i, colliderPath));
                    }
                }
            }

            return refData;
        }

        /// <summary>
        /// PhysBoneColliderの参照データを作成
        /// </summary>
        private static ComponentReferenceData CreateColliderReferenceData(
            VRCPhysBoneCollider collider,
            Transform armature,
            Transform avatarRoot,
            Animator animator)
        {
            var refData = new ComponentReferenceData(collider.gameObject.name, DynamicsComponentType.PhysBoneCollider);

            // RootTransformの参照
            var rootTransform = collider.rootTransform ?? collider.transform;
            refData.rootTransformPath = PathResolver.CreatePathReference(rootTransform, armature, avatarRoot, animator);

            return refData;
        }

        /// <summary>
        /// Contact系の参照データを作成
        /// </summary>
        private static ComponentReferenceData CreateContactReferenceData(
            ContactBase contact,
            DynamicsComponentType componentType,
            Transform armature,
            Transform avatarRoot,
            Animator animator)
        {
            var refData = new ComponentReferenceData(contact.gameObject.name, componentType);

            // RootTransformの参照
            var rootTransform = contact.rootTransform ?? contact.transform;
            refData.rootTransformPath = PathResolver.CreatePathReference(rootTransform, armature, avatarRoot, animator);

            return refData;
        }

        /// <summary>
        /// Constraintを収集
        /// </summary>
        private static void CollectConstraints<T>(
            GameObject root,
            AvatarDynamicsConfig config,
            DynamicsComponentType componentType,
            Transform armature,
            Transform avatarRoot,
            Animator animator) where T : VRCConstraintBase
        {
            var constraints = root.GetComponentsInChildren<T>(true);
            foreach (var constraint in constraints)
            {
                var refData = CreateConstraintReferenceData(constraint, componentType, armature, avatarRoot, animator);
                config.componentReferences.Add(refData);
            }
        }

        /// <summary>
        /// Constraintの参照データを作成
        /// </summary>
        private static ComponentReferenceData CreateConstraintReferenceData(
            VRCConstraintBase constraint,
            DynamicsComponentType componentType,
            Transform armature,
            Transform avatarRoot,
            Animator animator)
        {
            var refData = new ComponentReferenceData(constraint.gameObject.name, componentType);

            // TargetTransformの参照
            if (constraint.TargetTransform != null)
            {
                refData.targetTransformPath = PathResolver.CreatePathReference(constraint.TargetTransform, armature, avatarRoot, animator);
            }

            // ソースTransformの参照
            var sources = constraint.Sources;
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    if (source.SourceTransform != null)
                    {
                        var sourcePath = PathResolver.CreatePathReference(source.SourceTransform, armature, avatarRoot, animator);
                        refData.sourceTransformPaths.Add(sourcePath);
                    }
                }
            }

            return refData;
        }

        /// <summary>
        /// メタデータJSONを作成
        /// </summary>
        private static string CreateMetadataJson(AvatarDynamicsConfig config, string avatarName)
        {
            var metadata = new Dictionary<string, object>
            {
                { "version", EXPORT_VERSION },
                { "configName", config.configName },
                { "sourceAvatarName", avatarName },
                { "exportDate", config.exportDate },
                { "componentCount", config.ComponentCount }
            };

            return JsonUtility.ToJson(metadata, true);
        }

        /// <summary>
        /// エクスポートダイアログを表示
        /// </summary>
        public static void ShowExportDialog(GameObject avatarDynamicsRoot, GameObject avatarRoot)
        {
            if (avatarDynamicsRoot == null)
            {
                EditorUtility.DisplayDialog("エクスポートエラー", "AvatarDynamicsオブジェクトが選択されていません", "OK");
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Prefab (.prefab)"), false, () =>
            {
                var path = EditorUtility.SaveFilePanel(
                    "Prefabをエクスポート",
                    "Assets",
                    avatarDynamicsRoot.name,
                    "prefab");

                if (!string.IsNullOrEmpty(path))
                {
                    // Assets/からの相対パスに変換
                    if (path.StartsWith(Application.dataPath))
                    {
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    var result = ExportAsPrefab(avatarDynamicsRoot, avatarRoot, path);
                    EditorUtility.DisplayDialog(result.Success ? "成功" : "エラー", result.Message, "OK");
                }
            });

            menu.AddItem(new GUIContent("Avatar Dynamics Config (.adcr)"), false, () =>
            {
                var path = EditorUtility.SaveFilePanel(
                    ".adcrファイルをエクスポート",
                    "",
                    avatarDynamicsRoot.name,
                    "adcr");

                if (!string.IsNullOrEmpty(path))
                {
                    var result = ExportAsAdcr(avatarDynamicsRoot, avatarRoot, path);
                    EditorUtility.DisplayDialog(result.Success ? "成功" : "エラー", result.Message, "OK");
                }
            });

            menu.AddItem(new GUIContent("Unity Package (.unitypackage)"), false, () =>
            {
                var path = EditorUtility.SaveFilePanel(
                    "UnityPackageをエクスポート",
                    "",
                    avatarDynamicsRoot.name,
                    "unitypackage");

                if (!string.IsNullOrEmpty(path))
                {
                    var result = ExportAsUnityPackage(avatarDynamicsRoot, avatarRoot, path);
                    EditorUtility.DisplayDialog(result.Success ? "成功" : "エラー", result.Message, "OK");
                }
            });

            menu.ShowAsContext();
        }
    }
}
