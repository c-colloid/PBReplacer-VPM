using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using colloid.PBReplacer.NDMF;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace colloid.PBReplacer
{
    /// <summary>
    /// AvatarDynamicsConfigのカスタムエディタ
    /// Inspector上で参照解決ボタンと参照状態の詳細表示を提供する
    /// </summary>
    [CustomEditor(typeof(AvatarDynamicsConfig))]
    public class AvatarDynamicsConfigEditor : Editor
    {
        private AvatarDynamicsConfig _config;
        private VRCAvatarDescriptor _cachedAvatarDescriptor;
        private bool _showReferenceDetails = true;
        private bool _showMissingOnly = true;
        private Vector2 _scrollPosition;

        // 参照状態のキャッシュ
        private List<ReferenceStatus> _referenceStatuses = new List<ReferenceStatus>();
        private int _missingCount = 0;
        private int _resolvedCount = 0;

        /// <summary>
        /// 参照状態を表すクラス
        /// </summary>
        private class ReferenceStatus
        {
            public string ComponentName { get; set; }
            public DynamicsComponentType ComponentType { get; set; }
            public Component TargetComponent { get; set; }
            public List<MissingReference> MissingReferences { get; set; } = new List<MissingReference>();
            public bool HasMissingReferences => MissingReferences.Count > 0;
        }

        /// <summary>
        /// 見つからない参照の情報
        /// </summary>
        private class MissingReference
        {
            public string ReferenceType { get; set; }
            public string ExpectedPath { get; set; }
        }

        private void OnEnable()
        {
            _config = (AvatarDynamicsConfig)target;
            FindAvatarDescriptor();
            UpdateReferenceStatuses();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // ヘッダー
            EditorGUILayout.LabelField("Avatar Dynamics Config", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 基本情報
            DrawExportInfo();

            EditorGUILayout.Space();

            // アバター検出状態
            DrawAvatarDetection();

            EditorGUILayout.Space();

            // 参照解決ボタン
            DrawResolveButton();

            EditorGUILayout.Space();

            // マッピング設定
            DrawMappingSettings();

            EditorGUILayout.Space();

            // 参照状態サマリー
            DrawReferenceStatusSummary();

            EditorGUILayout.Space();

            // 参照詳細の折りたたみ
            DrawReferenceDetails();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// エクスポート情報を描画
        /// </summary>
        private void DrawExportInfo()
        {
            EditorGUILayout.LabelField("エクスポート情報", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("設定名", _config.configName);
            EditorGUILayout.LabelField("元アバター", _config.sourceAvatarName);
            EditorGUILayout.LabelField("エクスポート日時", _config.exportDate);
            EditorGUILayout.LabelField("バージョン", _config.exporterVersion);
            EditorGUILayout.LabelField("コンポーネント数", _config.ComponentCount.ToString());
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// アバター検出状態を描画
        /// </summary>
        private void DrawAvatarDetection()
        {
            EditorGUILayout.LabelField("参照解決", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

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
        }

        /// <summary>
        /// 参照解決ボタンを描画
        /// </summary>
        private void DrawResolveButton()
        {
            EditorGUI.BeginDisabledGroup(_cachedAvatarDescriptor == null);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("参照を解決", GUILayout.Height(30)))
            {
                ResolveReferences();
            }
            if (GUILayout.Button("状態を更新", GUILayout.Width(80), GUILayout.Height(30)))
            {
                UpdateReferenceStatuses();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// マッピング設定を描画
        /// </summary>
        private void DrawMappingSettings()
        {
            EditorGUI.BeginDisabledGroup(_cachedAvatarDescriptor == null);

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
        }

        /// <summary>
        /// 参照状態サマリーを描画
        /// </summary>
        private void DrawReferenceStatusSummary()
        {
            EditorGUILayout.LabelField("参照状態", EditorStyles.boldLabel);

            var totalComponents = _referenceStatuses.Count;
            var okComponents = _referenceStatuses.Count(s => !s.HasMissingReferences);
            var problemComponents = _referenceStatuses.Count(s => s.HasMissingReferences);

            EditorGUILayout.BeginHorizontal();

            // 解決済み
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = Color.green;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(100));
            EditorGUILayout.LabelField("解決済み", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField($"{okComponents}", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
            EditorGUILayout.EndVertical();

            // 問題あり
            GUI.backgroundColor = problemComponents > 0 ? Color.red : Color.green;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(100));
            EditorGUILayout.LabelField("問題あり", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField($"{problemComponents}", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
            EditorGUILayout.EndVertical();

            // 合計
            GUI.backgroundColor = originalColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(100));
            EditorGUILayout.LabelField("合計", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField($"{totalComponents}", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            if (problemComponents > 0)
            {
                EditorGUILayout.HelpBox($"{problemComponents}個のコンポーネントで参照が見つかりませんでした。\n詳細を確認し、「参照を解決」を実行してください。", MessageType.Warning);
            }
        }

        /// <summary>
        /// 参照詳細を描画
        /// </summary>
        private void DrawReferenceDetails()
        {
            EditorGUILayout.BeginHorizontal();
            _showReferenceDetails = EditorGUILayout.Foldout(_showReferenceDetails, "参照詳細", true);

            if (_showReferenceDetails)
            {
                _showMissingOnly = GUILayout.Toggle(_showMissingOnly, "問題のみ表示", GUILayout.Width(100));
            }
            EditorGUILayout.EndHorizontal();

            if (!_showReferenceDetails) return;

            if (_referenceStatuses == null || _referenceStatuses.Count == 0)
            {
                EditorGUILayout.LabelField("参照データがありません");
                return;
            }

            EditorGUI.indentLevel++;

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MaxHeight(300));

            foreach (var status in _referenceStatuses)
            {
                if (_showMissingOnly && !status.HasMissingReferences)
                    continue;

                DrawComponentStatus(status);
            }

            EditorGUILayout.EndScrollView();

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 個別コンポーネントの状態を描画
        /// </summary>
        private void DrawComponentStatus(ReferenceStatus status)
        {
            var bgColor = GUI.backgroundColor;
            GUI.backgroundColor = status.HasMissingReferences ? new Color(1f, 0.8f, 0.8f) : new Color(0.8f, 1f, 0.8f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = bgColor;

            EditorGUILayout.BeginHorizontal();

            // アイコン
            var icon = status.HasMissingReferences ? "console.warnicon.sml" : "Collab";
            var iconContent = EditorGUIUtility.IconContent(icon);
            GUILayout.Label(iconContent, GUILayout.Width(20), GUILayout.Height(20));

            // コンポーネント名とタイプ
            EditorGUILayout.LabelField($"{status.ComponentName}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"[{status.ComponentType}]", GUILayout.Width(120));

            // ジャンプボタン
            if (status.TargetComponent != null)
            {
                if (GUILayout.Button("選択", GUILayout.Width(50)))
                {
                    Selection.activeGameObject = status.TargetComponent.gameObject;
                    EditorGUIUtility.PingObject(status.TargetComponent.gameObject);
                }
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Button("N/A", GUILayout.Width(50));
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndHorizontal();

            // 見つからない参照の詳細
            if (status.HasMissingReferences)
            {
                EditorGUI.indentLevel++;
                foreach (var missing in status.MissingReferences)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"• {missing.ReferenceType}:", GUILayout.Width(120));
                    EditorGUILayout.SelectableLabel(missing.ExpectedPath, EditorStyles.miniLabel, GUILayout.Height(16));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
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
                UpdateReferenceStatuses();

                var problemCount = _referenceStatuses.Count(s => s.HasMissingReferences);
                if (problemCount > 0)
                {
                    EditorUtility.DisplayDialog("完了",
                        $"参照を解決しました\n" +
                        $"コンポーネント数: {_config.ComponentCount}\n" +
                        $"解決できなかった参照: {problemCount}個\n\n" +
                        $"詳細は「参照詳細」セクションを確認してください。",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("完了",
                        $"参照を解決しました\n" +
                        $"コンポーネント数: {_config.ComponentCount}\n" +
                        $"すべての参照が正常に解決されました。",
                        "OK");
                }

                Debug.Log($"[PBReplacer] 参照を解決しました: {_config.ComponentCount}個のコンポーネント");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("エラー",
                    $"参照解決中にエラーが発生しました:\n{ex.Message}",
                    "OK");
                Debug.LogError($"[PBReplacer] 参照解決エラー: {ex.Message}\n{ex.StackTrace}");
            }

            EditorUtility.SetDirty(_config);
        }

        /// <summary>
        /// 参照状態を更新
        /// </summary>
        private void UpdateReferenceStatuses()
        {
            _referenceStatuses.Clear();
            _missingCount = 0;
            _resolvedCount = 0;

            if (_config == null || _config.componentReferences == null)
                return;

            var dynamicsRoot = _config.gameObject;

            foreach (var refData in _config.componentReferences)
            {
                var status = new ReferenceStatus
                {
                    ComponentName = refData.gameObjectName,
                    ComponentType = refData.componentType
                };

                // コンポーネントを検索
                status.TargetComponent = FindComponent(dynamicsRoot, refData);

                if (status.TargetComponent != null)
                {
                    // 参照の状態をチェック
                    CheckComponentReferences(status, refData);
                }
                else
                {
                    status.MissingReferences.Add(new MissingReference
                    {
                        ReferenceType = "コンポーネント",
                        ExpectedPath = refData.gameObjectName
                    });
                }

                _referenceStatuses.Add(status);

                if (status.HasMissingReferences)
                    _missingCount++;
                else
                    _resolvedCount++;
            }

            Repaint();
        }

        /// <summary>
        /// コンポーネントを検索
        /// </summary>
        private Component FindComponent(GameObject root, ComponentReferenceData refData)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            var targetTransform = transforms.FirstOrDefault(t => t.name == refData.gameObjectName);

            if (targetTransform == null) return null;

            switch (refData.componentType)
            {
                case DynamicsComponentType.PhysBone:
                    return targetTransform.GetComponent<VRCPhysBone>();
                case DynamicsComponentType.PhysBoneCollider:
                    return targetTransform.GetComponent<VRCPhysBoneCollider>();
                case DynamicsComponentType.ContactSender:
                    return targetTransform.GetComponent<VRCContactSender>();
                case DynamicsComponentType.ContactReceiver:
                    return targetTransform.GetComponent<VRCContactReceiver>();
                case DynamicsComponentType.PositionConstraint:
                    return targetTransform.GetComponent<VRCPositionConstraint>();
                case DynamicsComponentType.RotationConstraint:
                    return targetTransform.GetComponent<VRCRotationConstraint>();
                case DynamicsComponentType.ScaleConstraint:
                    return targetTransform.GetComponent<VRCScaleConstraint>();
                case DynamicsComponentType.ParentConstraint:
                    return targetTransform.GetComponent<VRCParentConstraint>();
                case DynamicsComponentType.LookAtConstraint:
                    return targetTransform.GetComponent<VRCLookAtConstraint>();
                case DynamicsComponentType.AimConstraint:
                    return targetTransform.GetComponent<VRCAimConstraint>();
                default:
                    return null;
            }
        }

        /// <summary>
        /// コンポーネントの参照状態をチェック
        /// </summary>
        private void CheckComponentReferences(ReferenceStatus status, ComponentReferenceData refData)
        {
            switch (refData.componentType)
            {
                case DynamicsComponentType.PhysBone:
                    CheckPhysBoneReferences(status, refData);
                    break;
                case DynamicsComponentType.PhysBoneCollider:
                    CheckPhysBoneColliderReferences(status, refData);
                    break;
                case DynamicsComponentType.ContactSender:
                case DynamicsComponentType.ContactReceiver:
                    CheckContactReferences(status, refData);
                    break;
                default:
                    CheckConstraintReferences(status, refData);
                    break;
            }
        }

        /// <summary>
        /// PhysBoneの参照をチェック
        /// </summary>
        private void CheckPhysBoneReferences(ReferenceStatus status, ComponentReferenceData refData)
        {
            var pb = status.TargetComponent as VRCPhysBone;
            if (pb == null) return;

            // RootTransform
            if (refData.rootTransformPath != null && refData.rootTransformPath.IsValid)
            {
                if (pb.rootTransform == null)
                {
                    status.MissingReferences.Add(new MissingReference
                    {
                        ReferenceType = "RootTransform",
                        ExpectedPath = refData.rootTransformPath.relativePath ?? refData.rootTransformPath.absolutePath
                    });
                }
            }

            // Colliders
            if (refData.colliderReferences != null && refData.colliderReferences.Count > 0)
            {
                var expectedCount = refData.colliderReferences.Count;
                var actualCount = pb.colliders?.Count(c => c != null) ?? 0;

                if (actualCount < expectedCount)
                {
                    status.MissingReferences.Add(new MissingReference
                    {
                        ReferenceType = "Colliders",
                        ExpectedPath = $"期待: {expectedCount}個, 実際: {actualCount}個"
                    });
                }
            }
        }

        /// <summary>
        /// PhysBoneColliderの参照をチェック
        /// </summary>
        private void CheckPhysBoneColliderReferences(ReferenceStatus status, ComponentReferenceData refData)
        {
            var pbc = status.TargetComponent as VRCPhysBoneCollider;
            if (pbc == null) return;

            // RootTransform
            if (refData.rootTransformPath != null && refData.rootTransformPath.IsValid)
            {
                if (pbc.rootTransform == null)
                {
                    status.MissingReferences.Add(new MissingReference
                    {
                        ReferenceType = "RootTransform",
                        ExpectedPath = refData.rootTransformPath.relativePath ?? refData.rootTransformPath.absolutePath
                    });
                }
            }
        }

        /// <summary>
        /// Contact系の参照をチェック
        /// </summary>
        private void CheckContactReferences(ReferenceStatus status, ComponentReferenceData refData)
        {
            VRC.Dynamics.ContactBase contact = null;

            if (refData.componentType == DynamicsComponentType.ContactSender)
                contact = status.TargetComponent as VRCContactSender;
            else
                contact = status.TargetComponent as VRCContactReceiver;

            if (contact == null) return;

            // RootTransform
            if (refData.rootTransformPath != null && refData.rootTransformPath.IsValid)
            {
                if (contact.rootTransform == null)
                {
                    status.MissingReferences.Add(new MissingReference
                    {
                        ReferenceType = "RootTransform",
                        ExpectedPath = refData.rootTransformPath.relativePath ?? refData.rootTransformPath.absolutePath
                    });
                }
            }
        }

        /// <summary>
        /// Constraintの参照をチェック
        /// </summary>
        private void CheckConstraintReferences(ReferenceStatus status, ComponentReferenceData refData)
        {
            var constraint = status.TargetComponent as VRC.Dynamics.VRCConstraintBase;
            if (constraint == null) return;

            // TargetTransform
            if (refData.targetTransformPath != null && refData.targetTransformPath.IsValid)
            {
                if (constraint.TargetTransform == null)
                {
                    status.MissingReferences.Add(new MissingReference
                    {
                        ReferenceType = "TargetTransform",
                        ExpectedPath = refData.targetTransformPath.relativePath ?? refData.targetTransformPath.absolutePath
                    });
                }
            }

            // ソースTransform
            if (refData.sourceTransformPaths != null && refData.sourceTransformPaths.Count > 0)
            {
                var sources = constraint.Sources;
                var expectedCount = refData.sourceTransformPaths.Count;
                var actualCount = 0;

                if (sources != null)
                {
                    for (int i = 0; i < sources.Count; i++)
                    {
                        if (sources[i].SourceTransform != null)
                            actualCount++;
                    }
                }

                if (actualCount < expectedCount)
                {
                    status.MissingReferences.Add(new MissingReference
                    {
                        ReferenceType = "Sources",
                        ExpectedPath = $"期待: {expectedCount}個, 実際: {actualCount}個"
                    });
                }
            }
        }
    }
}
