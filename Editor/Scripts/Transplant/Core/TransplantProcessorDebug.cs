using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// TransplantProcessorのデバッグ用MenuItemコマンド
    /// </summary>
    internal static class TransplantProcessorDebug
    {
        [MenuItem("PBReplacer/Debug/Test TransplantProcessor")]
        static void TestTransplantProcessor()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[PBReplacer] GameObjectを選択してください");
                return;
            }

            Undo.SetCurrentGroupName("Test TransplantProcessor");

            // AvatarDynamicsルートを作成
            var root = TransplantProcessor.EnsureAvatarDynamicsRoot(selected);
            Debug.Log($"[PBReplacer] AvatarDynamicsRoot: {root.name} (parent: {selected.name})");

            // フォルダパスを作成
            var pbFolder = TransplantProcessor.EnsureFolderPath(root.transform, "PhysBones");
            Debug.Log($"[PBReplacer] Folder: {GetPath(pbFolder)}");

            var constraintFolder = TransplantProcessor.EnsureFolderPath(root.transform, "Constraints/Position");
            Debug.Log($"[PBReplacer] Folder: {GetPath(constraintFolder)}");

            var contactFolder = TransplantProcessor.EnsureFolderPath(root.transform, "Contacts/Sender");
            Debug.Log($"[PBReplacer] Folder: {GetPath(contactFolder)}");

            // 補助オブジェクトを作成（Hipsボーン想定）
            var hips = selected.transform.Find("Armature/Hips");
            if (hips != null)
            {
                var intermediate = TransplantProcessor.EnsureIntermediateObject(hips, "ColliderHelper");
                Debug.Log($"[PBReplacer] IntermediateObject: {GetPath(intermediate)}");
            }
            else
            {
                Debug.Log("[PBReplacer] Armature/Hipsが見つからないため補助オブジェクトテストをスキップ");
            }

            Debug.Log("[PBReplacer] TransplantProcessor テスト完了");
        }

        static string GetPath(Transform t)
        {
            string path = t.name;
            var parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
