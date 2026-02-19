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

        // 検出状態 (移植元 → 移植先の順)
        private Label _sourceAvatarLabel;
        private Label _destAvatarLabel;
        private Label _modeLabel;
        private Label _sourceBadge;
        private Label _destBadge;
        private HelpBox _stateBox;
        private HelpBox _detectionWarningBox;

        // 手動指定
        private ObjectField _sourceRootOverrideField;
        private ObjectField _destRootOverrideField;

        // コンポーネント一覧
        private Label _componentsSummary;
        private VisualElement _resolutionSummary;

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
        private SerializedProperty _sourceRootOverrideProp;
        private SerializedProperty _destRootOverrideProp;

        // 検出結果キャッシュ
        private SourceDetector.DetectionResult _detection;

        // 階層変更検知用
        private Transform _cachedParent;
        private int _cachedChildCount;

        // UXML文字列リソース
        private StringResources _strings;

        /// <summary>
        /// UXML文字列リソースのキャッシュ構造体。
        /// 固定テキストをUXMLに外部化し、コンパイル不要で変更可能にする。
        /// </summary>
        private struct StringResources
        {
            // 検出ラベル
            public string DetectSourcePrefix;
            public string DetectDestPrefix;
            public string DetectSourceUndetected;
            public string DetectDestUndetected;
            public string DetectSourceError;
            public string DetectDestError;
            public string DetectSourceTransplanted;
            public string DetectSourcePrefab;

            // モードラベル
            public string ModeLive;
            public string ModePrefab;

            // ステートボックス
            public string StateNoDest;
            public string StateTransplanted;
            public string StateNoSource;

            // バッジ
            public string BadgeFallbackText;
            public string BadgeFallbackTooltip;
            public string BadgeNonHumanoidText;
            public string BadgeNonHumanoidTooltip;

            // スケール
            public string ScaleUnavailable;
            public string ScaleNoSourceScale;

            // ルールヒント
            public string HintPrefixReplace;
            public string HintCharSubstitution;
            public string HintRegexReplace;

            // ルールフィールドtooltip
            public string TooltipPrefixSource;
            public string TooltipPrefixDest;
            public string TooltipCharSource;
            public string TooltipCharDest;
            public string TooltipRegexSource;
            public string TooltipRegexDest;

            // ダイアログ
            public string DialogTitle;
            public string DialogConfirmTemplate;
            public string DialogOk;
            public string DialogCancel;
            public string DialogCompleteTitle;
            public string DialogCompleteOk;
        }

        private void LoadStringResources()
        {
            string Text(string name) => _root.Q<Label>(name)?.text ?? "";
            string Tooltip(string name) => _root.Q<Label>(name)?.tooltip ?? "";

            _strings = new StringResources
            {
                DetectSourcePrefix = Text("str-detect-source-prefix"),
                DetectDestPrefix = Text("str-detect-dest-prefix"),
                DetectSourceUndetected = Text("str-detect-source-undetected"),
                DetectDestUndetected = Text("str-detect-dest-undetected"),
                DetectSourceError = Text("str-detect-source-error"),
                DetectDestError = Text("str-detect-dest-error"),
                DetectSourceTransplanted = Text("str-detect-source-transplanted"),
                DetectSourcePrefab = Text("str-detect-source-prefab"),

                ModeLive = Text("str-mode-live"),
                ModePrefab = Text("str-mode-prefab"),

                StateNoDest = Text("str-state-no-dest"),
                StateTransplanted = Text("str-state-transplanted"),
                StateNoSource = Text("str-state-no-source"),

                BadgeFallbackText = Text("str-badge-fallback"),
                BadgeFallbackTooltip = Tooltip("str-badge-fallback"),
                BadgeNonHumanoidText = Text("str-badge-non-humanoid"),
                BadgeNonHumanoidTooltip = Tooltip("str-badge-non-humanoid"),

                ScaleUnavailable = Text("str-scale-unavailable"),
                ScaleNoSourceScale = Text("str-scale-no-source-scale"),

                HintPrefixReplace = Text("str-hint-prefix-replace"),
                HintCharSubstitution = Text("str-hint-char-substitution"),
                HintRegexReplace = Text("str-hint-regex-replace"),

                TooltipPrefixSource = Text("str-tooltip-prefix-source"),
                TooltipPrefixDest = Text("str-tooltip-prefix-dest"),
                TooltipCharSource = Text("str-tooltip-char-source"),
                TooltipCharDest = Text("str-tooltip-char-dest"),
                TooltipRegexSource = Text("str-tooltip-regex-source"),
                TooltipRegexDest = Text("str-tooltip-regex-dest"),

                DialogTitle = Text("str-dialog-title"),
                DialogConfirmTemplate = Text("str-dialog-confirm-template"),
                DialogOk = Text("str-dialog-ok"),
                DialogCancel = Text("str-dialog-cancel"),
                DialogCompleteTitle = Text("str-dialog-complete-title"),
                DialogCompleteOk = Text("str-dialog-complete-ok"),
            };
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();

            // SerializedPropertyを取得
            _autoCalculateScaleProp = serializedObject.FindProperty("autoCalculateScale");
            _scaleFactorProp = serializedObject.FindProperty("scaleFactor");
            _pathRemapRulesProp = serializedObject.FindProperty("pathRemapRules");
            _serializedBoneRefsProp = serializedObject.FindProperty("serializedBoneReferences");
            _sourceAvatarScaleProp = serializedObject.FindProperty("sourceAvatarScale");
            _sourceRootOverrideProp = serializedObject.FindProperty("sourceRootOverride");
            _destRootOverrideProp = serializedObject.FindProperty("destinationRootOverride");

            // UXMLをロード
            var visualTree = Resources.Load<VisualTreeAsset>("UXML/PBRemap");
            if (visualTree == null)
            {
                _root.Add(new HelpBox("PBRemap.uxml が見つかりません", HelpBoxMessageType.Error));
                return _root;
            }

            visualTree.CloneTree(_root);

            // 文字列リソースをロード
            LoadStringResources();

            // USSをロード
            var styleSheet = Resources.Load<StyleSheet>("USS/PBRemap");
            if (styleSheet != null)
                _root.styleSheets.Add(styleSheet);

            // 要素を取得 (移植元 → 移植先の順)
            _sourceAvatarLabel = _root.Q<Label>("source-avatar-label");
            _destAvatarLabel = _root.Q<Label>("dest-avatar-label");
            _modeLabel = _root.Q<Label>("mode-label");
            _sourceBadge = _root.Q<Label>("source-badge");
            _destBadge = _root.Q<Label>("dest-badge");
            _stateBox = _root.Q<HelpBox>("pbremap-state-box");
            _detectionWarningBox = _root.Q<HelpBox>("detection-warning-box");
            _sourceRootOverrideField = _root.Q<ObjectField>("source-root-override");
            _destRootOverrideField = _root.Q<ObjectField>("dest-root-override");
            _componentsSummary = _root.Q<Label>("components-summary");
            _resolutionSummary = _root.Q<VisualElement>("resolution-summary");
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

            // 手動指定フィールドの型を設定
            if (_sourceRootOverrideField != null)
            {
                _sourceRootOverrideField.objectType = typeof(GameObject);
                _sourceRootOverrideField.RegisterValueChangedCallback(_ =>
                {
                    RefreshDetection();
                    UpdateSerializedBoneReferences();
                });
            }
            if (_destRootOverrideField != null)
            {
                _destRootOverrideField.objectType = typeof(GameObject);
                _destRootOverrideField.RegisterValueChangedCallback(_ =>
                {
                    RefreshDetection();
                    UpdateSerializedBoneReferences();
                });
            }

            // イベント登録
            _autoScaleToggle.RegisterValueChangedCallback(evt => OnAutoScaleChanged(evt.newValue));
            addRuleButton.clicked += OnAddRuleClicked;
            _remapButton.clicked += OnRemapClicked;
            _previewButton.clicked += OnPreviewClicked;

            // ルールヒント表示切替ボタン
            var toggleHintsButton = _root.Q<Button>("toggle-hints-button");
            if (toggleHintsButton != null)
            {
                toggleHintsButton.clicked += () =>
                {
                    _showRuleHints = !_showRuleHints;
                    toggleHintsButton.EnableInClassList("pbremap-hint-toggle-button-active", _showRuleHints);
                    _rulesListView.Rebuild();
                };
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

                // SceneViewプレビューが有効ならキャッシュを再構築
                if (PBRemapScenePreviewState.Instance.IsActive && _detection.IsLiveMode)
                {
                    var previewData = PBRemapPreview.GeneratePreview(def, _detection);
                    PBRemapScenePreviewState.Instance.Activate(previewData, _detection);
                }
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

            // プレビューウィンドウが閉じていればSceneViewプレビューを無効化
            if (FindPreviewWindow() == null)
                PBRemapScenePreviewState.Instance.Deactivate();
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
                _sourceAvatarLabel.text = _strings.DetectSourceError;
                _destAvatarLabel.text = _strings.DetectDestError;
                _remapButton.SetEnabled(false);
                _previewButton.SetEnabled(false);
                return;
            }

            _detection = detectResult.Value;

            // ソース表示 (移植元を先に更新)
            if (_detection.IsReferencingDestination)
            {
                _sourceAvatarLabel.text = _strings.DetectSourceTransplanted;
                _modeLabel.text = "";
            }
            else if (_detection.IsLiveMode && _detection.SourceAvatar != null)
            {
                _sourceAvatarLabel.text = _strings.DetectSourcePrefix + _detection.SourceAvatar.name;
                _modeLabel.text = _strings.ModeLive;
            }
            else if (!_detection.IsLiveMode && definition.SerializedBoneReferences.Count > 0)
            {
                _sourceAvatarLabel.text = _strings.DetectSourcePrefab;
                _modeLabel.text = _strings.ModePrefab;
            }
            else
            {
                _sourceAvatarLabel.text = _strings.DetectSourceUndetected;
                _modeLabel.text = "";
            }

            // デスティネーション表示
            if (_detection.DestinationAvatar != null)
                _destAvatarLabel.text = _strings.DetectDestPrefix + _detection.DestinationAvatar.name;
            else
                _destAvatarLabel.text = _strings.DetectDestUndetected;

            UpdateStateBox(definition);
            UpdateDetectionBadges();

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

            // SceneViewプレビューが有効なら検出結果の変更を反映
            if (PBRemapScenePreviewState.Instance.IsActive && _detection.IsLiveMode)
            {
                var def = (PBRemap)target;
                var previewData = PBRemapPreview.GeneratePreview(def, _detection);
                PBRemapScenePreviewState.Instance.Activate(previewData, _detection);
            }

            if (_autoCalculateScaleProp.boolValue)
                UpdateCalculatedScaleLabel();

            _statusBox.style.display = DisplayStyle.None;
        }

        private void UpdateStateBox(PBRemap definition)
        {
            if (_detection.DestinationAvatar == null)
            {
                _stateBox.text = _strings.StateNoDest;
                _stateBox.messageType = HelpBoxMessageType.Info;
                _stateBox.style.display = DisplayStyle.Flex;
            }
            else if (_detection.IsReferencingDestination)
            {
                _stateBox.text = _strings.StateTransplanted;
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
                _stateBox.text = _strings.StateNoSource;
                _stateBox.messageType = HelpBoxMessageType.Info;
                _stateBox.style.display = DisplayStyle.Flex;
            }
        }

        private void UpdateDetectionBadges()
        {
            if (_detection == null) return;

            // 移植元 → 移植先の順で更新
            UpdateSingleBadge(
                _sourceBadge,
                _detection.SourceAvatar,
                _detection.SourceHasDescriptor,
                _detection.SourceAvatarData);

            UpdateSingleBadge(
                _destBadge,
                _detection.DestinationAvatar,
                _detection.DestinationHasDescriptor,
                _detection.DestAvatarData);
        }

        private void UpdateSingleBadge(
            Label badge, GameObject avatar, bool hasDescriptor, AvatarData avatarData)
        {
            if (badge == null) return;

            var tags = new List<string>();
            var tooltipParts = new List<string>();

            if (avatar != null && !hasDescriptor)
            {
                tags.Add(_strings.BadgeFallbackText);
                tooltipParts.Add(_strings.BadgeFallbackTooltip);
            }

            if (avatarData != null)
            {
                var animator = avatarData.AvatarAnimator;
                if (animator == null || !animator.isHuman)
                {
                    tags.Add(_strings.BadgeNonHumanoidText);
                    tooltipParts.Add(_strings.BadgeNonHumanoidTooltip);
                }
            }

            if (tags.Count > 0)
            {
                badge.text = string.Join(" / ", tags);
                badge.tooltip = string.Join("\n\n", tooltipParts);
                badge.style.display = DisplayStyle.Flex;
            }
            else
            {
                badge.style.display = DisplayStyle.None;
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
            _resolutionSummary.Clear();

            int autoCreatable = preview.AutoCreatableBones;
            int trueUnresolved = preview.UnresolvedBones - autoCreatable;

            // ヘッダーラベル
            var header = new Label($"ボーン解決: {preview.ResolvedBones}/{total}");
            header.AddToClassList("pbremap-resolution-header");
            _resolutionSummary.Add(header);

            // 解決済みチップ
            if (preview.ResolvedBones > 0)
                _resolutionSummary.Add(CreateResolutionChip(preview.ResolvedBones, "解決済み", "resolved"));

            // 作成予定チップ
            if (autoCreatable > 0)
                _resolutionSummary.Add(CreateResolutionChip(autoCreatable, "作成予定", "auto-creatable"));

            // 未解決チップ
            if (trueUnresolved > 0)
            {
                _resolutionSummary.Add(CreateResolutionChip(trueUnresolved, "未解決", "unresolved"));
                Highlighter.Highlight("Inspector", "プレビュー", HighlightSearchMode.Auto);
            }
            else
            {
                Highlighter.Stop();
            }
        }

        private static VisualElement CreateResolutionChip(int count, string label, string state)
        {
            var chip = new VisualElement();
            chip.AddToClassList("pbremap-resolution-chip");
            chip.AddToClassList($"pbremap-resolution-chip-{state}");

            var dot = new VisualElement();
            dot.AddToClassList("pbremap-resolution-dot");
            dot.AddToClassList($"pbremap-resolution-dot-{state}");
            chip.Add(dot);

            var text = new Label($"{count} {label}");
            text.AddToClassList("pbremap-resolution-chip-label");
            text.AddToClassList($"pbremap-resolution-chip-label-{state}");
            chip.Add(text);

            return chip;
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

            // スケルトンボーン判定用のセットを構築
            var skinnedBones = BoneMapper.CollectSkinnedBones(_detection.SourceAvatar);

            var boneRefs = new List<SerializedBoneReference>();
            ScanComponentReferences<VRCPhysBone>(definitionRoot, sourceArmature, humanoidBoneMap, skinnedBones, boneRefs);
            ScanComponentReferences<VRCPhysBoneCollider>(definitionRoot, sourceArmature, humanoidBoneMap, skinnedBones, boneRefs);
            ScanComponentReferences<VRCConstraintBase>(definitionRoot, sourceArmature, humanoidBoneMap, skinnedBones, boneRefs);
            ScanComponentReferences<ContactBase>(definitionRoot, sourceArmature, humanoidBoneMap, skinnedBones, boneRefs);

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
                element.FindPropertyRelative("isSkeletonBone").boolValue = br.isSkeletonBone;
            }

            float sourceScale = PBRemapper.CalculateAvatarScale(sourceData);
            _sourceAvatarScaleProp.floatValue = sourceScale;

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private void ScanComponentReferences<T>(
            Transform definitionRoot,
            Transform sourceArmature,
            Dictionary<Transform, HumanBodyBones> humanoidBoneMap,
            HashSet<Transform> skinnedBones,
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

                    boneRef.isSkeletonBone = BoneMapper.IsSkeletonBone(objRef, skinnedBones);

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

        private void UpdateRuleHint(Label hintLabel, PathRemapRule.RemapMode mode)
        {
            switch (mode)
            {
                case PathRemapRule.RemapMode.PrefixReplace:
                    hintLabel.text = _strings.HintPrefixReplace;
                    break;
                case PathRemapRule.RemapMode.CharacterSubstitution:
                    hintLabel.text = _strings.HintCharSubstitution;
                    break;
                case PathRemapRule.RemapMode.RegexReplace:
                    hintLabel.text = _strings.HintRegexReplace;
                    break;
            }
        }

        private void UpdateFieldTooltips(
            TextField sourceField, TextField destField, PathRemapRule.RemapMode mode)
        {
            switch (mode)
            {
                case PathRemapRule.RemapMode.PrefixReplace:
                    sourceField.tooltip = _strings.TooltipPrefixSource;
                    destField.tooltip = _strings.TooltipPrefixDest;
                    break;
                case PathRemapRule.RemapMode.CharacterSubstitution:
                    sourceField.tooltip = _strings.TooltipCharSource;
                    destField.tooltip = _strings.TooltipCharDest;
                    break;
                case PathRemapRule.RemapMode.RegexReplace:
                    sourceField.tooltip = _strings.TooltipRegexSource;
                    destField.tooltip = _strings.TooltipRegexDest;
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
            {
                PBRemapPreviewWindow.Open(definition, _detection);

                // SceneViewプレビューを有効化（Live Modeのみ）
                if (_detection.IsLiveMode)
                {
                    var previewData = PBRemapPreview.GeneratePreview(definition, _detection);
                    PBRemapScenePreviewState.Instance.Activate(previewData, _detection);
                }
            }
        }

        private void OnRemapClicked()
        {
            serializedObject.Update();
            var definition = (PBRemap)target;

            var settings = PBReplacerSettings.Load();
            if (settings.ShowConfirmDialog)
            {
                string sourceName = _detection?.IsLiveMode == true
                    ? _detection.SourceAvatar?.name ?? "(不明)"
                    : "(Prefab)";
                string destName = _detection?.DestinationAvatar?.name ?? "(不明)";

                bool confirmed = EditorUtility.DisplayDialog(
                    _strings.DialogTitle,
                    string.Format(_strings.DialogConfirmTemplate, sourceName, destName),
                    _strings.DialogOk, _strings.DialogCancel);

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

                    if (success.AutoCreatedObjectCount > 0)
                        message += $"\n自動作成オブジェクト: {success.AutoCreatedObjectCount}";

                    if (success.UnresolvedReferenceCount > 0)
                        message += $"\n未解決参照: {success.UnresolvedReferenceCount}";

                    if (success.Warnings.Count > 0)
                        message += $"\n\n警告 ({success.Warnings.Count}):\n" +
                                   string.Join("\n", success.Warnings);

                    EditorUtility.DisplayDialog(
                        _strings.DialogCompleteTitle, message, _strings.DialogCompleteOk);

                    _statusBox.text = $"移植完了: {success.RemappedReferenceCount} 参照をリマップ" +
                        (success.AutoCreatedObjectCount > 0
                            ? $", {success.AutoCreatedObjectCount} オブジェクトを自動作成"
                            : "");
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
                        _calculatedScaleLabel.text = _strings.ScaleNoSourceScale;
                    }
                }
                else
                {
                    _calculatedScaleLabel.text = _strings.ScaleUnavailable;
                }
            }
            catch
            {
                _calculatedScaleLabel.text = _strings.ScaleUnavailable;
            }
        }

        #endregion
    }
}
