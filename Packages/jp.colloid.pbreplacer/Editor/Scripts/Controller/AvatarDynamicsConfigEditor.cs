using UnityEngine;
using UnityEditor;
using colloid.PBReplacer.NDMF;
using VRC.SDK3.Avatars.Components;

namespace colloid.PBReplacer
{
    /// <summary>
    /// AvatarDynamicsConfigのカスタムエディタ
    /// Inspector上で参照解決ボタンを提供する
    /// </summary>
    [CustomEditor(typeof(AvatarDynamicsConfig))]
    public class AvatarDynamicsConfigEditor : Editor
    {
        private AvatarDynamicsConfig _config;
        private VRCAvatarDescriptor _cachedAvatarDescriptor;
        private bool _showReferenceDetails = false;

        private void OnEnable()
        {
            _config = (AvatarDynamicsConfig)target;
            FindAvatarDescriptor();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ヘッダー
            EditorGUILayout.LabelField("Avatar Dynamics Config", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 基本情報
            EditorGUILayout.LabelField("エクスポート情報", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("設定名", _config.configName);
            EditorGUILayout.LabelField("元アバター", _config.sourceAvatarName);
            EditorGUILayout.LabelField("エクスポート日時", _config.exportDate);
            EditorGUILayout.LabelField("バージョン", _config.exporterVersion);
            EditorGUILayout.LabelField("コンポーネント数", _config.ComponentCount.ToString());
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // アバター検出状態
            EditorGUILayout.LabelField("参照解決", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            // アバター検出
            FindAvatarDescriptor();
            if (_cachedAvatarDescriptor != null)
            {
                EditorGUILayout.HelpBox($"アバター検出: {_cachedAvatarDescriptor.gameObject.name}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("アバターが見つかりません。\nこのオブジェクトをアバターの子に配置してください。", MessageType.Warning);
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // 参照解決ボタン
            EditorGUI.BeginDisabledGroup(_cachedAvatarDescriptor == null);

            if (GUILayout.Button("参照を解決", GUILayout.Height(30)))
            {
                ResolveReferences();
            }

            EditorGUILayout.Space();

            // 詳細設定
            EditorGUILayout.LabelField("マッピング設定", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("armatureNameOverride"),
                new GUIContent("Armature名オーバーライド", "空の場合は自動検出"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pathMatchingMode"),
                new GUIContent("パスマッチングモード"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("missingReferenceHandling"),
                new GUIContent("見つからない参照の処理"));

            EditorGUI.indentLevel--;

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();

            // 参照詳細の折りたたみ
            _showReferenceDetails = EditorGUILayout.Foldout(_showReferenceDetails, "参照詳細", true);
            if (_showReferenceDetails)
            {
                EditorGUI.indentLevel++;
                DrawReferenceDetails();
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 親階層からVRCAvatarDescriptorを検索
        /// </summary>
        private void FindAvatarDescriptor()
        {
            if (_config == null) return;

            _cachedAvatarDescriptor = _config.GetComponentInParent<VRCAvatarDescriptor>();
        }

        /// <summary>
        /// 参照を解決
        /// </summary>
        private void ResolveReferences()
        {
            if (_cachedAvatarDescriptor == null)
            {
                EditorUtility.DisplayDialog("エラー", "アバターが見つかりません", "OK");
                return;
            }

            Undo.RecordObject(_config.gameObject, "Resolve AvatarDynamics References");

            // 子オブジェクトも含めてUndo登録
            foreach (Transform child in _config.GetComponentsInChildren<Transform>(true))
            {
                var components = child.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component != null)
                    {
                        Undo.RecordObject(component, "Resolve AvatarDynamics References");
                    }
                }
            }

            try
            {
                AvatarDynamicsResolver.ResolveReferences(_config, _cachedAvatarDescriptor.gameObject);

                EditorUtility.DisplayDialog("完了",
                    $"参照を解決しました\n" +
                    $"コンポーネント数: {_config.ComponentCount}",
                    "OK");

                Debug.Log($"[PBReplacer] 参照を解決しました: {_config.ComponentCount}個のコンポーネント");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("エラー",
                    $"参照解決中にエラーが発生しました:\n{ex.Message}",
                    "OK");
                Debug.LogError($"[PBReplacer] 参照解決エラー: {ex.Message}\n{ex.StackTrace}");
            }

            // Inspector更新
            EditorUtility.SetDirty(_config);
        }

        /// <summary>
        /// 参照詳細を描画
        /// </summary>
        private void DrawReferenceDetails()
        {
            if (_config.componentReferences == null || _config.componentReferences.Count == 0)
            {
                EditorGUILayout.LabelField("参照データがありません");
                return;
            }

            // コンポーネントタイプごとにグループ化して表示
            var typeGroups = new System.Collections.Generic.Dictionary<DynamicsComponentType, int>();
            foreach (var refData in _config.componentReferences)
            {
                if (!typeGroups.ContainsKey(refData.componentType))
                {
                    typeGroups[refData.componentType] = 0;
                }
                typeGroups[refData.componentType]++;
            }

            foreach (var kvp in typeGroups)
            {
                EditorGUILayout.LabelField($"{kvp.Key}: {kvp.Value}個");
            }
        }
    }
}
