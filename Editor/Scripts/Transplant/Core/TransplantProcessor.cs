using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 移植先のAvatarDynamics階層構造を作成するプロセッサ
    /// ソース側の階層構造をデスティネーション側に再現する
    /// </summary>
    public static class TransplantProcessor
    {
        /// <summary>
        /// デスティネーションアバター直下にAvatarDynamicsルートオブジェクトを確保する
        /// 既存があればそれを返し、なければ新規作成する（冪等）
        /// </summary>
        /// <param name="destAvatar">デスティネーションアバターのGameObject</param>
        /// <returns>AvatarDynamicsルートのGameObject</returns>
        public static GameObject EnsureAvatarDynamicsRoot(GameObject destAvatar)
        {
            var settings = PBReplacerSettings.Load();
            string rootName = settings.RootObjectName;

            // 既存のルートオブジェクトを検索
            var existing = destAvatar.transform.Find(rootName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            // 新規作成
            var rootObject = new GameObject(rootName);
            rootObject.transform.SetParent(destAvatar.transform);
            rootObject.transform.localPosition = Vector3.zero;
            rootObject.transform.localRotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;

            Undo.RegisterCreatedObjectUndo(rootObject, $"Create {rootName}");

            return rootObject;
        }

        /// <summary>
        /// ルート以下にフォルダパスを再帰的に作成または取得する（冪等）
        /// </summary>
        /// <param name="root">起点となるTransform（通常はAvatarDynamicsルート）</param>
        /// <param name="folderPath">作成するフォルダパス（例: "PhysBones", "Constraints/Position"）</param>
        /// <returns>最深階層のTransform</returns>
        public static Transform EnsureFolderPath(Transform root, string folderPath)
        {
            string[] folders = folderPath.Split('/');
            Transform current = root;

            foreach (string folderName in folders)
            {
                if (string.IsNullOrEmpty(folderName))
                    continue;

                var child = current.Find(folderName);
                if (child != null)
                {
                    current = child;
                    continue;
                }

                // 新規作成
                var folderObj = new GameObject(folderName);
                folderObj.transform.SetParent(current);
                folderObj.transform.localPosition = Vector3.zero;
                folderObj.transform.localRotation = Quaternion.identity;
                folderObj.transform.localScale = Vector3.one;

                Undo.RegisterCreatedObjectUndo(folderObj, $"Create {folderName}");

                current = folderObj.transform;
            }

            return current;
        }

        /// <summary>
        /// 親ボーンの子に補助オブジェクトを作成または取得する（冪等）
        /// コライダー用等のボーン直下の補助オブジェクトに使用
        /// </summary>
        /// <param name="parentBone">親ボーンのTransform</param>
        /// <param name="objectName">作成する補助オブジェクト名</param>
        /// <returns>補助オブジェクトのTransform</returns>
        public static Transform EnsureIntermediateObject(Transform parentBone, string objectName)
        {
            // 既存チェック
            var existing = parentBone.Find(objectName);
            if (existing != null)
            {
                return existing;
            }

            // 新規作成
            var intermediateObj = new GameObject(objectName);
            intermediateObj.transform.SetParent(parentBone);
            intermediateObj.transform.localPosition = Vector3.zero;
            intermediateObj.transform.localRotation = Quaternion.identity;
            intermediateObj.transform.localScale = Vector3.one;

            Undo.RegisterCreatedObjectUndo(intermediateObj, $"Create {objectName}");

            return intermediateObj.transform;
        }
    }
}
