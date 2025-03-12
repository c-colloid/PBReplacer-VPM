using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;

namespace colloid.PBReplacer
{
    /// <summary>
    /// PhysBoneとPhysBoneColliderコンポーネントの処理を担当するクラス
    /// </summary>
    public class PhysBoneProcessor
    {
        #region Properties
        // 設定
        private readonly PBReplacerSettings _settings;
        
        // PhysBoneの親オブジェクト名
        private const string PHYSBONES_PARENT_NAME = "PhysBones";
        
        // PhysBoneColliderの親オブジェクト名
        private const string COLLIDERS_PARENT_NAME = "PhysBoneColliders";
        
        // Constraintの親オブジェクト名
        private const string CONSTRAINTS_PARENT_NAME = "Constraints";
        
        // 接触の親オブジェクト名
        private const string CONTACTS_PARENT_NAME = "Contacts";
        
        // AvatarDynamicsのプレハブ名
        private const string AVATAR_DYNAMICS_PREFAB_NAME = "AvatarDynamics";
        #endregion

        #region Constructor
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="settings">プロセッサの設定</param>
        public PhysBoneProcessor(PBReplacerSettings settings = null)
        {
            _settings = settings ?? new PBReplacerSettings();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// アバター上のPhysBoneコンポーネントを処理
        /// </summary>
        /// <param name="avatar">対象のアバター</param>
        /// <param name="physBones">処理対象のPhysBone</param>
        /// <param name="colliders">処理対象のPhysBoneCollider</param>
        /// <returns>処理結果</returns>
        public ProcessingResult ProcessPhysBones(
            GameObject avatar, 
            List<VRCPhysBone> physBones, 
            List<VRCPhysBoneCollider> colliders)
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
                Undo.SetCurrentGroupName("PBReplacer - PhysBone置換");
                int undoGroup = Undo.GetCurrentGroup();

                // AvatarDynamicsオブジェクトを準備
                var avatarDynamics = PrepareAvatarDynamicsObject(avatar);
                result.AvatarDynamicsObject = avatarDynamics;

                // コライダーを先に処理（PhysBoneがコライダーを参照するため）
                if (colliders != null && colliders.Count > 0)
                {
                    var colliderResult = ProcessPhysBoneColliders(avatarDynamics, colliders);
                    result.ProcessedColliderCount = colliderResult.Count;
                    result.CreatedObjects.AddRange(colliderResult.CreatedObjects);
                }

                // PhysBoneを処理
                if (physBones != null && physBones.Count > 0)
                {
                    var pbResult = ProcessPhysBoneComponents(avatarDynamics, physBones);
                    result.ProcessedPhysBoneCount = pbResult.Count;
                    result.CreatedObjects.AddRange(pbResult.CreatedObjects);
                }

                // Undoグループ終了
                Undo.CollapseUndoOperations(undoGroup);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"処理中にエラーが発生しました: {ex.Message}";
                Debug.LogError($"PhysBone処理中にエラー: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// AvatarDynamicsオブジェクトを準備
        /// </summary>
        /// <param name="avatar">対象のアバター</param>
        /// <returns>準備されたAvatarDynamicsオブジェクト</returns>
        private GameObject PrepareAvatarDynamicsObject(GameObject avatar)
        {
            // 既存のAvatarDynamicsオブジェクトを検索
            var existingAvatarDynamics = avatar.transform.Find(AVATAR_DYNAMICS_PREFAB_NAME);
            if (existingAvatarDynamics != null)
            {
                return existingAvatarDynamics.gameObject;
            }

            // プレハブを読み込み
            var prefab = Resources.Load<GameObject>(AVATAR_DYNAMICS_PREFAB_NAME);
            if (prefab == null)
            {
                throw new Exception($"{AVATAR_DYNAMICS_PREFAB_NAME}プレハブが見つかりません");
            }

            // プレハブをインスタンス化
            var avatarDynamics = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (avatarDynamics == null)
            {
                throw new Exception($"{AVATAR_DYNAMICS_PREFAB_NAME}プレハブのインスタンス化に失敗しました");
            }

            // 親子関係の設定
            avatarDynamics.transform.SetParent(avatar.transform);
            avatarDynamics.transform.localPosition = Vector3.zero;
            avatarDynamics.transform.localRotation = Quaternion.identity;
            avatarDynamics.transform.localScale = Vector3.one;

            // Undo登録
            Undo.RegisterCreatedObjectUndo(avatarDynamics, $"Create {AVATAR_DYNAMICS_PREFAB_NAME}");

            return avatarDynamics;
        }

        /// <summary>
        /// PhysBoneColliderコンポーネントを処理
        /// </summary>
        /// <param name="avatarDynamics">AvatarDynamicsオブジェクト</param>
        /// <param name="colliders">処理対象のコライダーリスト</param>
        /// <returns>処理結果</returns>
        private (int Count, List<GameObject> CreatedObjects) ProcessPhysBoneColliders(
            GameObject avatarDynamics, 
            List<VRCPhysBoneCollider> colliders)
        {
            List<GameObject> createdObjects = new List<GameObject>();
            Dictionary<VRCPhysBoneCollider, VRCPhysBoneCollider> colliderMap = 
                new Dictionary<VRCPhysBoneCollider, VRCPhysBoneCollider>();

            // PhysBoneCollidersの親オブジェクトを取得
            var collidersParent = avatarDynamics.transform.Find(COLLIDERS_PARENT_NAME);
            if (collidersParent == null)
            {
                throw new Exception($"{COLLIDERS_PARENT_NAME}親オブジェクトが見つかりません");
            }

            int total = colliders.Count;
            for (int i = 0; i < total; i++)
            {
                if (_settings.ShowProgressBar)
                {
                    EditorUtility.DisplayProgressBar(
                        "PhysBoneCollider処理中", 
                        $"{i + 1}/{total}", 
                        (float)i / total);
                }

                VRCPhysBoneCollider collider = colliders[i];
                if (collider == null) continue;

                // RootTransformが設定されていない場合、自動設定
                if (collider.rootTransform == null)
                {
                    collider.rootTransform = collider.transform;
                }

                // 新しいオブジェクトを作成
                string objName = GetSafeObjectName(collider.rootTransform.name);
                GameObject newObj = new GameObject(objName);
                createdObjects.Add(newObj);

                // 親を設定
                if (_settings.PreserveHierarchy && collider.rootTransform != collider.transform)
                {
                    // 階層構造を維持する場合
                    Transform parentTransform = collidersParent.Find(collider.name);
                    if (parentTransform == null)
                    {
                        GameObject parentObj = new GameObject(GetSafeObjectName(collider.name));
                        parentObj.transform.SetParent(collidersParent);
                        parentObj.transform.localPosition = Vector3.zero;
                        parentObj.transform.localRotation = Quaternion.identity;
                        parentObj.transform.localScale = Vector3.one;
                        newObj.transform.SetParent(parentObj.transform);
                        createdObjects.Add(parentObj);
                    }
                    else
                    {
                        newObj.transform.SetParent(parentTransform);
                    }
                }
                else
                {
                    // 階層構造を維持しない場合、直接親の下に配置
                    newObj.transform.SetParent(collidersParent);
                }

                // 位置を設定
                newObj.transform.localPosition = Vector3.zero;
                newObj.transform.localRotation = Quaternion.identity;
                newObj.transform.localScale = Vector3.one;

                // 新しいコライダーコンポーネントを追加
                var newCollider = Undo.AddComponent<VRCPhysBoneCollider>(newObj);

                // プロパティをコピー
                CopyComponentProperties(collider, newCollider);

                // マッピングを保存
                colliderMap[collider] = newCollider;
            }

            // PhysBoneのコライダー参照を更新
            var allPhysBones = GameObject.FindObjectsOfType<VRCPhysBone>(true);
            foreach (var pb in allPhysBones)
            {
                if (pb == null || pb.colliders == null) continue;

                bool modified = false;
                for (int i = 0; i < pb.colliders.Count; i++)
                {
                    var oldCollider = pb.colliders[i] as VRCPhysBoneCollider;
                    if (oldCollider != null && colliderMap.TryGetValue(oldCollider, out var newCollider))
                    {
                        pb.colliders[i] = newCollider;
                        modified = true;
                    }
                }

                if (modified)
                {
                    EditorUtility.SetDirty(pb);
                }
            }

            // 古いコライダーを削除
            foreach (var collider in colliders)
            {
                if (collider != null)
                {
                    Undo.DestroyObjectImmediate(collider);
                }
            }

            if (_settings.ShowProgressBar)
            {
                EditorUtility.ClearProgressBar();
            }

            return (colliders.Count, createdObjects);
        }

        /// <summary>
        /// PhysBoneコンポーネントを処理
        /// </summary>
        /// <param name="avatarDynamics">AvatarDynamicsオブジェクト</param>
        /// <param name="physBones">処理対象のPhysBoneリスト</param>
        /// <returns>処理結果</returns>
        private (int Count, List<GameObject> CreatedObjects) ProcessPhysBoneComponents(
            GameObject avatarDynamics, 
            List<VRCPhysBone> physBones)
        {
            List<GameObject> createdObjects = new List<GameObject>();

            // PhysBonesの親オブジェクトを取得
            var physBonesParent = avatarDynamics.transform.Find(PHYSBONES_PARENT_NAME);
            if (physBonesParent == null)
            {
                throw new Exception($"{PHYSBONES_PARENT_NAME}親オブジェクトが見つかりません");
            }

            int total = physBones.Count;
            for (int i = 0; i < total; i++)
            {
                if (_settings.ShowProgressBar)
                {
                    EditorUtility.DisplayProgressBar(
                        "PhysBone処理中", 
                        $"{i + 1}/{total}", 
                        (float)i / total);
                }

                VRCPhysBone pb = physBones[i];
                if (pb == null) continue;

                // RootTransformが設定されていない場合、自動設定
                if (pb.rootTransform == null)
                {
                    pb.rootTransform = pb.transform;
                }

                // 新しいオブジェクトを作成
                string objName = GetSafeObjectName(pb.rootTransform.name);
                GameObject newObj = new GameObject(objName);
                createdObjects.Add(newObj);

                // 親を設定
                if (_settings.PreserveHierarchy && pb.rootTransform != pb.transform)
                {
                    // 階層構造を維持する場合
                    Transform parentTransform = physBonesParent.Find(pb.name);
                    if (parentTransform == null)
                    {
                        GameObject parentObj = new GameObject(GetSafeObjectName(pb.name));
                        parentObj.transform.SetParent(physBonesParent);
                        parentObj.transform.localPosition = Vector3.zero;
                        parentObj.transform.localRotation = Quaternion.identity;
                        parentObj.transform.localScale = Vector3.one;
                        newObj.transform.SetParent(parentObj.transform);
                        createdObjects.Add(parentObj);
                    }
                    else
                    {
                        newObj.transform.SetParent(parentTransform);
                    }
                }
                else
                {
                    // 階層構造を維持しない場合、直接親の下に配置
                    newObj.transform.SetParent(physBonesParent);
                }

                // 位置を設定
                newObj.transform.localPosition = Vector3.zero;
                newObj.transform.localRotation = Quaternion.identity;
                newObj.transform.localScale = Vector3.one;

                // 新しいPhysBoneコンポーネントを追加
                var newPB = Undo.AddComponent<VRCPhysBone>(newObj);

                // プロパティをコピー
                CopyComponentProperties(pb, newPB);

                // 元のコンポーネントを削除
                Undo.DestroyObjectImmediate(pb);
            }

            if (_settings.ShowProgressBar)
            {
                EditorUtility.ClearProgressBar();
            }

            return (physBones.Count, createdObjects);
        }

        /// <summary>
        /// コンポーネントのプロパティをコピー
        /// </summary>
        /// <param name="source">コピー元コンポーネント</param>
        /// <param name="destination">コピー先コンポーネント</param>
        private void CopyComponentProperties(Component source, Component destination)
        {
            if (source == null || destination == null)
                return;

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
                        // 一部のプロパティはコピーできない場合がある
                        Debug.LogWarning($"プロパティ '{property.Name}' のコピー中にエラーが発生しました: {ex.Message}");
                    }
                }
            }
            
            // シリアライズされたプライベートフィールドへの対応（Unity特有）
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
        /// オブジェクト名として安全な名前を取得（無効な文字を除去）
        /// </summary>
        /// <param name="name">元の名前</param>
        /// <returns>安全な名前</returns>
        private string GetSafeObjectName(string name)
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

        #region Result Class
        /// <summary>
        /// 処理結果を格納するクラス
        /// </summary>
        public class ProcessingResult
        {
            /// <summary>処理が成功したかどうか</summary>
            public bool Success { get; set; } = true;
            
            /// <summary>エラーメッセージ（失敗時）</summary>
            public string ErrorMessage { get; set; } = string.Empty;
            
            /// <summary>生成されたAvatarDynamicsオブジェクト</summary>
            public GameObject AvatarDynamicsObject { get; set; }
            
            /// <summary>処理されたPhysBoneの数</summary>
            public int ProcessedPhysBoneCount { get; set; }
            
            /// <summary>処理されたPhysBoneColliderの数</summary>
            public int ProcessedColliderCount { get; set; }
            
            /// <summary>生成されたオブジェクトのリスト</summary>
            public List<GameObject> CreatedObjects { get; set; } = new List<GameObject>();
        }
        #endregion
    }
}
