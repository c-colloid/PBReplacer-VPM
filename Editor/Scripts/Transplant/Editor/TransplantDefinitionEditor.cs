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
            var addRuleButton = _root.Q<Button>("add-rule-button");

            // バインド
            _root.Bind(serializedObject);

            // ListView設定
            SetupRemapRulesListView();

            // イベント登録
            _sourceField.RegisterValueChangedCallback(_ => OnAvatarFieldChanged());
            _destField.RegisterValueChangedCallback(_ => OnAvatarFieldChanged());
            _autoScaleToggle.RegisterValueChangedCallback(evt => OnAutoScaleChanged(evt.newValue));
            addRuleButton.clicked += OnAddRuleClicked;
            _transplantButton.clicked += OnTransplantClicked;

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

        #endregion

        #region イベントハンドラ

        private void OnAvatarFieldChanged()
        {
            // SerializedObjectを更新して最新値を反映
            serializedObject.Update();
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

        #region バリデーション

        private void ValidateAndUpdateUI()
        {
            var sourceObj = _sourceAvatarProp.objectReferenceValue as GameObject;
            var destObj = _destAvatarProp.objectReferenceValue as GameObject;

            if (sourceObj == null || destObj == null)
            {
                _transplantButton.SetEnabled(false);
                _statusBox.text = "ソースアバターとデスティネーションアバターを設定してください";
                _statusBox.messageType = HelpBoxMessageType.Warning;
                _statusBox.style.display = DisplayStyle.Flex;
                return;
            }

            if (sourceObj == destObj)
            {
                _transplantButton.SetEnabled(false);
                _statusBox.text = "ソースとデスティネーションに同じアバターが設定されています";
                _statusBox.messageType = HelpBoxMessageType.Warning;
                _statusBox.style.display = DisplayStyle.Flex;
                return;
            }

            _transplantButton.SetEnabled(true);
            _statusBox.style.display = DisplayStyle.None;

            if (_autoCalculateScaleProp.boolValue)
                UpdateCalculatedScaleLabel();
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
