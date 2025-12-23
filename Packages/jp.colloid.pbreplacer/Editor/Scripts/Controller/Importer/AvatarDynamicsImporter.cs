using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// AvatarDynamics設定をインポートするクラス
    /// </summary>
    public static class AvatarDynamicsImporter
    {
        private const string ADCR_EXTENSION = ".adcr";

        /// <summary>
        /// インポート結果
        /// </summary>
        public class ImportResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public GameObject ImportedObject { get; set; }
            public AvatarDynamicsConfig Config { get; set; }
        }

        /// <summary>
        /// .adcrファイルをインポート
        /// </summary>
        public static ImportResult ImportFromAdcr(string adcrPath, Transform targetParent = null)
        {
            var result = new ImportResult();

            try
            {
                if (!File.Exists(adcrPath))
                {
                    result.Success = false;
                    result.Message = $"ファイルが見つかりません: {adcrPath}";
                    return result;
                }

                // 一時ディレクトリに解凍
                var tempDir = Path.Combine(Path.GetTempPath(), $"PBReplacer_Import_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    ZipFile.ExtractToDirectory(adcrPath, tempDir);

                    // Prefabファイルを検索
                    var prefabFiles = Directory.GetFiles(tempDir, "*.prefab");
                    if (prefabFiles.Length == 0)
                    {
                        result.Success = false;
                        result.Message = ".adcrファイル内にPrefabが見つかりません";
                        return result;
                    }

                    var prefabPath = prefabFiles[0];
                    var prefabName = Path.GetFileNameWithoutExtension(prefabPath);

                    // Assets内に一時的にコピー
                    var tempAssetPath = $"Assets/Temp_PBReplacer_Import_{prefabName}_{Guid.NewGuid()}.prefab";
                    File.Copy(prefabPath, Path.GetFullPath(tempAssetPath));

                    // .metaファイルもコピー（あれば）
                    var metaPath = prefabPath + ".meta";
                    if (File.Exists(metaPath))
                    {
                        File.Copy(metaPath, Path.GetFullPath(tempAssetPath) + ".meta");
                    }

                    AssetDatabase.Refresh();

                    // Prefabを読み込み
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(tempAssetPath);
                    if (prefab == null)
                    {
                        result.Success = false;
                        result.Message = "Prefabの読み込みに失敗しました";
                        AssetDatabase.DeleteAsset(tempAssetPath);
                        return result;
                    }

                    // インスタンス化
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    if (targetParent != null)
                    {
                        instance.transform.SetParent(targetParent);
                        instance.transform.localPosition = Vector3.zero;
                        instance.transform.localRotation = Quaternion.identity;
                        instance.transform.localScale = Vector3.one;
                    }

                    // Prefabリンクを解除
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                    // 一時Prefabを削除
                    AssetDatabase.DeleteAsset(tempAssetPath);

                    // 結果を設定
                    result.Success = true;
                    result.Message = $"インポートしました: {prefabName}";
                    result.ImportedObject = instance;
                    result.Config = instance.GetComponent<AvatarDynamicsConfig>();

                    Undo.RegisterCreatedObjectUndo(instance, "Import AvatarDynamics");
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
                result.Message = $"インポート中にエラーが発生しました: {ex.Message}";
                Debug.LogError($"[PBReplacer] インポートエラー: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// PrefabアセットからAvatarDynamicsをインポート
        /// </summary>
        public static ImportResult ImportFromPrefab(GameObject prefab, Transform targetParent = null)
        {
            var result = new ImportResult();

            try
            {
                if (prefab == null)
                {
                    result.Success = false;
                    result.Message = "Prefabが指定されていません";
                    return result;
                }

                // Prefabかどうかを確認
                var prefabType = PrefabUtility.GetPrefabAssetType(prefab);
                GameObject instance;

                if (prefabType != PrefabAssetType.NotAPrefab)
                {
                    // Prefabアセットからインスタンス化
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                }
                else
                {
                    // 通常のGameObjectの場合は複製
                    instance = UnityEngine.Object.Instantiate(prefab);
                    instance.name = prefab.name;
                }

                if (targetParent != null)
                {
                    instance.transform.SetParent(targetParent);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    instance.transform.localScale = Vector3.one;
                }

                // Prefabの場合はリンクを解除
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                {
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                result.Success = true;
                result.Message = $"インポートしました: {instance.name}";
                result.ImportedObject = instance;
                result.Config = instance.GetComponent<AvatarDynamicsConfig>();

                Undo.RegisterCreatedObjectUndo(instance, "Import AvatarDynamics");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"インポート中にエラーが発生しました: {ex.Message}";
                Debug.LogError($"[PBReplacer] インポートエラー: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// ファイルパスに基づいて自動的にインポート形式を判定
        /// </summary>
        public static ImportResult ImportAuto(string path, Transform targetParent = null)
        {
            if (path.EndsWith(ADCR_EXTENSION, StringComparison.OrdinalIgnoreCase))
            {
                return ImportFromAdcr(path, targetParent);
            }
            else if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                return ImportFromPrefab(prefab, targetParent);
            }
            else
            {
                return new ImportResult
                {
                    Success = false,
                    Message = $"サポートされていないファイル形式です: {Path.GetExtension(path)}"
                };
            }
        }

        /// <summary>
        /// インポートダイアログを表示
        /// </summary>
        public static void ShowImportDialog(Transform targetParent = null)
        {
            var path = EditorUtility.OpenFilePanel(
                "AvatarDynamics設定をインポート",
                "",
                "adcr,prefab");

            if (!string.IsNullOrEmpty(path))
            {
                ImportResult result;

                if (path.EndsWith(ADCR_EXTENSION, StringComparison.OrdinalIgnoreCase))
                {
                    result = ImportFromAdcr(path, targetParent);
                }
                else
                {
                    // パスがAssets内かどうか確認
                    string assetPath;
                    if (path.StartsWith(Application.dataPath))
                    {
                        assetPath = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("エラー", "Assets外のPrefabは直接インポートできません。.adcr形式を使用してください。", "OK");
                        return;
                    }

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    result = ImportFromPrefab(prefab, targetParent);
                }

                EditorUtility.DisplayDialog(result.Success ? "成功" : "エラー", result.Message, "OK");

                if (result.Success && result.ImportedObject != null)
                {
                    Selection.activeGameObject = result.ImportedObject;
                }
            }
        }
    }
}
