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
    [CustomEditor(typeof(TransplantDefinition))]
    public class TransplantDefinitionEditor : Editor
    {
        private VisualElement _root;

        // 検出状態
        private Label _destAvatarLabel;
        private Label _sourceAvatarLabel;
        private Label _modeLabel;
        private HelpBox _transplantStateBox;
        private HelpBox _detectionWarningBox;
        private HelpBox _humanoidInfoBox;

        // コンポーネント一覧
        private Label _componentsSummary;
        private Label _resolutionSummary;

        // リマップルール
        private ListView _rulesListView;

        // スケール
        private Toggle _autoScaleToggle;
        private VisualElement _manualScaleContainer;
        private FloatField _scaleFactorField;
        private Label _calculatedScaleLabel;

        // プレビュー
        private Button _previewButton;
        private Foldout _previewFoldout;
        private Label _previewSummary;
        private ListView _previewBoneList;
        private Foldout _previewBoneFoldout;
        private ListView _previewWarningsList;
        private Foldout _previewWarningsFoldout;
        private TransplantPreviewData _currentPreview;

        // アクション
        private Button _transplantButton;
        private HelpBox _statusBox;

        // SerializedProperties
        private SerializedProperty _autoCalculateScaleProp;
        private SerializedProperty _scaleFactorProp;
        private SerializedProperty _pathRemapRulesProp;
        private SerializedProperty _serializedBoneRefsProp;
        private SerializedProperty _sourceAvatarScaleProp;

        // 検出結果キャッシュ
        private SourceDetector.DetectionResult _detection;

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
            _destAvatarLabel = _root.Q<Label>("dest-avatar-label");
            _sourceAvatarLabel = _root.Q<Label>("source-avatar-label");
            _modeLabel = _root.Q<Label>("mode-label");
            _transplantStateBox = _root.Q<HelpBox>("transplant-state-box");
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
            _previewFoldout = _root.Q<Foldout>("preview-foldout");
            _previewSummary = _root.Q<Label>("preview-summary");
            _previewBoneList = _root.Q<ListView>("preview-bone-list");
            _previewBoneFoldout = _root.Q<Foldout>("preview-bone-foldout");
            _previewWarningsList = _root.Q<ListView>("preview-warnings-list");
            _previewWarningsFoldout = _root.Q<Foldout>("preview-warnings-foldout");
            _transplantButton = _root.Q<Button>("transplant-button");
            _statusBox = _root.Q<HelpBox>("status-box");
            var addRuleButton = _root.Q<Button>("add-rule-button");

            // バインド
            _root.Bind(serializedObject);

            // ListView設定
            SetupRemapRulesListView();
            SetupPreviewBoneListView();
            SetupPreviewWarningsListView();

            // イベント登録
            _autoScaleToggle.RegisterValueChangedCallback(evt => OnAutoScaleChanged(evt.newValue));
            addRuleButton.clicked += OnAddRuleClicked;
            _transplantButton.clicked += OnTransplantClicked;
            _previewButton.clicked += OnPreviewClicked;

            // 初期状態を設定
            OnAutoScaleChanged(_autoCalculateScaleProp.boolValue);
            RefreshDetection();
            UpdateSerializedBoneReferences();

            return _root;
        }

        #region 検出と更新

        /// <summary>
        /// SourceDetectorを実行し、UIを更新する。
        /// </summary>
        private void RefreshDetection()
        {
            var definition = (TransplantDefinition)target;
            var detectResult = SourceDetector.Detect(definition);

            if (detectResult.IsFailure)
            {
                _destAvatarLabel.text = "移植先: (検出エラー)";
                _sourceAvatarLabel.text = "移植元: (検出エラー)";
                _transplantButton.SetEnabled(false);
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

            // 状態に応じたステータスボックス表示
            UpdateTransplantStateBox(definition);

            // 検出エラー警告（解析失敗等）
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

            // コンポーネント一覧更新
            UpdateComponentsSummary(definition);

            // Humanoid情報更新
            UpdateHumanoidInfoBox();

            // ボタン有効化判定
            bool canOperate = !_detection.IsReferencingDestination
                && _detection.DestinationAvatar != null
                && (_detection.IsLiveMode && _detection.SourceAvatar != null
                    || !_detection.IsLiveMode && definition.SerializedBoneReferences.Count > 0);
            _transplantButton.SetEnabled(canOperate);
            _previewButton.SetEnabled(canOperate || _detection.IsReferencingDestination);

            // ボーン解決サマリーを自動更新
            UpdateResolutionSummary(definition);

            // スケールラベル更新
            if (_autoCalculateScaleProp.boolValue)
                UpdateCalculatedScaleLabel();

            // ステータス初期化
            _statusBox.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// 移植の状態に応じたHelpBoxメッセージを表示する。
        /// </summary>
        private void UpdateTransplantStateBox(TransplantDefinition definition)
        {
            if (_detection.DestinationAvatar == null)
            {
                _transplantStateBox.text = "このコンポーネントをアバターの子階層に配置してください。";
                _transplantStateBox.messageType = HelpBoxMessageType.Info;
                _transplantStateBox.style.display = DisplayStyle.Flex;
            }
            else if (_detection.IsReferencingDestination)
            {
                _transplantStateBox.text =
                    "移植済み — ボーン参照は移植先アバターに接続されています。";
                _transplantStateBox.messageType = HelpBoxMessageType.Info;
                _transplantStateBox.style.display = DisplayStyle.Flex;
            }
            else if (_detection.IsLiveMode && _detection.SourceAvatar != null
                || !_detection.IsLiveMode && definition.SerializedBoneReferences.Count > 0)
            {
                // 移植準備完了 — ステートボックスは非表示（検出ラベルで十分）
                _transplantStateBox.style.display = DisplayStyle.None;
            }
            else
            {
                _transplantStateBox.text =
                    "移植対象のコンポーネントをこの階層の子に配置してください。";
                _transplantStateBox.messageType = HelpBoxMessageType.Info;
                _transplantStateBox.style.display = DisplayStyle.Flex;
            }
        }

        /// <summary>
        /// ボーン解決状態を自動チェックし、コンポーネント一覧の下にサマリーを表示する。
        /// プレビューを開かなくても未解決ボーンが一目で分かるようにする。
        /// </summary>
        private void UpdateResolutionSummary(TransplantDefinition definition)
        {
            // 移植可能な状態でのみチェックを実行
            bool canCheck = _detection.DestinationAvatar != null
                && _detection.DestAvatarData != null
                && !_detection.IsReferencingDestination
                && (_detection.IsLiveMode && _detection.SourceAvatar != null
                    || !_detection.IsLiveMode && definition.SerializedBoneReferences.Count > 0);

            if (!canCheck)
            {
                _resolutionSummary.style.display = DisplayStyle.None;
                return;
            }

            // 軽量プレビューを実行（副作用なし）
            var preview = TransplantPreview.GeneratePreview(definition, _detection);
            int total = preview.ResolvedBones + preview.UnresolvedBones;

            if (total == 0)
            {
                _resolutionSummary.style.display = DisplayStyle.None;
                return;
            }

            _resolutionSummary.style.display = DisplayStyle.Flex;

            if (preview.UnresolvedBones == 0)
            {
                _resolutionSummary.text = $"ボーン解決: {preview.ResolvedBones}/{total} 全て解決済み";
                _resolutionSummary.RemoveFromClassList("transplant-resolution-unresolved");
                _resolutionSummary.AddToClassList("transplant-resolution-resolved");
            }
            else
            {
                _resolutionSummary.text =
                    $"ボーン解決: {preview.ResolvedBones}/{total} ({preview.UnresolvedBones} 未解決)";
                _resolutionSummary.RemoveFromClassList("transplant-resolution-resolved");
                _resolutionSummary.AddToClassList("transplant-resolution-unresolved");
            }
        }

        /// <summary>
        /// 配下のVRCコンポーネント数を集計して表示する。
        /// </summary>
        private void UpdateComponentsSummary(TransplantDefinition definition)
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

        /// <summary>
        /// 非Humanoidアバターの情報を表示する。
        /// </summary>
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

        /// <summary>
        /// Custom Editorが表示されている間、シリアライズ済みボーン参照データを自動更新する。
        /// Prefab化時にこのデータが保持されるため、Inspector表示時に常に最新化しておく。
        /// </summary>
        private void UpdateSerializedBoneReferences()
        {
            if (_detection == null || !_detection.IsLiveMode)
                return;
            if (_detection.SourceAvatarData == null)
                return;

            var definition = (TransplantDefinition)target;
            var definitionRoot = definition.transform;
            var sourceData = _detection.SourceAvatarData;
            var sourceArmature = sourceData.Armature.transform;
            var sourceAnimator = sourceData.AvatarAnimator;

            // Humanoidボーンの逆引きマップ構築
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

            // 全VRCコンポーネントのTransform参照をスキャンしてシリアライズ
            var boneRefs = new List<SerializedBoneReference>();
            ScanComponentReferences<VRCPhysBone>(definitionRoot, sourceArmature, humanoidBoneMap, boneRefs);
            ScanComponentReferences<VRCPhysBoneCollider>(definitionRoot, sourceArmature, humanoidBoneMap, boneRefs);
            ScanComponentReferences<VRCConstraintBase>(definitionRoot, sourceArmature, humanoidBoneMap, boneRefs);
            ScanComponentReferences<ContactBase>(definitionRoot, sourceArmature, humanoidBoneMap, boneRefs);

            // SerializedObjectとして書き込み
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

            // ソースアバタースケールも保存
            float sourceScale = TransplantRemapper.CalculateAvatarScale(sourceData);
            _sourceAvatarScaleProp.floatValue = sourceScale;

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 指定コンポーネント型の全インスタンスからTransform参照をスキャンする。
        /// </summary>
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

                    // 内部参照はスキップ
                    if (objRef.IsChildOf(definitionRoot))
                        continue;

                    // ボーンの相対パスを計算
                    string relativePath = BoneMapper.GetRelativePath(objRef, sourceArmature);
                    if (relativePath == null)
                        continue;

                    // コンポーネントオブジェクトの相対パス
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

                    // Humanoidボーン情報を付与
                    if (humanoidBoneMap.TryGetValue(objRef, out var humanBone))
                    {
                        boneRef.humanBodyBone = humanBone;
                    }

                    // 最寄りHumanoid祖先を探索
                    var ancestor = objRef.parent;
                    var pathSegments = new List<string> { objRef.name };
                    while (ancestor != null && ancestor != sourceArmature.parent)
                    {
                        if (humanoidBoneMap.TryGetValue(ancestor, out var ancestorBone))
                        {
                            boneRef.nearestHumanoidAncestor = ancestorBone;
                            pathSegments.Reverse();
                            // 祖先自身は含めず、その子からのパス
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

        /// <summary>
        /// rootからtargetまでの相対パスを取得する。
        /// </summary>
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

            enabledToggle.BindProperty(enabledProp);
            modeField.BindProperty(modeProp);
            sourcePatternField.BindProperty(sourcePatternProp);
            destPatternField.BindProperty(destPatternProp);

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

            // 再検出してプレビュー生成
            RefreshDetection();
            _currentPreview = TransplantPreview.GeneratePreview(definition, _detection);
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

            // リマップ実行
            var result = TransplantRemapper.Remap(definition);

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

                    // 検出状態を更新（リマップ後は参照先が変わっている）
                    RefreshDetection();
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

            int totalBones = _currentPreview.ResolvedBones + _currentPreview.UnresolvedBones;

            _previewSummary.text =
                $"PB:{_currentPreview.TotalPhysBones} " +
                $"PBC:{_currentPreview.TotalPhysBoneColliders} " +
                $"Constraint:{_currentPreview.TotalConstraints} " +
                $"Contact:{_currentPreview.TotalContacts}" +
                $" | ボーン解決: {_currentPreview.ResolvedBones}/{totalBones}" +
                $" | スケール: {_currentPreview.CalculatedScaleFactor:F3}";

            _previewBoneList.itemsSource = _currentPreview.BoneMappings;
            _previewBoneList.style.height = Mathf.Min(
                _currentPreview.BoneMappings.Count * 22, 200);
            _previewBoneList.Rebuild();

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
                    var definition = (TransplantDefinition)target;
                    if (definition.SourceAvatarScale > 0)
                    {
                        float destScale = TransplantRemapper.CalculateAvatarScale(_detection.DestAvatarData);
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
