using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
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
        private ProcessorSettings _settings;
        
        // コンストラクタ
	    public ComponentProcessor(PBReplacerSettings settings = null)
        {
        	var pbreplacerSettings = settings ?? PBReplacerSettings.Load();   
	        _settings = pbreplacerSettings.GetProcessorSettings();
        }
        
        // 設定のゲッター・セッター
        public ProcessorSettings Settings
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

                    // 親を設定
                    newObj.transform.SetParent(componentFolder);
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
                
                // 元のコンポーネントを削除
                foreach (var component in components)
                {
                    if (component != null)
                    {
                        Undo.DestroyObjectImmediate(component);
                    }
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
        
        #region PhysBone特有の処理メソッド
        
        /// <summary>
        /// PhysBoneとPhysBoneColliderを一緒に処理するメソッド
        /// </summary>
        public ProcessingResult ProcessPhysBones(
            GameObject avatar, 
            List<VRCPhysBone> physBones, 
            List<VRCPhysBoneCollider> colliders)
        {
            // 処理をヘルパーに委譲
            return ComponentProcessingHelper.ProcessPhysBones(this, avatar, physBones, colliders);
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
		    string subfolder) where T : Component
	    {
		    // 処理をヘルパーに委譲
		    return ComponentProcessingHelper.ProcessContacts(this, avatar, contacts, subfolder);
	    }
        
        #endregion
        
        #region ユーティリティメソッド
        
        /// <summary>
        /// ルートオブジェクトを準備
        /// </summary>
        public GameObject PrepareRootObject(GameObject avatar)
        {
            // 既存のルートオブジェクトを検索
            var existingRoot = avatar.transform.Find(_settings.RootObjectName);
            if (existingRoot != null)
            {
                return existingRoot.gameObject;
            }

            // プレハブを読み込み
            var prefab = Resources.Load<GameObject>(_settings.RootPrefabName);
            if (prefab == null)
            {
                throw new Exception($"{_settings.RootPrefabName}プレハブが見つかりません");
            }

            // プレハブをインスタンス化
            var rootObject = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
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
        /// コンポーネントフォルダを準備
        /// </summary>
        public Transform PrepareComponentFolder(GameObject parent, string folderName)
        {
            var folder = parent.transform.Find(folderName);
            if (folder != null)
            {
                return folder;
            }

            // フォルダを新規作成
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