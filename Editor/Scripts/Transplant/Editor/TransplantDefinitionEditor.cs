using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace colloid.PBReplacer
{
    [CustomEditor(typeof(TransplantDefinition))]
    public class TransplantDefinitionEditor : Editor
    {
        private VisualElement _root;
        private ObjectField _sourceField;
        private ObjectField _destField;
        private ListView _rulesListView;
        private Toggle _autoScaleToggle;
        private VisualElement _manualScaleContainer;
        private FloatField _scaleFactorField;
        private Label _calculatedScaleLabel;
        private Button _transplantButton;
        private HelpBox _statusBox;
        private HelpBox _humanoidInfoBox;

        // プレビュー関連
        private Button _previewButton;
        private Foldout _previewFoldout;
        private Label _previewSummary;
        private ListView _previewBoneList;
        private Foldout _previewBoneFoldout;
        private ListView _previewWarningsList;
        private Foldout _previewWarningsFoldout;
        private TransplantPreviewData _currentPreview;

        private SerializedProperty _sourceAvatarProp;
        private SerializedProperty _destAvatarProp;
        private SerializedProperty _autoCalculateScaleProp;
        private SerializedProperty _scaleFactorProp;
        private SerializedProperty _pathRemapRulesProp;

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();

            // SerializedPropertyを取得
            _sourceAvatarProp = serializedObject.FindProperty("sourceAvatar");
            _destAvatarProp = serializedObject.FindProperty("destinationAvatar");
            _autoCalculateScaleProp = serializedObject.FindProperty("autoCalculateScale");
            _scaleFactorProp = serializedObject.FindProperty("scaleFactor");
            _pathRemapRulesProp = serializedObject.FindProperty("pathRemapRules");

            // UXMLをロード
            var visualTree = Resources.Load<VisualTreeAsset>("UXML/TransplantDefinition");
            if (visualTree == null)
            {
                _root.Add(new HelpBox("TransplantDefinition.uxml が見つかりません", HelpBoxMessageType.Error));
                return _root;
            }

            visualTree.CloneTree(_root);

            // USSをロード
            var styleSheet = Resources.Load<StyleSheet>("USS/TransplantDefinition");
            if (styleSheet != null)
                _root.styleSheets.Add(styleSheet);

            // 要素を取得
            _sourceField = _root.Q<ObjectField>("source-avatar");
            _destField = _root.Q<ObjectField>("destination-avatar");
            _rulesListView = _root.Q<ListView>("remap-rules-list");
            _autoScaleToggle = _root.Q<Toggle>("auto-scale-toggle");
            _manualScaleContainer = _root.Q<VisualElement>("manual-scale-container");
            _scaleFactorField = _root.Q<FloatField>("scale-factor-field");
            _calculatedScaleLabel = _root.Q<Label>("calculated-scale-label");
            _transplantButton = _root.Q<Button>("transplant-button");
            _statusBox = _root.Q<HelpBox>("status-box");
            _humanoidInfoBox = _root.Q<HelpBox>("humanoid-info-box");
            var addRuleButton = _root.Q<Button>("add-rule-button");

            // プレビュー要素を取得
            _previewButton = _root.Q<Button>("preview-button");
            _previewFoldout = _root.Q<Foldout>("preview-foldout");
            _previewSummary = _root.Q<Label>("preview-summary");
            _previewBoneList = _root.Q<ListView>("preview-bone-list");
            _previewBoneFoldout = _root.Q<Foldout>("preview-bone-foldout");
            _previewWarningsList = _root.Q<ListView>("preview-warnings-list");
            _previewWarningsFoldout = _root.Q<Foldout>("preview-warnings-foldout");

            // バインド
            _root.Bind(serializedObject);

            // ListView設定
            SetupRemapRulesListView();
            SetupPreviewBoneListView();
            SetupPreviewWarningsListView();

            // イベント登録
            _sourceField.RegisterValueChangedCallback(_ => OnAvatarFieldChanged());
            _destField.RegisterValueChangedCallback(_ => OnAvatarFieldChanged());
            _autoScaleToggle.RegisterValueChangedCallback(evt => OnAutoScaleChanged(evt.newValue));
            addRuleButton.clicked += OnAddRuleClicked;
            _transplantButton.clicked += OnTransplantClicked;
            _previewButton.clicked += OnPreviewClicked;

            // 初期状態を設定
            OnAutoScaleChanged(_autoCalculateScaleProp.boolValue);
            ValidateAndUpdateUI();

            return _root;
        }

        #region ListView設定

        private void SetupRemapRulesListView()
        {
            _rulesListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _rulesListView.showBoundCollectionSize = false;
            _rulesListView.reorderable = true;
            _rulesListView.bindingPath = "pathRemapRules";

            _rulesListView.makeItem = MakeRuleItem;
            _rulesListView.bindItem = BindRuleItem;
        }

        private VisualElement MakeRuleItem()
        {
            var container = new VisualElement();
            container.AddToClassList("remap-rule-item");

            var enabledToggle = new Toggle();
            enabledToggle.name = "rule-enabled";
            container.Add(enabledToggle);

            var modeField = new EnumField(PathRemapRule.RemapMode.CharacterSubstitution);
            modeField.name = "rule-mode";
            container.Add(modeField);

            var sourcePatternField = new TextField();
            sourcePatternField.name = "rule-source-pattern";
            container.Add(sourcePatternField);

            var arrowLabel = new Label("\u2192");
            arrowLabel.AddToClassList("remap-rule-arrow");
            container.Add(arrowLabel);

            var destPatternField = new TextField();
            destPatternField.name = "rule-dest-pattern";
            container.Add(destPatternField);

            var deleteButton = new Button();
            deleteButton.name = "rule-delete";
            deleteButton.text = "\u2715";
            deleteButton.AddToClassList("remap-rule-delete-button");
            container.Add(deleteButton);

            return container;
        }

        private void BindRuleItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _pathRemapRulesProp.arraySize)
                return;

            var ruleProp = _pathRemapRulesProp.GetArrayElementAtIndex(index);
            var enabledProp = ruleProp.FindPropertyRelative("enabled");
            var modeProp = ruleProp.FindPropertyRelative("mode");
            var sourcePatternProp = ruleProp.FindPropertyRelative("sourcePattern");
            var destPatternProp = ruleProp.FindPropertyRelative("destinationPattern");

            var enabledToggle = element.Q<Toggle>("rule-enabled");
            var modeField = element.Q<EnumField>("rule-mode");
            var sourcePatternField = element.Q<TextField>("rule-source-pattern");
            var destPatternField = element.Q<TextField>("rule-dest-pattern");
            var deleteButton = element.Q<Button>("rule-delete");

            // バインド
            enabledToggle.BindProperty(enabledProp);
            modeField.BindProperty(modeProp);
            sourcePatternField.BindProperty(sourcePatternProp);
            destPatternField.BindProperty(destPatternProp);

            // 削除ボタン
            deleteButton.clickable = new Clickable(() => OnDeleteRuleClicked(index));
        }

        private void SetupPreviewBoneListView()
        {
            _previewBoneList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            _previewBoneList.fixedItemHeight = 22;
            _previewBoneList.selectionType = SelectionType.None;

            _previewBoneList.makeItem = () =>
            {
                var container = new VisualElement();
                container.AddToClassList("preview-bone-item");

                var sourceLabel = new Label();
                sourceLabel.name = "bone-source";
                container.Add(sourceLabel);

                var arrow = new Label("\u2192");
                arrow.AddToClassList("preview-bone-arrow");
                container.Add(arrow);

                var destLabel = new Label();
                destLabel.name = "bone-dest";
                container.Add(destLabel);

                return container;
            };

            _previewBoneList.bindItem = (element, index) =>
            {
                if (_currentPreview == null || index >= _currentPreview.BoneMappings.Count)
                    return;

                var mapping = _currentPreview.BoneMappings[index];
                var sourceLabel = element.Q<Label>("bone-source");
                var destLabel = element.Q<Label>("bone-dest");

                sourceLabel.text = mapping.sourceBonePath;

                if (mapping.resolved)
                {
                    destLabel.text = mapping.destinationBonePath;
                    element.RemoveFromClassList("preview-bone-unresolved");
                    element.AddToClassList("preview-bone-resolved");
                }
                else
                {
                    destLabel.text = mapping.errorMessage ?? "未解決";
                    element.RemoveFromClassList("preview-bone-resolved");
                    element.AddToClassList("preview-bone-unresolved");
                }
            };
        }

        private void SetupPreviewWarningsListView()
        {
            _previewWarningsList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            _previewWarningsList.fixedItemHeight = 22;
            _previewWarningsList.selectionType = SelectionType.None;

            _previewWarningsList.makeItem = () =>
            {
                var container = new VisualElement();
                container.AddToClassList("preview-warning-item");

                var label = new Label();
                label.name = "warning-text";
                container.Add(label);

                return container;
            };

            _previewWarningsList.bindItem = (element, index) =>
            {
                if (_currentPreview == null || index >= _currentPreview.Warnings.Count)
                    return;

                var label = element.Q<Label>("warning-text");
                label.text = _currentPreview.Warnings[index];
            };
        }

        #endregion

        #region イベントハンドラ

        private void OnAvatarFieldChanged()
        {
            // SerializedObjectを更新して最新値を反映
            serializedObject.Update();
            ClearPreview();
            ValidateAndUpdateUI();
        }

        private void OnAutoScaleChanged(bool autoScale)
        {
            _manualScaleContainer.style.display = autoScale
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            _calculatedScaleLabel.style.display = autoScale
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            if (autoScale)
                UpdateCalculatedScaleLabel();
        }

        private void OnAddRuleClicked()
        {
            serializedObject.Update();
            int newIndex = _pathRemapRulesProp.arraySize;
            _pathRemapRulesProp.InsertArrayElementAtIndex(newIndex);

            var newRule = _pathRemapRulesProp.GetArrayElementAtIndex(newIndex);
            newRule.FindPropertyRelative("enabled").boolValue = true;
            newRule.FindPropertyRelative("mode").enumValueIndex =
                (int)PathRemapRule.RemapMode.CharacterSubstitution;
            newRule.FindPropertyRelative("sourcePattern").stringValue = "";
            newRule.FindPropertyRelative("destinationPattern").stringValue = "";

            serializedObject.ApplyModifiedProperties();
            _rulesListView.Rebuild();
        }

        private void OnDeleteRuleClicked(int index)
        {
            serializedObject.Update();
            if (index >= 0 && index < _pathRemapRulesProp.arraySize)
            {
                _pathRemapRulesProp.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                _rulesListView.Rebuild();
            }
        }

        private void OnPreviewClicked()
        {
            serializedObject.Update();
            var definition = (TransplantDefinition)target;

            _currentPreview = TransplantPreview.GeneratePreview(definition);
            DisplayPreview();
        }

        private void OnTransplantClicked()
        {
            serializedObject.Update();
            var definition = (TransplantDefinition)target;

            // 確認ダイアログ
            var settings = PBReplacerSettings.Load();
            if (settings.ShowConfirmDialog)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "コンポーネント移植",
                    $"ソース: {definition.SourceAvatar.name}\n" +
                    $"デスティネーション: {definition.DestinationAvatar.name}\n\n" +
                    "移植を実行しますか？",
                    "実行", "キャンセル");

                if (!confirmed)
                    return;
            }

            // 移植実行
            var result = TransplantProcessor.TransplantComponents(definition);

            result.Match(
                onSuccess: success =>
                {
                    string message =
                        $"移植が完了しました\n\n" +
                        $"PhysBone: {success.PhysBoneCount}\n" +
                        $"PhysBoneCollider: {success.PhysBoneColliderCount}\n" +
                        $"Constraint: {success.ConstraintCount}\n" +
                        $"Contact: {success.ContactCount}\n" +
                        $"合計: {success.TotalCount}";

                    if (success.Warnings.Count > 0)
                        message += $"\n\n警告 ({success.Warnings.Count}):\n" +
                                   string.Join("\n", success.Warnings);

                    EditorUtility.DisplayDialog("移植完了", message, "OK");

                    _statusBox.text = $"移植完了: {success.TotalCount} コンポーネント";
                    _statusBox.messageType = HelpBoxMessageType.Info;
                    _statusBox.style.display = DisplayStyle.Flex;
                },
                onFailure: error =>
                {
                    _statusBox.text = error;
                    _statusBox.messageType = HelpBoxMessageType.Error;
                    _statusBox.style.display = DisplayStyle.Flex;
                });
        }

        #endregion

        #region プレビュー

        private void DisplayPreview()
        {
            if (_currentPreview == null)
            {
                ClearPreview();
                return;
            }

            // サマリー更新
            int totalComponents = _currentPreview.TotalPhysBones
                + _currentPreview.TotalPhysBoneColliders
                + _currentPreview.TotalConstraints
                + _currentPreview.TotalContacts;
            int totalBones = _currentPreview.ResolvedBones + _currentPreview.UnresolvedBones;

            _previewSummary.text =
                $"PB:{_currentPreview.TotalPhysBones} " +
                $"PBC:{_currentPreview.TotalPhysBoneColliders} " +
                $"Constraint:{_currentPreview.TotalConstraints} " +
                $"Contact:{_currentPreview.TotalContacts}" +
                $" | ボーン解決: {_currentPreview.ResolvedBones}/{totalBones}" +
                $" | スケール: {_currentPreview.CalculatedScaleFactor:F3}";

            // ボーンマッピングリスト更新
            _previewBoneList.itemsSource = _currentPreview.BoneMappings;
            _previewBoneList.style.height = Mathf.Min(
                _currentPreview.BoneMappings.Count * 22, 200);
            _previewBoneList.Rebuild();

            // 警告リスト更新
            if (_currentPreview.Warnings.Count > 0)
            {
                _previewWarningsList.itemsSource = _currentPreview.Warnings;
                _previewWarningsList.style.height = Mathf.Min(
                    _currentPreview.Warnings.Count * 22, 110);
                _previewWarningsList.Rebuild();
                _previewWarningsFoldout.style.display = DisplayStyle.Flex;
                _previewWarningsFoldout.value = true;
            }
            else
            {
                _previewWarningsFoldout.style.display = DisplayStyle.None;
            }

            // Foldoutを表示して開く
            _previewFoldout.style.display = DisplayStyle.Flex;
            _previewFoldout.value = true;
            _previewBoneFoldout.value = true;
        }

        private void ClearPreview()
        {
            _currentPreview = null;
            _previewFoldout.style.display = DisplayStyle.None;
            _previewFoldout.value = false;
        }

        #endregion

        #region バリデーション

        private void ValidateAndUpdateUI()
        {
            var sourceObj = _sourceAvatarProp.objectReferenceValue as GameObject;
            var destObj = _destAvatarProp.objectReferenceValue as GameObject;

            if (sourceObj == null || destObj == null)
            {
                _transplantButton.SetEnabled(false);
                _previewButton.SetEnabled(false);
                _statusBox.text = "ソースアバターとデスティネーションアバターを設定してください";
                _statusBox.messageType = HelpBoxMessageType.Warning;
                _statusBox.style.display = DisplayStyle.Flex;
                return;
            }

            if (sourceObj == destObj)
            {
                _transplantButton.SetEnabled(false);
                _previewButton.SetEnabled(false);
                _statusBox.text = "ソースとデスティネーションに同じアバターが設定されています";
                _statusBox.messageType = HelpBoxMessageType.Warning;
                _statusBox.style.display = DisplayStyle.Flex;
                return;
            }

            _transplantButton.SetEnabled(true);
            _previewButton.SetEnabled(true);
            _statusBox.style.display = DisplayStyle.None;

            // 非Humanoidアバターの情報表示
            UpdateHumanoidInfoBox(sourceObj, destObj);

            if (_autoCalculateScaleProp.boolValue)
                UpdateCalculatedScaleLabel();
        }

        private void UpdateHumanoidInfoBox(GameObject sourceObj, GameObject destObj)
        {
            if (_humanoidInfoBox == null)
                return;

            var sourceAnimator = sourceObj != null ? sourceObj.GetComponent<Animator>() : null;
            var destAnimator = destObj != null ? destObj.GetComponent<Animator>() : null;

            bool sourceIsHumanoid = sourceAnimator != null && sourceAnimator.isHuman;
            bool destIsHumanoid = destAnimator != null && destAnimator.isHuman;

            if (sourceObj != null && destObj != null && (!sourceIsHumanoid || !destIsHumanoid))
            {
                var nonHumanoidNames = new System.Collections.Generic.List<string>();
                if (!sourceIsHumanoid) nonHumanoidNames.Add($"ソース ({sourceObj.name})");
                if (!destIsHumanoid) nonHumanoidNames.Add($"デスティネーション ({destObj.name})");

                _humanoidInfoBox.text =
                    $"{string.Join("、", nonHumanoidNames)} は非Humanoidです。" +
                    "Humanoidボーンマッピングが使用できないため、パス名/ボーン名での解決になります。" +
                    "必要に応じてパスリマップルールを追加してください。";
                _humanoidInfoBox.messageType = HelpBoxMessageType.Info;
                _humanoidInfoBox.style.display = DisplayStyle.Flex;
            }
            else
            {
                _humanoidInfoBox.style.display = DisplayStyle.None;
            }
        }

        private void UpdateCalculatedScaleLabel()
        {
            var sourceObj = _sourceAvatarProp.objectReferenceValue as GameObject;
            var destObj = _destAvatarProp.objectReferenceValue as GameObject;

            if (sourceObj == null || destObj == null)
            {
                _calculatedScaleLabel.text = "";
                return;
            }

            try
            {
                var sourceData = new AvatarData(sourceObj);
                var destData = new AvatarData(destObj);

                float scale = ScaleCalculator.CalculateScaleFactor(
                    sourceData.Armature.transform,
                    destData.Armature.transform,
                    sourceData.AvatarAnimator,
                    destData.AvatarAnimator);

                _calculatedScaleLabel.text = $"算出値: {scale:F4}";
            }
            catch
            {
                _calculatedScaleLabel.text = "算出不可";
            }
        }

        #endregion
    }
}
