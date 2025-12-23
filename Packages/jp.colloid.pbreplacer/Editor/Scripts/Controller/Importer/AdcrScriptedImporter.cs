using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEditor;
using UnityEditor.AssetImporters;

namespace colloid.PBReplacer
{
    /// <summary>
    /// .adcrファイルをUnityにインポートするためのScriptedImporter
    /// </summary>
    [ScriptedImporter(1, "adcr")]
    public class AdcrScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            try
            {
                // 一時ディレクトリに解凍
                var tempDir = Path.Combine(Path.GetTempPath(), $"PBReplacer_Preview_{System.Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    ZipFile.ExtractToDirectory(ctx.assetPath, tempDir);

                    // メタデータを読み込み
                    var metadataPath = Path.Combine(tempDir, "metadata.json");
                    string configName = Path.GetFileNameWithoutExtension(ctx.assetPath);
                    string sourceAvatarName = "Unknown";
                    string exportDate = "";
                    int componentCount = 0;

                    if (File.Exists(metadataPath))
                    {
                        var json = File.ReadAllText(metadataPath);
                        var metadata = JsonUtility.FromJson<AdcrMetadata>(json);
                        if (metadata != null)
                        {
                            configName = metadata.configName ?? configName;
                            sourceAvatarName = metadata.sourceAvatarName ?? sourceAvatarName;
                            exportDate = metadata.exportDate ?? "";
                            componentCount = metadata.componentCount;
                        }
                    }

                    // プレビュー用のScriptableObjectを作成
                    var preview = ScriptableObject.CreateInstance<AdcrAssetPreview>();
                    preview.name = configName;
                    preview.configName = configName;
                    preview.sourceAvatarName = sourceAvatarName;
                    preview.exportDate = exportDate;
                    preview.componentCount = componentCount;
                    preview.adcrPath = ctx.assetPath;

                    ctx.AddObjectToAsset("main", preview);
                    ctx.SetMainObject(preview);
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
            catch (System.Exception ex)
            {
                Debug.LogError($"[PBReplacer] .adcrファイルのインポートに失敗しました: {ex.Message}");

                // エラー時のフォールバック
                var errorPreview = ScriptableObject.CreateInstance<AdcrAssetPreview>();
                errorPreview.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
                errorPreview.configName = errorPreview.name;
                errorPreview.importError = ex.Message;
                errorPreview.adcrPath = ctx.assetPath;

                ctx.AddObjectToAsset("main", errorPreview);
                ctx.SetMainObject(errorPreview);
            }
        }

        /// <summary>
        /// メタデータJSONのデシリアライズ用クラス
        /// </summary>
        [System.Serializable]
        private class AdcrMetadata
        {
            public string version;
            public string configName;
            public string sourceAvatarName;
            public string exportDate;
            public int componentCount;
        }
    }

    /// <summary>
    /// .adcrファイルのプレビュー用ScriptableObject
    /// </summary>
    public class AdcrAssetPreview : ScriptableObject
    {
        [Header("設定情報")]
        public string configName;
        public string sourceAvatarName;
        public string exportDate;
        public int componentCount;

        [Header("ファイル情報")]
        public string adcrPath;
        public string importError;

        /// <summary>
        /// エラーがあるかどうか
        /// </summary>
        public bool HasError => !string.IsNullOrEmpty(importError);
    }

    /// <summary>
    /// AdcrAssetPreviewのカスタムエディタ
    /// </summary>
    [CustomEditor(typeof(AdcrAssetPreview))]
    public class AdcrAssetPreviewEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var preview = (AdcrAssetPreview)target;

            if (preview.HasError)
            {
                EditorGUILayout.HelpBox($"インポートエラー: {preview.importError}", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Avatar Dynamics Config", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("設定名", preview.configName);
            EditorGUILayout.LabelField("元アバター", preview.sourceAvatarName);
            EditorGUILayout.LabelField("エクスポート日時", preview.exportDate);
            EditorGUILayout.LabelField("コンポーネント数", preview.componentCount.ToString());

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "このファイルをHierarchy内のアバターにドラッグ＆ドロップすると、" +
                "AvatarDynamics設定が自動的にインポートされます。",
                MessageType.Info);

            EditorGUILayout.Space();

            if (GUILayout.Button("アバターにインポート..."))
            {
                ShowAvatarSelectionDialog(preview);
            }
        }

        private void ShowAvatarSelectionDialog(AdcrAssetPreview preview)
        {
            // VRCAvatarDescriptorを持つオブジェクトを検索
            var avatars = FindObjectsByType<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(FindObjectsSortMode.None);

            if (avatars.Length == 0)
            {
                EditorUtility.DisplayDialog("エラー", "シーン内にアバターが見つかりません", "OK");
                return;
            }

            var menu = new GenericMenu();
            foreach (var avatar in avatars)
            {
                var avatarName = avatar.name;
                var avatarTransform = avatar.transform;
                menu.AddItem(new GUIContent(avatarName), false, () =>
                {
                    var result = AvatarDynamicsImporter.ImportFromAdcr(preview.adcrPath, avatarTransform);
                    if (result.Success)
                    {
                        Selection.activeGameObject = result.ImportedObject;
                        EditorUtility.DisplayDialog("成功", result.Message, "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("エラー", result.Message, "OK");
                    }
                });
            }
            menu.ShowAsContext();
        }
    }
}
