using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// Hierarchyウィンドウへのドラッグ＆ドロップを処理するクラス
    /// </summary>
    [InitializeOnLoad]
    public static class HierarchyDragDropHandler
    {
        static HierarchyDragDropHandler()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGUI;
            DragAndDrop.AddDropHandler(OnHierarchyDrop);
        }

        private static void OnHierarchyWindowItemOnGUI(int instanceID, Rect selectionRect)
        {
            // ドラッグ中の視覚フィードバック
            if (Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform)
            {
                if (IsAvatarDynamicsAsset())
                {
                    var gameObject = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
                    if (gameObject != null && IsValidDropTarget(gameObject))
                    {
                        // ドロップ可能な視覚フィードバック
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    }
                }
            }
        }

        private static DragAndDropVisualMode OnHierarchyDrop(
            int dropTargetInstanceID,
            HierarchyDropFlags dropMode,
            Transform parentForDraggedObjects,
            bool perform)
        {
            if (!IsAvatarDynamicsAsset())
            {
                return DragAndDropVisualMode.None;
            }

            var targetObject = EditorUtility.InstanceIDToObject(dropTargetInstanceID) as GameObject;

            // ドロップ先がない場合や無効な場合
            if (targetObject == null)
            {
                return DragAndDropVisualMode.None;
            }

            if (!IsValidDropTarget(targetObject))
            {
                return DragAndDropVisualMode.Rejected;
            }

            if (perform)
            {
                // ドロップ実行
                PerformDrop(targetObject.transform);
            }

            return DragAndDropVisualMode.Copy;
        }

        /// <summary>
        /// ドラッグ中のアセットがAvatarDynamics関連かどうか
        /// </summary>
        private static bool IsAvatarDynamicsAsset()
        {
            var objects = DragAndDrop.objectReferences;
            if (objects == null || objects.Length == 0)
            {
                // ファイルパスをチェック
                var paths = DragAndDrop.paths;
                if (paths != null)
                {
                    foreach (var path in paths)
                    {
                        if (path.EndsWith(".adcr", System.StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            foreach (var obj in objects)
            {
                // .adcrアセット（AdcrAssetPreview）
                if (obj is AdcrAssetPreview)
                {
                    return true;
                }

                // AvatarDynamicsConfigを持つPrefab
                if (obj is GameObject go)
                {
                    if (go.GetComponent<AvatarDynamicsConfig>() != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 有効なドロップ先かどうか
        /// </summary>
        private static bool IsValidDropTarget(GameObject target)
        {
            if (target == null)
                return false;

            // アバターのルートまたはその子孫であることを確認
            var avatarDescriptor = target.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            return avatarDescriptor != null;
        }

        /// <summary>
        /// ドロップを実行
        /// </summary>
        private static void PerformDrop(Transform targetParent)
        {
            var objects = DragAndDrop.objectReferences;

            // AdcrAssetPreviewの場合
            foreach (var obj in objects)
            {
                if (obj is AdcrAssetPreview preview)
                {
                    var result = AvatarDynamicsImporter.ImportFromAdcr(preview.adcrPath, targetParent);
                    if (result.Success)
                    {
                        Selection.activeGameObject = result.ImportedObject;
                        Debug.Log($"[PBReplacer] {result.Message}");
                    }
                    else
                    {
                        Debug.LogError($"[PBReplacer] {result.Message}");
                        EditorUtility.DisplayDialog("インポートエラー", result.Message, "OK");
                    }
                    return;
                }
            }

            // Prefabの場合
            foreach (var obj in objects)
            {
                if (obj is GameObject prefab && prefab.GetComponent<AvatarDynamicsConfig>() != null)
                {
                    var result = AvatarDynamicsImporter.ImportFromPrefab(prefab, targetParent);
                    if (result.Success)
                    {
                        Selection.activeGameObject = result.ImportedObject;
                        Debug.Log($"[PBReplacer] {result.Message}");
                    }
                    else
                    {
                        Debug.LogError($"[PBReplacer] {result.Message}");
                        EditorUtility.DisplayDialog("インポートエラー", result.Message, "OK");
                    }
                    return;
                }
            }

            // ファイルパスからの.adcrインポート
            var paths = DragAndDrop.paths;
            if (paths != null)
            {
                foreach (var path in paths)
                {
                    if (path.EndsWith(".adcr", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var result = AvatarDynamicsImporter.ImportFromAdcr(path, targetParent);
                        if (result.Success)
                        {
                            Selection.activeGameObject = result.ImportedObject;
                            Debug.Log($"[PBReplacer] {result.Message}");
                        }
                        else
                        {
                            Debug.LogError($"[PBReplacer] {result.Message}");
                            EditorUtility.DisplayDialog("インポートエラー", result.Message, "OK");
                        }
                        return;
                    }
                }
            }
        }
    }
}
