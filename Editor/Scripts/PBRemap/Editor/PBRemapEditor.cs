using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
    [CustomEditor(typeof(PBRemap))]
    public class PBRemapEditor : Editor
    {
        private VisualElement _root;

        // 検出状態
        private Label _destAvatarLabel;
        private Label _sourceAvatarLabel;
        private Label _modeLabel;
        private HelpBox _stateBox;
        private HelpBox _detectionWarningBox;
        private HelpBox _humanoidInfoBox;

        // コンポーネント一覧
        private Label _componentsSummary;
        private Label _resolutionSummary;

        // リマップルール
        private ListView _rulesListView;
        private bool _showRuleHints;

        // スケール
        private Toggle _autoScaleToggle;
        private VisualElement _manualScaleContainer;
        private FloatField _scaleFactorField;
        private Label _calculatedScaleLabel;

        // アクション
        private Button _previewButton;
        private Button _remapButton;
        private HelpBox _statusBox;

        // SerializedProperties
        private SerializedProperty _autoCalculateScaleProp;
        private SerializedProperty _scaleFactorProp;
        private SerializedProperty _pathRemapRulesProp;
        private SerializedProperty _serializedBoneRefsProp;
        private SerializedProperty _sourceAvatarScaleProp;

        // 検出結果キャッシュ
        private SourceDetector.DetectionResult _detection;

        // 階層変更検知用
        private Transform _cachedParent;
        private int _cachedChildCount;

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();

            // SerializedPropertyを取得
            _autoCalculateScaleProp = serializedObject.FindProperty("autoCalculateScale");
            _scaleFactorProp = serializedObject.FindProperty("scaleFactor");
            _pathRemapRulesProp = serializedObject.FindProperty("pathRemapRules");
            _serializedBoneRefsProp = serializedObject.FindProperty("serializedBoneReferences");
            _sourceAvatarScaleProp = serializedObject.FindProperty("sourceAvatarScale");

            // UXMLをロード
            var visualTree = Resources.Load<VisualTreeAsset>("UXML/PBRemap");
            if (visualTree == null)
            {
                _root.Add(new HelpBox("PBRemap.uxml が見つかりません", HelpBoxMessageType.Error));
                return _root;
            }

            visualTree.CloneTree(_root);

            // USSをロード
            var styleSheet = Resources.Load<StyleSheet>("USS/PBRemap");
            if (styleSheet != null)
                _root.styleSheets.Add(styleSheet);

            // 要素を取得
            _destAvatarLabel = _root.Q<Label>("dest-avatar-label");
            _sourceAvatarLabel = _root.Q<Label>("source-avatar-label");
            _modeLabel = _root.Q<Label>("mode-label");
            _stateBox = _root.Q<HelpBox>("pbremap-state-box");
            _detectionWarningBox = _root.Q<HelpBox>("detection-warning-box");
            _humanoidInfoBox = _root.Q<HelpBox>("humanoid-info-box");
            _componentsSummary = _root.Q<Label>("components-summary");
            _resolutionSummary = _root.Q<Label>("resolution-summary");
            _rulesListView = _root.Q<ListView>("remap-rules-list");
            _autoScaleToggle = _root.Q<Toggle>("auto-scale-toggle");
            _manualScaleContainer = _root.Q<VisualElement>("manual-scale-container");
            _scaleFactorField = _root.Q<FloatField>("scale-factor-field");
            _calculatedScaleLabel = _root.Q<Label>("calculated-scale-label");
            _previewButton = _root.Q<Button>("preview-button");
            _remapButton = _root.Q<Button>("pbremap-button");
            _statusBox = _root.Q<HelpBox>("status-box");
            var addRuleButton = _root.Q<Button>("add-rule-button");

            // バインド
            _root.Bind(serializedObject);

            // ListView設定
            SetupRemapRulesListView();

            // イベント登録
            _autoScaleToggle.RegisterValueChangedCallback(evt => OnAutoScaleChanged(evt.newValue));
            addRuleButton.clicked += OnAddRuleClicked;
            _remapButton.clicked += OnRemapClicked;
            _previewButton.clicked += OnPreviewClicked;

            // ルールヘルプ表示切替
            var helpFoldout = _root.Q<Foldout>("remap-rules-help");
            if (helpFoldout != null)
            {
                helpFoldout.RegisterValueChangedCallback(evt =>
                {
                    _showRuleHints = evt.newValue;
                    _rulesListView.Rebuild();
                });
            }

            // 初期状態を設定
            OnAutoScaleChanged(_autoCalculateScaleProp.boolValue);
            RefreshDetection();
            UpdateSerializedBoneReferences();

            // リマップルール変更時にボーン解決を再評価
            _root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                if (_detection == null)
                    return;
                var def = (PBRemap)target;
                UpdateResolutionSummary(def);

                var previewWindow = FindPreviewWindow();
                if (previewWindow != null)
                    previewWindow.RefreshPreview();
            });

            // 階層変更時の自動更新を登録
            var definition = (PBRemap)target;
            _cachedParent = definition.transform.parent;
            _cachedChildCount = definition.transform.childCount;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            return _root;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        /// <summary>
        /// 階層変更を検知してインスペクター表示を更新する。
        /// </summary>
        private void OnHierarchyChanged()
        {
            if (target == null)
                return;

            var definition = (PBRemap)target;
            var currentParent = definition.transform.parent;
            var currentChildCount = definition.transform.childCount;

            if (currentParent == _cachedParent && currentChildCount == _cachedChildCount)
                return;

            _cachedParent = currentParent;
            _cachedChildCount = currentChildCount;

            RefreshDetection();
            UpdateSerializedBoneReferences();
        }

        #region 検出と更新

        private void RefreshDetection()
        {
            var definition = (PBRemap)target;
            var detectResult = SourceDetector.Detect(definition);

            if (detectResult.IsFailure)
            {
                _destAvatarLabel.text = "移植先: (検出エラー)";
                _sourceAvatarLabel.text = "移植元: (検出エラー)";
                _remapButton.SetEnabled(false);
                _previewButton.SetEnabled(false);
                return;
            }

            _detection = detectResult.Value;

            // デスティネーション表示
            if (_detection.DestinationAvatar != null)
                _destAvatarLabel.text = $"移植先: {_detection.DestinationAvatar.name}";
            else
                _destAvatarLabel.text = "移植先: (未検出)";

            // ソース・モード表示
            if (_detection.IsReferencingDestination)
            {
                _sourceAvatarLabel.text = "移植元: (移植済み)";
                _modeLabel.text = "";
            }
            else if (_detection.IsLiveMode && _detection.SourceAvatar != null)
            {
                _sourceAvatarLabel.text = $"移植元: {_detection.SourceAvatar.name}";
                _modeLabel.text = "同一シーンモード";
            }
            else if (!_detection.IsLiveMode && definition.SerializedBoneReferences.Count > 0)
            {
                _sourceAvatarLabel.text = "移植元: (Prefabデータから復元)";
                _modeLabel.text = "Prefabモード";
            }
            else
            {
                _sourceAvatarLabel.text = "移植元: (未検出)";
                _modeLabel.text = "";
            }

            UpdateStateBox(definition);

            if (_detection.Warnings.Count > 0)
            {
                _detectionWarningBox.text = string.Join("\n", _detection.Warnings);
                _detectionWarningBox.messageType = HelpBoxMessageType.Warning;
                _detectionWarningBox.style.display = DisplayStyle.Flex;
            }
            else
            {
                _detectionWarningBox.style.display = DisplayStyle.None;
            }

            UpdateComponentsSummary(definition);
            UpdateHumanoidInfoBox();

            bool canOperate = !_detection.IsReferencingDestination
                && _detection.DestinationAvatar != null
                && (_detection.IsLiveMode && _detection.SourceAvatar != null
                    || !_detection.IsLiveMode && definition.SerializedBoneReferences.Count > 0);
            _remapButton.SetEnabled(canOperate);
            _previewButton.SetEnabled(canOperate || _detection.IsReferencingDestination);

            UpdateResolutionSummary(definition);

            // プレビューウィンドウが開いていれば検出結果を更新（フォーカスを奪わない）
            var previewWindow = FindPreviewWindow();
            if (previewWindow != null)
                previewWindow.UpdateDetection(_detection);

            if (_autoCalculateScaleProp.boolValue)
                UpdateCalculatedScaleLabel();

            _statusBox.style.display = DisplayStyle.None;
        }

        private void UpdateStateBox(PBRemap definition)
        {
            if (_detection.DestinationAvatar == null)
            {
                _stateBox.text = "このコンポーネントをアバターの子階層に配置してください。";
                _stateBox.messageType = HelpBoxMessageType.Info;
                _stateBox.style.display = DisplayStyle.Flex;
            }
            else if (_detection.IsReferencingDestination)
            {
                _stateBox.text =
                    "移植済み — ボーン参照は移植先アバターに接続されています。";
                _stateBox.messageType = HelpBoxMessageType.Info;
                _stateBox.style.display = DisplayStyle.Flex;
            }
            else if (_detection.IsLiveMode && _detection.SourceAvatar != null
                || !_detection.IsLiveMode && definition.SerializedBoneReferences.Count > 0)
            {
                _stateBox.style.display = DisplayStyle.None;
            }
            else
            {
                _stateBox.text =
                    "移植対象のコンポーネントをこの階層の子に配置してください。";
                _stateBox.messageType = HelpBoxMessageType.Info;
                _stateBox.style.display = DisplayStyle.Flex;
            }
        }

        private void UpdateResolutionSummary(PBRemap definition)
        {
            bool canCheck = _detection.DestinationAvatar != null
                && _detection.DestAvatarData != null
                && !_detection.IsReferencingDestination
                && (_detection.IsLiveMode && _detection.SourceAvatar != null
                    || !_detection.IsLiveMode && definition.SerializedBoneReferences.Count > 0);

            if (!canCheck)
            {
                _resolutionSummary.style.display = DisplayStyle.None;
                Highlighter.Stop();
                return;
            }

            var preview = PBRemapPreview.GeneratePreview(definition, _detection);
            int total = preview.ResolvedBones + preview.UnresolvedBones;

            if (total == 0)
            {
                _resolutionSummary.style.display = DisplayStyle.None;
                Highlighter.Stop();
                return;
            }

            _resolutionSummary.style.display = DisplayStyle.Flex;

            if (preview.UnresolvedBones == 0)
            {
                _resolutionSummary.text = $"ボーン解決: {preview.ResolvedBones}/{total} 全て解決済み";
                _resolutionSummary.RemoveFromClassList("pbremap-resolution-unresolved");
                _resolutionSummary.AddToClassList("pbremap-resolution-resolved");
                Highlighter.Stop();
            }
            else
            {
                _resolutionSummary.text =
                    $"ボーン解決: {preview.ResolvedBones}/{total} ({preview.UnresolvedBones} 未解決)";
                _resolutionSummary.RemoveFromClassList("pbremap-resolution-resolved");
                _resolutionSummary.AddToClassList("pbremap-resolution-unresolved");
                Highlighter.Highlight("Inspector", "プレビュー", HighlightSearchMode.Auto);
            }
        }

        private void UpdateComponentsSummary(PBRemap definition)
        {
            var root = definition.transform;
            int pb = root.GetComponentsInChildren<VRCPhysBone>(true).Length;
            int pbc = root.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
            int constraint = root.GetComponentsInChildren<VRCConstraintBase>(true).Length;
            int contact = root.GetComponentsInChildren<ContactBase>(true).Length;
            int total = pb + pbc + constraint + contact;

            _componentsSummary.text =
                $"PhysBone: {pb}  PhysBoneCollider: {pbc}  " +
                $"Constraint: {constraint}  Contact: {contact}  " +
                $"(合計: {total})";
        }

        private void UpdateHumanoidInfoBox()
        {
            if (_humanoidInfoBox == null || _detection == null)
                return;

            var nonHumanoidNames = new List<string>();

            if (_detection.SourceAvatarData != null)
            {
                var srcAnimator = _detection.SourceAvatarData.AvatarAnimator;
                if (srcAnimator == null || !srcAnimator.isHuman)
                    nonHumanoidNames.Add($"移植元 ({_detection.SourceAvatar.name})");
            }

            if (_detection.DestAvatarData != null)
            {
                var destAnimator = _detection.DestAvatarData.AvatarAnimator;
                if (destAnimator == null || !destAnimator.isHuman)
                    nonHumanoidNames.Add($"移植先 ({_detection.DestinationAvatar.name})");
            }

            if (nonHumanoidNames.Count > 0)
            {
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

        private void UpdateSerializedBoneReferences()
        {
            if (_detection == null || !_detection.IsLiveMode)
                return;
            if (_detection.SourceAvatarData == null)
                return;

            var definition = (PBRemap)target;
            var definitionRoot = definition.transform;
            var sourceData = _detection.SourceAvatarData;
            var sourceArmature = sourceData.Armature.transform;
            var sourceAnimator = sourceData.AvatarAnimator;

            var humanoidBoneMap = new Dictionary<Transform, HumanBodyBones>();
            if (sourceAnimator != null && sourceAnimator.isHuman)
            {
                foreach (HumanBodyBones boneId in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (boneId == HumanBodyBones.LastBone) continue;
                    var bone = sourceAnimator.GetBoneTransform(boneId);
                    if (bone != null && !humanoidBoneMap.ContainsKey(bone))
                        humanoidBoneMap[bone] = boneId;
                }
            }

            var boneRefs = new List<SerializedBoneReference>();
            ScanComponentReferences<VRCPhysBone>(definitionRoot, sourceArmature, humanoidBoneMap, boneRefs);
            ScanComponentReferences<VRCPhysBoneCollider>(definitionRoot, sourceArmature, humanoidBoneMap, boneRefs);
            ScanComponentReferences<VRCConstraintBase>(definitionRoot, sourceArmature, humanoidBoneMap, boneRefs);
            ScanComponentReferences<ContactBase>(definitionRoot, sourceArmature, humanoidBoneMap, boneRefs);

            serializedObject.Update();
            _serializedBoneRefsProp.ClearArray();
            for (int i = 0; i < boneRefs.Count; i++)
            {
                _serializedBoneRefsProp.InsertArrayElementAtIndex(i);
                var element = _serializedBoneRefsProp.GetArrayElementAtIndex(i);
                var br = boneRefs[i];
                element.FindPropertyRelative("componentObjectPath").stringValue = br.componentObjectPath;
                element.FindPropertyRelative("componentTypeName").stringValue = br.componentTypeName;
                element.FindPropertyRelative("propertyPath").stringValue = br.propertyPath;
                element.FindPropertyRelative("boneRelativePath").stringValue = br.boneRelativePath;
                element.FindPropertyRelative("humanBodyBone").enumValueIndex = (int)br.humanBodyBone;
                element.FindPropertyRelative("nearestHumanoidAncestor").enumValueIndex = (int)br.nearestHumanoidAncestor;
                element.FindPropertyRelative("pathFromHumanoidAncestor").stringValue = br.pathFromHumanoidAncestor ?? "";
            }

            float sourceScale = PBRemapper.CalculateAvatarScale(sourceData);
            _sourceAvatarScaleProp.floatValue = sourceScale;

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private void ScanComponentReferences<T>(
            Transform definitionRoot,
            Transform sourceArmature,
            Dictionary<Transform, HumanBodyBones> humanoidBoneMap,
            List<SerializedBoneReference> results) where T : Component
        {
            foreach (var component in definitionRoot.GetComponentsInChildren<T>(true))
            {
                var so = new SerializedObject(component);
                var prop = so.GetIterator();

                while (prop.Next(true))
                {
                    if (prop.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    var objRef = prop.objectReferenceValue as Transform;
                    if (objRef == null)
                        continue;

                    if (objRef.IsChildOf(definitionRoot))
                        continue;

                    string relativePath = BoneMapper.GetRelativePath(objRef, sourceArmature);
                    if (relativePath == null)
                        continue;

                    string componentPath = GetRelativePath(component.transform, definitionRoot);
                    if (componentPath == null)
                        continue;

                    var boneRef = new SerializedBoneReference
                    {
                        componentObjectPath = componentPath,
                        componentTypeName = component.GetType().Name,
                        propertyPath = prop.propertyPath,
                        boneRelativePath = relativePath,
                        humanBodyBone = HumanBodyBones.LastBone,
                        nearestHumanoidAncestor = HumanBodyBones.LastBone,
                        pathFromHumanoidAncestor = ""
                    };

                    if (humanoidBoneMap.TryGetValue(objRef, out var humanBone))
                    {
                        boneRef.humanBodyBone = humanBone;
                    }

                    var ancestor = objRef.parent;
                    var pathSegments = new List<string> { objRef.name };
                    while (ancestor != null && ancestor != sourceArmature.parent)
                    {
                        if (humanoidBoneMap.TryGetValue(ancestor, out var ancestorBone))
                        {
                            boneRef.nearestHumanoidAncestor = ancestorBone;
                            pathSegments.Reverse();
                            boneRef.pathFromHumanoidAncestor = string.Join("/", pathSegments);
                            break;
                        }
                        pathSegments.Add(ancestor.name);
                        ancestor = ancestor.parent;
                    }

                    results.Add(boneRef);
                }
            }
        }

        private static string GetRelativePath(Transform target, Transform root)
        {
            if (target == root) return "";

            var segments = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }
            if (current != root) return null;

            segments.Reverse();
            return string.Join("/", segments);
        }

        #endregion

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
            var root = new VisualElement();

            var row = new VisualElement();
            row.AddToClassList("remap-rule-item");

            var enabledToggle = new Toggle();
            enabledToggle.name = "rule-enabled";
            row.Add(enabledToggle);

            var modeField = new EnumField(PathRemapRule.RemapMode.CharacterSubstitution);
            modeField.name = "rule-mode";
            row.Add(modeField);

            var sourcePatternField = new TextField();
            sourcePatternField.name = "rule-source-pattern";
            row.Add(sourcePatternField);

            var arrowLabel = new Label("\u2194");
            arrowLabel.AddToClassList("remap-rule-arrow");
            row.Add(arrowLabel);

            var destPatternField = new TextField();
            destPatternField.name = "rule-dest-pattern";
            row.Add(destPatternField);

            var deleteButton = new Button();
            deleteButton.name = "rule-delete";
            deleteButton.text = "\u2715";
            deleteButton.AddToClassList("remap-rule-delete-button");
            row.Add(deleteButton);

            root.Add(row);

            var hintLabel = new Label();
            hintLabel.name = "rule-hint";
            hintLabel.AddToClassList("remap-rule-hint");
            root.Add(hintLabel);

            // モード変更時にヒントとtooltipを更新（makeItemで1回だけ登録）
            modeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is PathRemapRule.RemapMode mode)
                {
                    UpdateRuleHint(hintLabel, mode);
                    UpdateFieldTooltips(sourcePatternField, destPatternField, mode);
                }
            });

            return root;
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
            var hintLabel = element.Q<Label>("rule-hint");

            enabledToggle.BindProperty(enabledProp);
            modeField.BindProperty(modeProp);
            sourcePatternField.BindProperty(sourcePatternProp);
            destPatternField.BindProperty(destPatternProp);

            deleteButton.clickable = new Clickable(() => OnDeleteRuleClicked(index));

            var mode = (PathRemapRule.RemapMode)modeProp.enumValueIndex;
            UpdateRuleHint(hintLabel, mode);
            UpdateFieldTooltips(sourcePatternField, destPatternField, mode);
            hintLabel.style.display = _showRuleHints ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void UpdateRuleHint(Label hintLabel, PathRemapRule.RemapMode mode)
        {
            switch (mode)
            {
                case PathRemapRule.RemapMode.PrefixReplace:
                    hintLabel.text =
                        "ボーン名の先頭を双方向で置換。空欄で接頭辞を除去/追加\n" +
                        "例: [J_Bip_C_] \u2194 [] → J_Bip_C_Hips \u2194 Hips";
                    break;
                case PathRemapRule.RemapMode.CharacterSubstitution:
                    hintLabel.text = "ボーン名内の文字列を双方向で全置換  例: [_L] \u2194 [.L]";
                    break;
                case PathRemapRule.RemapMode.RegexReplace:
                    hintLabel.text =
                        "左欄=正規表現パターン  右欄=置換文字列（双方向）\n" +
                        "例: [Bone(\\d+)] \u2194 [B$1]";
                    break;
            }
        }

        private static void UpdateFieldTooltips(
            TextField sourceField, TextField destField, PathRemapRule.RemapMode mode)
        {
            switch (mode)
            {
                case PathRemapRule.RemapMode.PrefixReplace:
                    sourceField.tooltip = "移植元の接頭辞（空欄 = 接頭辞なし）";
                    destField.tooltip = "移植先の接頭辞（空欄 = 接頭辞なし）";
                    break;
                case PathRemapRule.RemapMode.CharacterSubstitution:
                    sourceField.tooltip = "移植元の文字列";
                    destField.tooltip = "移植先の文字列";
                    break;
                case PathRemapRule.RemapMode.RegexReplace:
                    sourceField.tooltip = "正規表現パターン（例: Bone(\\d+)）";
                    destField.tooltip = "置換文字列（例: B$1）。$1 でキャプチャグループ参照";
                    break;
            }
        }

        #endregion

        #region イベントハンドラ

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
            var definition = (PBRemap)target;
            if (_detection != null)
                PBRemapPreviewWindow.Open(definition, _detection);
        }

        private void OnRemapClicked()
        {
            serializedObject.Update();
            var definition = (PBRemap)target;

            var settings = PBReplacerSettings.Load();
            if (settings.ShowConfirmDialog)
            {
                string destName = _detection?.DestinationAvatar?.name ?? "(不明)";
                string sourceName = _detection?.IsLiveMode == true
                    ? _detection.SourceAvatar?.name ?? "(不明)"
                    : "(Prefab)";

                bool confirmed = EditorUtility.DisplayDialog(
                    "コンポーネント移植",
                    $"移植元: {sourceName}\n" +
                    $"移植先: {destName}\n\n" +
                    "移植を実行しますか？\n" +
                    "（コンポーネントのボーン参照がリマップされます）",
                    "実行", "キャンセル");

                if (!confirmed)
                    return;
            }

            var result = PBRemapper.Remap(definition);

            result.Match(
                onSuccess: success =>
                {
                    string message =
                        $"移植（リマップ）が完了しました\n\n" +
                        $"リマップ済みコンポーネント: {success.RemappedComponentCount}\n" +
                        $"リマップ済み参照: {success.RemappedReferenceCount}";

                    if (success.UnresolvedReferenceCount > 0)
                        message += $"\n未解決参照: {success.UnresolvedReferenceCount}";

                    if (success.Warnings.Count > 0)
                        message += $"\n\n警告 ({success.Warnings.Count}):\n" +
                                   string.Join("\n", success.Warnings);

                    EditorUtility.DisplayDialog("移植完了", message, "OK");

                    _statusBox.text = $"移植完了: {success.RemappedReferenceCount} 参照をリマップ";
                    _statusBox.messageType = success.UnresolvedReferenceCount > 0
                        ? HelpBoxMessageType.Warning
                        : HelpBoxMessageType.Info;
                    _statusBox.style.display = DisplayStyle.Flex;

                    RefreshDetection();
                },
                onFailure: error =>
                {
                    _statusBox.text = error;
                    _statusBox.messageType = HelpBoxMessageType.Error;
                    _statusBox.style.display = DisplayStyle.Flex;
                });
        }

        /// <summary>
        /// プレビューウィンドウをフォーカスせずに取得する。
        /// </summary>
        private static PBRemapPreviewWindow FindPreviewWindow()
        {
            var windows = Resources.FindObjectsOfTypeAll<PBRemapPreviewWindow>();
            return windows.Length > 0 ? windows[0] : null;
        }

        #endregion

        #region スケール

        private void UpdateCalculatedScaleLabel()
        {
            if (_detection == null)
            {
                _calculatedScaleLabel.text = "";
                return;
            }

            try
            {
                if (_detection.IsLiveMode
                    && _detection.SourceAvatarData != null
                    && _detection.DestAvatarData != null)
                {
                    float scale = ScaleCalculator.CalculateScaleFactor(
                        _detection.SourceAvatarData.Armature.transform,
                        _detection.DestAvatarData.Armature.transform,
                        _detection.SourceAvatarData.AvatarAnimator,
                        _detection.DestAvatarData.AvatarAnimator);
                    _calculatedScaleLabel.text = $"算出値: {scale:F4}";
                }
                else if (!_detection.IsLiveMode
                    && _detection.DestAvatarData != null)
                {
                    var definition = (PBRemap)target;
                    if (definition.SourceAvatarScale > 0)
                    {
                        float destScale = PBRemapper.CalculateAvatarScale(_detection.DestAvatarData);
                        float scale = destScale / definition.SourceAvatarScale;
                        _calculatedScaleLabel.text = $"算出値: {scale:F4} (Prefab)";
                    }
                    else
                    {
                        _calculatedScaleLabel.text = "算出不可 (ソーススケール未保存)";
                    }
                }
                else
                {
                    _calculatedScaleLabel.text = "算出不可";
                }
            }
            catch
            {
                _calculatedScaleLabel.text = "算出不可";
            }
        }

        #endregion
    }
}
