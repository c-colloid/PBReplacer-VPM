using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 処理結果を格納するクラス
    /// </summary>
    public class ProcessingResult
    {
        // 処理が成功したかどうか
        public bool Success { get; set; } = true;
        
        // エラーメッセージ
        public string ErrorMessage { get; set; } = string.Empty;
        
        // 生成された親オブジェクト
        public GameObject RootObject { get; set; }
        
        // 処理されたコンポーネント数
        public int ProcessedComponentCount { get; set; }
        
        // 生成されたオブジェクトのリスト
        public List<GameObject> CreatedObjects { get; set; } = new List<GameObject>();
        
        // 追加情報（コンポーネント固有）
        public Dictionary<string, object> AdditionalData { get; set; } = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// VRCコンポーネントの処理を行う汎用プロセッサ
    /// </summary>
    public class ComponentProcessor
    {
        // プロセッサの設定
        private PBReplacerSettings _settings;

        // コンストラクタ
        public ComponentProcessor(PBReplacerSettings settings = null)
        {
            _settings = settings ?? PBReplacerSettings.Load();
        }

        // 設定のゲッター・セッター
        public PBReplacerSettings Settings
        {
            get => _settings;
            set => _settings = value;
        }
        
        #region 汎用コンポーネント処理メソッド
        
        /// <summary>
        /// 汎用コンポーネント処理メソッド
        /// </summary>
        /// <typeparam name="T">処理対象のコンポーネント型</typeparam>
        /// <param name="avatar">アバターのGameObject</param>
        /// <param name="components">処理対象のコンポーネントリスト</param>
        /// <param name="folderName">コンポーネント配置先フォルダ名</param>
        /// <param name="postProcess">コンポーネント処理後のカスタム処理</param>
        /// <returns>処理結果</returns>
        public ProcessingResult ProcessComponents<T>(
            GameObject avatar, 
            List<T> components,
            string folderName,
            Action<T, T, GameObject, ProcessingResult> postProcess = null) where T : Component
        {
            if (avatar == null)
            {
                return new ProcessingResult
                {
                    Success = false,
                    ErrorMessage = "アバターがnullです"
                };
            }

            var result = new ProcessingResult();

            try
            {
                // Undoグループ開始
                Undo.SetCurrentGroupName($"{typeof(T).Name}置換");
                int undoGroup = Undo.GetCurrentGroup();

                // ルートオブジェクトを準備
                var rootObject = PrepareRootObject(avatar);
                result.RootObject = rootObject;

                // コンポーネントフォルダを準備
                var componentFolder = PrepareComponentFolder(rootObject, folderName);
                
                // 処理対象があるか確認
                if (components == null || components.Count == 0)
                {
                    result.Success = true;
                    return result;
                }

                // コンポーネント処理
                Dictionary<T, T> componentMap = new Dictionary<T, T>();
	            int total = components.Count;
                
	            var hashset = new HashSet<Transform>();
	            var duplicateElements = components.GroupBy(e => e.transform).Where(g => g.Count() > 1).Select(g => g.Key);

                for (int i = 0; i < total; i++)
                {
                    // 進捗表示
                    if (_settings.ShowProgressBar)
                    {
                        EditorUtility.DisplayProgressBar(
                            $"{typeof(T).Name}処理中", 
                            $"{i + 1}/{total}", 
                            (float)i / total);
                    }

                    T component = components[i];
                    if (component == null) continue;

                    // 新しいオブジェクトを作成
                    string objName = GetSafeObjectName(component.name);
                    GameObject newObj = new GameObject(objName);
	                result.CreatedObjects.Add(newObj);
                    
	                Transform targetFolder = componentFolder;
	                // 同じオブジェクトに付いているコンポーネントをまとめるオブジェクトを追加
	                if (duplicateElements.Contains(component.transform))
	                {
	                	targetFolder = PrepareComponentFolder(componentFolder.gameObject, component.name);
	                }

                    // 親を設定
	                newObj.transform.SetParent(targetFolder);
                    newObj.transform.localPosition = Vector3.zero;
                    newObj.transform.localRotation = Quaternion.identity;
                    newObj.transform.localScale = Vector3.one;

                    // 新しいコンポーネントを追加
                    T newComponent = Undo.AddComponent<T>(newObj);

                    // プロパティをコピー
                    CopyComponentProperties(component, newComponent);
                    
                    // コンポーネントマッピングを保存
                    componentMap[component] = newComponent;
                    
                    // カスタム後処理を実行
                    postProcess?.Invoke(component, newComponent, newObj, result);
                }
                
                // 処理したコンポーネント数を設定
                result.ProcessedComponentCount = componentMap.Count;
                
                // 元のコンポーネントを削除待ちリストに追加
                foreach (var component in components)
                {
                    ProcessingContext.Instance.AddPendingDeletion(component);
                }

                // Undoグループ終了
                Undo.CollapseUndoOperations(undoGroup);
                
                // 進捗バーをクリア
                if (_settings.ShowProgressBar)
                {
                    EditorUtility.ClearProgressBar();
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"処理中にエラーが発生しました: {ex.Message}";
                Debug.LogError($"{typeof(T).Name}処理中にエラー: {ex.Message}\n{ex.StackTrace}");
                
                if (_settings.ShowProgressBar)
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            return result;
        }
        
        #endregion

        #region コンストレイント特有の処理メソッド
        
        /// <summary>
        /// コンストレイントを処理するメソッド
        /// </summary>
        public ProcessingResult ProcessConstraints<T>(
            GameObject avatar,
            List<T> constraints,
            string subfolder) where T : VRCConstraintBase
        {
            // 処理をヘルパーに委譲
            return ComponentProcessingHelper.ProcessConstraints(this, avatar, constraints, subfolder);
        }
        
        #endregion
        
        #region コンタクト特有の処理メソッド
        
	    /// <summary>
	    /// ContactSenderを処理するメソッド
	    /// </summary>
	    public ProcessingResult ProcessContacts<T>(
		    GameObject avatar,
		    List<T> contacts,
		    string subfolder) where T : ContactBase
	    {
		    // 処理をヘルパーに委譲
		    return ComponentProcessingHelper.ProcessContacts(this, avatar, contacts, subfolder);
	    }
        
        #endregion
        
        #region ユーティリティメソッド
        
        /// <summary>
        /// ルートオブジェクトを準備
        /// </summary>
        /// <param name="avatar">アバターのGameObject</param>
        /// <param name="isNewlyCreated">新規作成された場合はtrue</param>
        public GameObject PrepareRootObject(GameObject avatar, out bool isNewlyCreated)
        {
            isNewlyCreated = false;

            // 既存のルートオブジェクトを検索
            var existingRoot = avatar.transform.Find(_settings.RootObjectName);
            if (existingRoot != null)
            {
                return existingRoot.gameObject;
            }

            isNewlyCreated = true;

            // プレハブを読み込み
            var prefab = Resources.Load<GameObject>(_settings.RootPrefabName);
            if (prefab == null)
            {
                throw new Exception($"{_settings.RootPrefabName}プレハブが見つかりません");
            }

            // プレハブをインスタンス化
            var rootObject = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

            if (_settings.UnpackPrefab)
            {
                PrefabUtility.UnpackPrefabInstance(rootObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            if (rootObject == null)
            {
                throw new Exception($"{_settings.RootPrefabName}プレハブのインスタンス化に失敗しました");
            }

            // 親子関係の設定
            rootObject.transform.SetParent(avatar.transform);
            rootObject.transform.localPosition = Vector3.zero;
            rootObject.transform.localRotation = Quaternion.identity;
            rootObject.transform.localScale = Vector3.one;

            // Undo登録
            Undo.RegisterCreatedObjectUndo(rootObject, $"Create {_settings.RootObjectName}");

            return rootObject;
        }

        /// <summary>
        /// ルートオブジェクトを準備（isNewlyCreatedなしのオーバーロード）
        /// </summary>
        public GameObject PrepareRootObject(GameObject avatar)
        {
            return PrepareRootObject(avatar, out _);
        }

        /// <summary>
        /// 使用しないフォルダを削除する
        /// 子にコンポーネントが存在しない空のフォルダのみを削除
        /// </summary>
        /// <param name="rootObject">AvatarDynamicsルートオブジェクト</param>
        /// <param name="foldersToKeep">保持するフォルダ名のリスト（使用予定のフォルダ）</param>
        public void CleanupUnusedFolders(GameObject rootObject, params string[] foldersToKeep)
        {
            if (rootObject == null) return;
            if (!_settings.DestroyUnusedObject) return;

            var keepSet = new HashSet<string>(foldersToKeep);

            // 再帰的に空のフォルダを削除
            CleanupEmptyFoldersRecursive(rootObject.transform, keepSet);
        }

        /// <summary>
        /// 再帰的に空のフォルダを削除する
        /// </summary>
        /// <param name="parent">親のTransform</param>
        /// <param name="foldersToKeep">保持するフォルダ名のセット</param>
        /// <returns>このフォルダが空になった場合はtrue</returns>
        private bool CleanupEmptyFoldersRecursive(Transform parent, HashSet<string> foldersToKeep)
        {
            var childCount = parent.childCount;
            var childrenToRemove = new List<GameObject>();

            // 子オブジェクトを逆順で処理
            for (int i = childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);

                // 子にコンポーネント（Transform以外）があるかチェック
                var components = child.GetComponents<Component>();
                bool hasComponents = components.Length > 1; // Transform以外のコンポーネントがあるか

                if (hasComponents)
                {
                    // コンポーネントがあるオブジェクトは削除しない
                    continue;
                }

                // 再帰的にサブフォルダを処理
                bool isChildEmpty = CleanupEmptyFoldersRecursive(child, foldersToKeep);

                // 子フォルダが空で、保持リストにも含まれていない場合は削除候補
                if (isChildEmpty && !foldersToKeep.Contains(child.name))
                {
                    childrenToRemove.Add(child.gameObject);
                }
            }

            // 削除候補のオブジェクトを削除
            foreach (var child in childrenToRemove)
            {
                Undo.DestroyObjectImmediate(child);
            }

            // このフォルダが空かどうかを返す（子がいない、またはTransformのみ）
            var remainingComponents = parent.GetComponents<Component>();
            return parent.childCount == 0 && remainingComponents.Length <= 1;
        }

        /// <summary>
        /// フォルダ階層を準備する（Prefabからの復元とクリーンアップを含む）
        /// ConstraintDataManagerやContactDataManagerで重複していたパターンを共通化
        /// </summary>
        /// <param name="root">ルートオブジェクト</param>
        /// <param name="parentFolder">親フォルダ名</param>
        /// <param name="subfolders">サブフォルダ名（可変長）</param>
        public void PrepareFolderHierarchy(GameObject root, string parentFolder, params string[] subfolders)
        {
            // 親フォルダをPrefabから復元
            RevertFolderFromPrefab(root, parentFolder);

            // 親フォルダを取得
            var folder = root.transform.Find(parentFolder);
            if (folder != null)
            {
                // 各サブフォルダをPrefabから復元
                foreach (var subfolder in subfolders)
                {
                    RevertFolderFromPrefab(folder.gameObject, subfolder);
                }
            }

            // 未使用フォルダをクリーンアップ
            CleanupUnusedFolders(root, parentFolder);
        }

        /// <summary>
        /// Prefabから削除されたフォルダを復元する（サブフォルダも含む）
        /// </summary>
        /// <param name="rootObject">AvatarDynamicsルートオブジェクト</param>
        /// <param name="folderPath">復元するフォルダパス（例: "Contacts/Sender"）</param>
        public void RevertFolderFromPrefab(GameObject rootObject, string folderPath)
        {
            if (rootObject == null) return;

            // パスを分割して階層的に処理
            var folders = folderPath.Split('/');
            Transform currentParent = rootObject.transform;

            foreach (var folderName in folders)
            {
                if (string.IsNullOrEmpty(folderName)) continue;

                var existingFolder = currentParent.Find(folderName);
                if (existingFolder != null)
                {
                    currentParent = existingFolder;
                    continue;
                }

                // フォルダが存在しない場合、Prefabから復元を試みる
                RevertSingleFolderFromPrefab(currentParent.gameObject, folderName);

                // 復元後に再度検索
                existingFolder = currentParent.Find(folderName);
                if (existingFolder != null)
                {
                    currentParent = existingFolder;
                }
                else
                {
                    // 復元に失敗した場合は終了（PrepareComponentFolderで新規作成される）
                    return;
                }
            }
        }

        /// <summary>
        /// 単一のフォルダをPrefabから復元する
        /// </summary>
        /// <param name="parent">親オブジェクト</param>
        /// <param name="folderName">復元するフォルダ名</param>
        /// <returns>復元に成功した場合はtrue</returns>
        private bool RevertSingleFolderFromPrefab(GameObject parent, string folderName)
        {
            // Prefabインスタンスかどうか確認
            if (!PrefabUtility.IsPartOfPrefabInstance(parent))
            {
                return false;
            }

            try
            {
                // Prefabインスタンスのルートを取得（GetRemovedGameObjectsはルートから呼び出す必要がある）
                var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(parent);
                if (prefabRoot == null)
                {
                    return false;
                }

                // parentに対応するPrefabアセット内のオブジェクトを取得
                var parentInPrefab = PrefabUtility.GetCorrespondingObjectFromSource(parent);
                if (parentInPrefab == null)
                {
                    return false;
                }

                // GetRemovedGameObjectsで削除されたオブジェクトを取得
                var removedObjects = PrefabUtility.GetRemovedGameObjects(prefabRoot);
                foreach (var removed in removedObjects)
                {
                    if (removed.assetGameObject == null) continue;

                    // 削除されたオブジェクトの名前と親が一致するか確認
                    if (removed.assetGameObject.name == folderName &&
                        removed.assetGameObject.transform.parent == parentInPrefab.transform)
                    {
                        removed.Revert();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PBReplacer] フォルダ復元中にエラー: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// コンポーネントフォルダを準備
        /// </summary>
        public Transform PrepareComponentFolder(GameObject parent, string folderName)
        {
            // パスが階層的かどうか確認
            if (folderName.Contains("/"))
            {
                string[] folders = folderName.Split('/');
                Transform currentParent = parent.transform;
                Transform result = null;

                // 各階層を順番に処理
                foreach (string splitFolderName in folders)
                {
                    if (string.IsNullOrEmpty(splitFolderName))
                        continue;

                    // 再帰的に次の階層のフォルダを準備
                    GameObject currentParentGO = currentParent.gameObject;
                    Transform childTransform = PrepareComponentFolder(currentParentGO, splitFolderName);
                    currentParent = childTransform;
                    result = childTransform;
                }

                return result;
            }

            // 既存のフォルダを検索
            var folder = parent.transform.Find(folderName);
            if (folder != null)
            {
                return folder;
            }

            // Prefabから復元を試みる
            if (RevertSingleFolderFromPrefab(parent, folderName))
            {
                // 復元後に再度検索
                folder = parent.transform.Find(folderName);
                if (folder != null)
                {
                    return folder;
                }
            }

            // 新規作成
            var folderObj = new GameObject(folderName);
            folderObj.transform.SetParent(parent.transform);
            folderObj.transform.localPosition = Vector3.zero;
            folderObj.transform.localRotation = Quaternion.identity;
            folderObj.transform.localScale = Vector3.one;

            // Undo登録
            Undo.RegisterCreatedObjectUndo(folderObj, $"Create {folderName}");

            return folderObj.transform;
        }
        
        /// <summary>
        /// コンポーネントプロパティをコピー
        /// </summary>
        public void CopyComponentProperties(Component source, Component destination)
        {
            if (source == null || destination == null)
                return;

            // リフレクションでプロパティをコピー
            CopyFieldsAndProperties(source, destination);
            
            // SerializedObjectでシリアライズされたフィールドをコピー
            CopySerializedProperties(source, destination);
        }
        
        /// <summary>
        /// フィールドとプロパティをコピー
        /// </summary>
        private void CopyFieldsAndProperties(Component source, Component destination)
        {
            Type type = source.GetType();
            
            // パブリックフィールドをコピー
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                try
                {
                    object value = field.GetValue(source);
                    field.SetValue(destination, value);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"フィールド '{field.Name}' のコピー中にエラーが発生しました: {ex.Message}");
                }
            }

            // パブリックプロパティで書き込み可能なものをコピー
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo property in properties)
            {
                if (property.CanWrite && property.CanRead)
                {
                    try
                    {
                        object value = property.GetValue(source);
                        property.SetValue(destination, value);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"プロパティ '{property.Name}' のコピー中にエラーが発生しました: {ex.Message}");
                    }
                }
            }
        }
        
        /// <summary>
        /// シリアライズされたプロパティをコピー
        /// </summary>
        private void CopySerializedProperties(Component source, Component destination)
        {
            var serializedObject = new SerializedObject(source);
            var targetObject = new SerializedObject(destination);
            
            SerializedProperty prop = serializedObject.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    targetObject.CopyFromSerializedProperty(prop);
                }
                while (prop.NextVisible(false));
            }
            
            targetObject.ApplyModifiedProperties();
        }
        
        /// <summary>
        /// 安全なオブジェクト名を取得
        /// </summary>
        public string GetSafeObjectName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "unnamed";

            // Unity上での無効な文字を置換
            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
        
        #endregion
    }
}