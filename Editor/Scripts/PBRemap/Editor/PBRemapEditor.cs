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
    /// <summary>
    /// PBRemap の Inspector。
    /// 状態カード（移植元→移植先）／解決サマリ／ボーン対応テーブル（手動マッピング）／スケール／アクション／詳細設定。
    /// 参照情報（マニフェスト）は <see cref="PBRemapTracker"/> により Inspector を開かなくても更新されるが、
    /// ここでも表示のたびに最新化する。
    /// </summary>
    [CustomEditor(typeof(PBRemap))]
    public class PBRemapEditor : Editor
    {
        private VisualElement _root;

        private Label _sourceAvatarLabel;
        private Label _destAvatarLabel;
        private Label _modeLabel;
        private Label _sourceBadge;
        private Label _destBadge;
        private HelpBox _stateBox;
        private HelpBox _detectionWarningBox;

        private ObjectField _sourceRootOverrideField;
        private ObjectField _destRootOverrideField;

        private Label _componentsSummary;
        private VisualElement _resolutionSummary;
        private Foldout _mappingFoldout;
        private VisualElement _mappingTable;
        private Button _clearManualButton;

        private ListView _rulesListView;
        private bool _showRuleHints;

        private EnumField _scaleModeField;
        private VisualElement _manualScaleContainer;
        private Label _calculatedScaleLabel;
        private Label _manifestInfo;

        private Button _refreshButton;
        private Button _previewButton;
        private Button _remapButton;
        private HelpBox _statusBox;

        private SerializedProperty _pathRemapRulesProp;
        private SerializedProperty _mappingOverridesProp;

        private SourceDetector.DetectionResult _detection;
        private PBRemapPreviewData _preview;
        private bool _refreshQueued;

        private StringResources _strings;

        private struct StringResources
        {
            public string DetectSourcePrefix, DetectDestPrefix, DetectSourceUndetected, DetectDestUndetected, DetectSourceError, DetectDestError, DetectSourceTransplanted, DetectSourcePrefab;
            public string ModeLive, ModePrefab, ModeHome, ModeNone;
            public string StateNoDest, StateTransplanted, StateNoSource, StateBrokenNoManifest, StateReady;
            public string BadgeMergeArmatureText, BadgeMergeArmatureTooltip, BadgePrefabText, BadgePrefabTooltip, BadgeAnimatorText, BadgeAnimatorTooltip, BadgeRootText, BadgeRootTooltip, BadgeNonHumanoidText, BadgeNonHumanoidTooltip;
            public string ScaleUnavailable, ScaleNoSourceScale;
            public string HintPrefixReplace, HintCharSubstitution, HintRegexReplace;
            public string TooltipPrefixSource, TooltipPrefixDest, TooltipCharSource, TooltipCharDest, TooltipRegexSource, TooltipRegexDest;
            public string DialogTitle, DialogConfirmTemplate, DialogOk, DialogCancel, DialogCompleteTitle, DialogCompleteOk;
        }

        private void LoadStringResources()
        {
            string Text(string name) => _root.Q<Label>(name)?.text ?? "";
            string Tooltip(string name) => _root.Q<Label>(name)?.tooltip ?? "";
            _strings = new StringResources
            {
                DetectSourcePrefix = Text("str-detect-source-prefix"), DetectDestPrefix = Text("str-detect-dest-prefix"),
                DetectSourceUndetected = Text("str-detect-source-undetected"), DetectDestUndetected = Text("str-detect-dest-undetected"),
                DetectSourceError = Text("str-detect-source-error"), DetectDestError = Text("str-detect-dest-error"),
                DetectSourceTransplanted = Text("str-detect-source-transplanted"), DetectSourcePrefab = Text("str-detect-source-prefab"),
                ModeLive = Text("str-mode-live"), ModePrefab = Text("str-mode-prefab"), ModeHome = Text("str-mode-home"), ModeNone = Text("str-mode-none"),
                StateNoDest = Text("str-state-no-dest"), StateTransplanted = Text("str-state-transplanted"), StateNoSource = Text("str-state-no-source"),
                StateBrokenNoManifest = Text("str-state-broken-no-manifest"), StateReady = Text("str-state-ready"),
                BadgeMergeArmatureText = Text("str-badge-merge-armature"), BadgeMergeArmatureTooltip = Tooltip("str-badge-merge-armature"),
                BadgePrefabText = Text("str-badge-prefab"), BadgePrefabTooltip = Tooltip("str-badge-prefab"),
                BadgeAnimatorText = Text("str-badge-animator"), BadgeAnimatorTooltip = Tooltip("str-badge-animator"),
                BadgeRootText = Text("str-badge-root"), BadgeRootTooltip = Tooltip("str-badge-root"),
                BadgeNonHumanoidText = Text("str-badge-non-humanoid"), BadgeNonHumanoidTooltip = Tooltip("str-badge-non-humanoid"),
                ScaleUnavailable = Text("str-scale-unavailable"), ScaleNoSourceScale = Text("str-scale-no-source-scale"),
                HintPrefixReplace = Text("str-hint-prefix-replace"), HintCharSubstitution = Text("str-hint-char-substitution"), HintRegexReplace = Text("str-hint-regex-replace"),
                TooltipPrefixSource = Text("str-tooltip-prefix-source"), TooltipPrefixDest = Text("str-tooltip-prefix-dest"),
                TooltipCharSource = Text("str-tooltip-char-source"), TooltipCharDest = Text("str-tooltip-char-dest"),
                TooltipRegexSource = Text("str-tooltip-regex-source"), TooltipRegexDest = Text("str-tooltip-regex-dest"),
                DialogTitle = Text("str-dialog-title"), DialogConfirmTemplate = Text("str-dialog-confirm-template"),
                DialogOk = Text("str-dialog-ok"), DialogCancel = Text("str-dialog-cancel"),
                DialogCompleteTitle = Text("str-dialog-complete-title"), DialogCompleteOk = Text("str-dialog-complete-ok"),
            };
        }

        public override VisualElement CreateInspectorGUI()
        {
            _root = new VisualElement();
            var definition = (PBRemap)target;
            PBRemapper.MigrateLegacyIfNeeded(definition);
            serializedObject.Update();

            _pathRemapRulesProp = serializedObject.FindProperty("pathRemapRules");
            _mappingOverridesProp = serializedObject.FindProperty("mappingOverrides");

            var visualTree = Resources.Load<VisualTreeAsset>("UXML/PBRemap");
            if (visualTree == null)
            {
                _root.Add(new HelpBox("PBRemap.uxml が見つかりません", HelpBoxMessageType.Error));
                return _root;
            }
            visualTree.CloneTree(_root);
            LoadStringResources();
            var styleSheet = Resources.Load<StyleSheet>("USS/PBRemap");
            if (styleSheet != null) _root.styleSheets.Add(styleSheet);

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
            _mappingFoldout = _root.Q<Foldout>("mapping-foldout");
            _mappingTable = _root.Q<VisualElement>("mapping-table");
            _clearManualButton = _root.Q<Button>("clear-manual-button");
            _rulesListView = _root.Q<ListView>("remap-rules-list");
            _scaleModeField = _root.Q<EnumField>("scale-mode-field");
            _manualScaleContainer = _root.Q<VisualElement>("manual-scale-container");
            _calculatedScaleLabel = _root.Q<Label>("calculated-scale-label");
            _manifestInfo = _root.Q<Label>("manifest-info");
            _refreshButton = _root.Q<Button>("refresh-manifest-button");
            _previewButton = _root.Q<Button>("preview-button");
            _remapButton = _root.Q<Button>("pbremap-button");
            _statusBox = _root.Q<HelpBox>("status-box");
            var addRuleButton = _root.Q<Button>("add-rule-button");

            _root.Bind(serializedObject);
            SetupRemapRulesListView();

            if (_sourceRootOverrideField != null)
            {
                _sourceRootOverrideField.objectType = typeof(GameObject);
                _sourceRootOverrideField.RegisterValueChangedCallback(_ => QueueRefresh());
            }
            if (_destRootOverrideField != null)
            {
                _destRootOverrideField.objectType = typeof(GameObject);
                _destRootOverrideField.RegisterValueChangedCallback(_ => QueueRefresh());
            }
            _scaleModeField?.RegisterValueChangedCallback(evt => { UpdateScaleVisibility(); QueueRefresh(); });
            if (addRuleButton != null) addRuleButton.clicked += OnAddRuleClicked;
            if (_refreshButton != null) _refreshButton.clicked += OnRefreshManifestClicked;
            if (_remapButton != null) _remapButton.clicked += OnRemapClicked;
            if (_previewButton != null) _previewButton.clicked += OnPreviewClicked;
            if (_clearManualButton != null) _clearManualButton.clicked += OnClearManualClicked;

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

            UpdateScaleVisibility();
            RefreshAll();

            // ルール/スケール/手動指定などの変更で再評価
            _root.TrackSerializedObjectValue(serializedObject, _ => QueueRefresh());
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            return _root;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (FindPreviewWindow() == null)
                PBRemapScenePreviewState.Instance.Deactivate();
        }

        private void OnHierarchyChanged() => QueueRefresh();
        private void OnUndoRedo() => QueueRefresh();

        private void QueueRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                _refreshQueued = false;
                if (target == null || _root == null) return;
                RefreshAll();
            };
        }

        #region refresh

        private void RefreshAll()
        {
            var definition = (PBRemap)target;
            if (definition == null) return;

            // 移植元にいる間は参照情報を最新化（Undoなし: 派生データ）
            PBRemapper.RefreshManifestIfLive(definition);

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
            var s = _detection.Situation;

            // 移植元/移植先ラベル
            switch (s.State)
            {
                case PBRemapState.AtHome:
                    _sourceAvatarLabel.text = _strings.DetectSourceTransplanted;
                    _modeLabel.text = _strings.ModeHome;
                    break;
                case PBRemapState.Displaced:
                    _sourceAvatarLabel.text = _strings.DetectSourcePrefix + (s.SourceRoot != null ? s.SourceRoot.name : "?");
                    _modeLabel.text = _strings.ModeLive;
                    break;
                case PBRemapState.Broken:
                    _sourceAvatarLabel.text = s.HasManifest ? string.Format(_strings.DetectSourcePrefab, definition.Manifest.sourceRootName) : _strings.DetectSourceUndetected;
                    _modeLabel.text = s.HasManifest ? _strings.ModePrefab : "";
                    break;
                default:
                    _sourceAvatarLabel.text = s.HasManifest && s.State == PBRemapState.NoDestination
                        ? string.Format(_strings.DetectSourcePrefab, definition.Manifest.sourceRootName)
                        : _strings.DetectSourceUndetected;
                    _modeLabel.text = s.State == PBRemapState.NoReferences ? _strings.ModeNone : "";
                    break;
            }
            _destAvatarLabel.text = s.DestinationRoot != null ? _strings.DetectDestPrefix + s.DestinationRoot.name : _strings.DetectDestUndetected;

            UpdateBadges(s);
            UpdateStateBox(s, definition);
            UpdateComponentsSummary(definition);

            // プレビュー（解決計画）
            _preview = null;
            bool canPlan = s.CanResolve;
            if (canPlan)
            {
                _preview = PBRemapPreview.GeneratePreview(definition, _detection);
            }
            UpdateResolutionSummary();
            UpdateMappingTable(definition);
            UpdateScaleLabel();
            UpdateManifestInfo(definition);

            var allWarnings = new List<string>(_detection.Warnings);
            if (definition.transform.parent != null && definition.transform.parent.GetComponentInParent<PBRemap>(true) != null)
                allWarnings.Add("このPBRemapは別のPBRemapの配下にあります。外側のPBRemapが一括で扱うため、このコンポーネントは無視されます。");
            if (definition.GetComponentsInChildren<PBRemap>(true).Any(x => x != definition))
                allWarnings.Add("配下に別のPBRemapがあります。その配下のコンポーネントはこのPBRemapの対象外です。");
            if (PrefabUtility.IsPartOfPrefabInstance(definition) && definition.Manifest != null && !definition.Manifest.IsEmpty)
                allWarnings.Add("参照情報はPrefabインスタンスのオーバーライドとして保存されています（Revert All Overrides で失われます。持ち出す場合は Apply するか Prefab を作り直してください）。");
            if (_preview != null) { allWarnings.AddRange(_preview.Errors); allWarnings.AddRange(_preview.Warnings); }
            allWarnings = allWarnings.Distinct().ToList();
            if (allWarnings.Count > 0)
            {
                _detectionWarningBox.text = string.Join("\n", allWarnings);
                _detectionWarningBox.messageType = _preview != null && _preview.Errors.Count > 0 ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
                _detectionWarningBox.style.display = DisplayStyle.Flex;
            }
            else _detectionWarningBox.style.display = DisplayStyle.None;

            bool canApply = _preview != null && _preview.Plan != null && _preview.Plan.CanApply;
            _remapButton.SetEnabled(canApply);
            _previewButton.SetEnabled(canPlan);
            _refreshButton.SetEnabled(s.State == PBRemapState.AtHome || s.State == PBRemapState.Displaced);

            var previewWindow = FindPreviewWindow();
            if (previewWindow != null) previewWindow.UpdateDetection(_detection);
            if (PBRemapScenePreviewState.Instance.IsActive && _detection.IsLiveMode && _preview != null)
                PBRemapScenePreviewState.Instance.Activate(_preview, _detection);
        }

        private void UpdateStateBox(PBRemapSituation s, PBRemap definition)
        {
            string text = null;
            var type = HelpBoxMessageType.Info;
            switch (s.State)
            {
                case PBRemapState.NoDestination: text = _strings.StateNoDest; type = HelpBoxMessageType.Warning; break;
                case PBRemapState.AtHome: text = _strings.StateTransplanted; break;
                case PBRemapState.NoReferences: text = _strings.StateNoSource; break;
                case PBRemapState.Broken: if (!s.HasManifest) { text = _strings.StateBrokenNoManifest; type = HelpBoxMessageType.Error; } else text = _strings.StateReady; break;
                case PBRemapState.Displaced: text = _strings.StateReady; break;
            }
            if (definition.Applied != null && definition.Applied.isApplied && s.State == PBRemapState.AtHome)
                text += $"\n（適用済み: {definition.Applied.sourceRootName} → {definition.Applied.destinationRootName}, スケール x{definition.Applied.worldScaleRatio:F3}）";
            if (text == null) _stateBox.style.display = DisplayStyle.None;
            else { _stateBox.text = text; _stateBox.messageType = type; _stateBox.style.display = DisplayStyle.Flex; }
        }

        private void UpdateBadges(PBRemapSituation s)
        {
            SetBadge(_sourceBadge, s.Source, _detection.SourceAvatarData);
            SetBadge(_destBadge, s.Destination, _detection.DestAvatarData);
        }

        private void SetBadge(Label badge, RootInfo info, AvatarData data)
        {
            if (badge == null) return;
            var tags = new List<string>(); var tips = new List<string>();
            if (info != null && info.Root != null)
            {
                switch (info.Method)
                {
                    case AvatarDetectionMethod.MergeArmature: tags.Add(_strings.BadgeMergeArmatureText); tips.Add(_strings.BadgeMergeArmatureTooltip); break;
                    case AvatarDetectionMethod.PrefabBoundary: tags.Add(_strings.BadgePrefabText); tips.Add(_strings.BadgePrefabTooltip); break;
                    case AvatarDetectionMethod.Animator: tags.Add(_strings.BadgeAnimatorText); tips.Add(_strings.BadgeAnimatorTooltip); break;
                    case AvatarDetectionMethod.Root: tags.Add(_strings.BadgeRootText); tips.Add(_strings.BadgeRootTooltip); break;
                    case AvatarDetectionMethod.Manual: tags.Add("手動"); tips.Add("詳細設定 > 手動指定で指定されています。"); break;
                }
                if (data != null && (data.AvatarAnimator == null || !data.AvatarAnimator.isHuman) && info.Kind != RootKind.MACostume)
                { tags.Add(_strings.BadgeNonHumanoidText); tips.Add(_strings.BadgeNonHumanoidTooltip); }
            }
            if (tags.Count > 0) { badge.text = string.Join(" / ", tags); badge.tooltip = string.Join("\n\n", tips); badge.style.display = DisplayStyle.Flex; }
            else badge.style.display = DisplayStyle.None;
        }

        private void UpdateComponentsSummary(PBRemap definition)
        {
            var t = definition.transform;
            int pb = t.GetComponentsInChildren<VRCPhysBone>(true).Length;
            int pbc = t.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
            int cs = t.GetComponentsInChildren<VRCConstraintBase>(true).Length;
            int ct = t.GetComponentsInChildren<ContactBase>(true).Length;
            _componentsSummary.text = $"PhysBone: {pb}  PhysBoneCollider: {pbc}  Constraint: {cs}  Contact: {ct}  (合計: {pb + pbc + cs + ct})";
        }

        private void UpdateResolutionSummary()
        {
            if (_preview == null || _preview.Plan == null || _preview.Plan.Resolutions.Count == 0)
            {
                _resolutionSummary.style.display = DisplayStyle.None;
                return;
            }
            var plan = _preview.Plan;
            int total = plan.Resolutions.Count;
            int ext = plan.CountOf(ResolutionStatus.ExternalObject);
            int unresolved = plan.CountOf(ResolutionStatus.Unresolved);
            _resolutionSummary.Clear();
            var header = new Label($"参照解決: {plan.ResolvedCount + plan.AutoCreateCount}/{total}");
            header.AddToClassList("pbremap-resolution-header");
            _resolutionSummary.Add(header);
            _resolutionSummary.Add(CreateResolutionChip(plan.ResolvedCount, "解決済み", "resolved"));
            if (plan.AutoCreateCount > 0) _resolutionSummary.Add(CreateResolutionChip(plan.AutoCreateCount, "作成予定", "auto-creatable"));
            if (plan.AmbiguousCount > 0) _resolutionSummary.Add(CreateResolutionChip(plan.AmbiguousCount, "要選択", "unresolved"));
            if (unresolved > 0) _resolutionSummary.Add(CreateResolutionChip(unresolved, "未解決", "unresolved"));
            if (ext > 0) _resolutionSummary.Add(CreateResolutionChip(ext, "外部参照", "unresolved"));
            _resolutionSummary.style.display = DisplayStyle.Flex;
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

        /// <summary>
        /// ボーン対応テーブル。移植元ボーン単位で1行。未解決/曖昧/自動作成の行には手動マッピング用のObjectFieldを出す。
        /// </summary>
        private void UpdateMappingTable(PBRemap definition)
        {
            _mappingTable.Clear();
            if (_preview == null || _preview.Plan == null || _preview.Plan.Resolutions.Count == 0)
            {
                _mappingFoldout.style.display = DisplayStyle.None;
                return;
            }
            _mappingFoldout.style.display = DisplayStyle.Flex;
            var plan = _preview.Plan;
            var groups = plan.Resolutions.GroupBy(r => r.SourceKey).ToList();
            bool anyManual = definition.MappingOverrides != null && definition.MappingOverrides.Count > 0;
            _clearManualButton.style.display = anyManual ? DisplayStyle.Flex : DisplayStyle.None;

            foreach (var g in groups)
            {
                var res = g.First();
                var row = new VisualElement();
                row.AddToClassList("preview-bone-item");
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.flexWrap = Wrap.Wrap;
                row.style.marginBottom = 2;

                string dotClass;
                switch (res.Status)
                {
                    case ResolutionStatus.Resolved: case ResolutionStatus.Manual: dotClass = "resolved"; break;
                    case ResolutionStatus.AutoCreate: dotClass = "auto-creatable"; break;
                    default: dotClass = "unresolved"; break;
                }
                var dot = new VisualElement();
                dot.AddToClassList("pbremap-resolution-dot");
                dot.AddToClassList($"pbremap-resolution-dot-{dotClass}");
                row.Add(dot);

                var src = new Label(res.SourceDisplayPath);
                src.AddToClassList("preview-bone-sourcelabel");
                src.style.flexGrow = 1; src.style.flexBasis = 0; src.style.overflow = Overflow.Hidden;
                src.tooltip = "参照: " + string.Join("\n", g.Select(x => x.Ref.componentPath + "." + x.Ref.propertyPath));
                row.Add(src);

                var arrow = new Label("→");
                arrow.AddToClassList("preview-bone-arrow");
                row.Add(arrow);

                var destField = new ObjectField { objectType = typeof(Transform), allowSceneObjects = true };
                destField.style.flexGrow = 1; destField.style.flexBasis = 0; destField.style.minWidth = 120; destField.style.flexShrink = 1;
                destField.SetValueWithoutNotify(res.Target);
                string tip;
                switch (res.Status)
                {
                    case ResolutionStatus.Resolved: tip = $"解決方法: {res.Method}"; break;
                    case ResolutionStatus.Manual: tip = "手動マッピング"; break;
                    case ResolutionStatus.AutoCreate: tip = res.Message + "\n（Transformを指定すると自動作成の代わりにそれを使います）"; break;
                    case ResolutionStatus.Ambiguous: tip = res.Message; break;
                    default: tip = res.Message + "\n（Transformをドロップして手動で対応付けできます）"; break;
                }
                destField.tooltip = tip;
                var key = res.SourceKey;
                var displayPath = res.SourceDisplayPath;
                destField.RegisterValueChangedCallback(evt => SetManualMapping(definition, key, displayPath, evt.newValue as Transform));
                row.Add(destField);

                if (res.Status == ResolutionStatus.AutoCreate)
                {
                    var note = new Label("作成予定");
                    note.AddToClassList("preview-bone-auto-creatable");
                    note.tooltip = res.Message;
                    note.style.flexShrink = 0;
                    row.Add(note);
                }
                else if (res.Status == ResolutionStatus.Ambiguous)
                {
                    var pick = new Button { text = "候補…" };
                    pick.tooltip = res.Message;
                    var candidates = res.Candidates;
                    pick.clicked += () =>
                    {
                        var menu = new GenericMenu();
                        foreach (var c in candidates)
                        {
                            var cc = c;
                            menu.AddItem(new GUIContent(BoneMapper.GetRelativePath(cc, plan.DestinationRoot.transform) ?? cc.name), false,
                                () => SetManualMapping(definition, key, displayPath, cc));
                        }
                        menu.ShowAsContext();
                    };
                    row.Add(pick);
                }
                else if (res.Status == ResolutionStatus.Unresolved || res.Status == ResolutionStatus.ExternalObject)
                {
                    var note = new Label(res.Status == ResolutionStatus.ExternalObject ? "外部" : "未解決");
                    note.AddToClassList("preview-bone-unresolved");
                    note.tooltip = res.Message;
                    note.style.flexShrink = 0;
                    row.Add(note);
                }
                _mappingTable.Add(row);
            }
        }

        private void SetManualMapping(PBRemap definition, string sourceKey, string sourcePath, Transform targetTransform)
        {
            serializedObject.Update();
            int found = -1;
            for (int i = 0; i < _mappingOverridesProp.arraySize; i++)
            {
                if (_mappingOverridesProp.GetArrayElementAtIndex(i).FindPropertyRelative("sourceKey").stringValue == sourceKey) { found = i; break; }
            }
            if (targetTransform == null)
            {
                if (found >= 0) _mappingOverridesProp.DeleteArrayElementAtIndex(found);
            }
            else
            {
                if (found < 0) { found = _mappingOverridesProp.arraySize; _mappingOverridesProp.arraySize = found + 1; }
                var el = _mappingOverridesProp.GetArrayElementAtIndex(found);
                el.FindPropertyRelative("sourceKey").stringValue = sourceKey;
                el.FindPropertyRelative("sourcePath").stringValue = sourcePath;
                el.FindPropertyRelative("target").objectReferenceValue = targetTransform;
                var destRoot = _detection?.DestinationAvatar;
                el.FindPropertyRelative("targetPathFromRoot").stringValue = destRoot != null ? (BoneMapper.GetRelativePath(targetTransform, destRoot.transform) ?? "") : "";
            }
            serializedObject.ApplyModifiedProperties();
            QueueRefresh();
        }

        private void OnClearManualClicked()
        {
            serializedObject.Update();
            _mappingOverridesProp.ClearArray();
            serializedObject.ApplyModifiedProperties();
            QueueRefresh();
        }

        private void UpdateScaleVisibility()
        {
            var definition = (PBRemap)target;
            _manualScaleContainer.style.display = definition.ScaleMode == PBRemapScaleMode.Manual ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateScaleLabel()
        {
            if (_preview == null || _preview.Plan == null)
            {
                _calculatedScaleLabel.text = "";
                return;
            }
            var plan = _preview.Plan;
            _calculatedScaleLabel.text = $"世界寸法比: x{plan.WorldScaleRatio:F4} ({plan.ScaleMethod})  ※ radius等は「元値 × 比 × 移植元/移植先ボーンのlossyScale比」で適用";
        }

        private void UpdateManifestInfo(PBRemap definition)
        {
            if (_manifestInfo == null) return;
            var m = definition.Manifest;
            if (m == null || m.IsEmpty) { _manifestInfo.text = "参照情報なし"; return; }
            var ctxs = string.Join(", ", m.contexts.Where(c => c.id != 0).Select(c => $"{c.kind}:{c.armatureName}{(c.isHumanoid ? "(Humanoid)" : "")}{(string.IsNullOrEmpty(c.maPrefix + c.maSuffix) ? "" : $"[{c.maPrefix}*{c.maSuffix}]")}"));
            _manifestInfo.text = $"移植元: {m.sourceRootName} ({m.sourceRootKind})\n取得: {m.capturedAtUtc}\n参照: {m.refs.Count} 件 / コンテキスト: {ctxs}\nHips-Head: {m.scaleReference.hipsToHead:F4}";
        }

        #endregion

        #region rules listview

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
            var row = new VisualElement { name = "rule-row" };
            row.AddToClassList("remap-rule-item");
            var enabledToggle = new Toggle { name = "rule-enabled" };
            row.Add(enabledToggle);
            var modeField = new EnumField(PathRemapRule.RemapMode.CharacterSubstitution) { name = "rule-mode" };
            row.Add(modeField);
            var sourcePatternField = new TextField { name = "rule-source-pattern" };
            row.Add(sourcePatternField);
            var arrowLabel = new Label("↔");
            arrowLabel.AddToClassList("remap-rule-arrow");
            row.Add(arrowLabel);
            var destPatternField = new TextField { name = "rule-dest-pattern" };
            row.Add(destPatternField);
            var deleteButton = new Button { name = "rule-delete", text = "✕" };
            deleteButton.AddToClassList("remap-rule-delete-button");
            row.Add(deleteButton);
            root.Add(row);
            var hintLabel = new Label { name = "rule-hint" };
            hintLabel.AddToClassList("remap-rule-hint");
            root.Add(hintLabel);
            var errorLabel = new Label { name = "rule-error" };
            errorLabel.AddToClassList("remap-rule-error-label");
            errorLabel.style.display = DisplayStyle.None;
            root.Add(errorLabel);

            modeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is PathRemapRule.RemapMode mode)
                {
                    UpdateRuleHint(hintLabel, mode);
                    UpdateFieldTooltips(sourcePatternField, destPatternField, mode);
                    UpdateRuleErrorDisplay(row, errorLabel, mode, sourcePatternField.value);
                }
            });
            sourcePatternField.RegisterValueChangedCallback(evt =>
            {
                var mode = modeField.value is PathRemapRule.RemapMode m ? m : PathRemapRule.RemapMode.CharacterSubstitution;
                UpdateRuleErrorDisplay(row, errorLabel, mode, evt.newValue);
            });
            return root;
        }

        private void BindRuleItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _pathRemapRulesProp.arraySize) return;
            var ruleProp = _pathRemapRulesProp.GetArrayElementAtIndex(index);
            var enabledProp = ruleProp.FindPropertyRelative("enabled");
            var modeProp = ruleProp.FindPropertyRelative("mode");
            var sourcePatternProp = ruleProp.FindPropertyRelative("sourcePattern");
            var destPatternProp = ruleProp.FindPropertyRelative("destinationPattern");

            var ruleRow = element.Q<VisualElement>("rule-row");
            var enabledToggle = element.Q<Toggle>("rule-enabled");
            var modeField = element.Q<EnumField>("rule-mode");
            var sourcePatternField = element.Q<TextField>("rule-source-pattern");
            var destPatternField = element.Q<TextField>("rule-dest-pattern");
            var deleteButton = element.Q<Button>("rule-delete");
            var hintLabel = element.Q<Label>("rule-hint");
            var errorLabel = element.Q<Label>("rule-error");

            enabledToggle.BindProperty(enabledProp);
            modeField.BindProperty(modeProp);
            sourcePatternField.BindProperty(sourcePatternProp);
            destPatternField.BindProperty(destPatternProp);
            deleteButton.clickable = new Clickable(() => OnDeleteRuleClicked(index));

            var mode = (PathRemapRule.RemapMode)modeProp.enumValueIndex;
            UpdateRuleHint(hintLabel, mode);
            UpdateFieldTooltips(sourcePatternField, destPatternField, mode);
            hintLabel.style.display = _showRuleHints ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateRuleErrorDisplay(ruleRow, errorLabel, mode, sourcePatternProp.stringValue);
        }

        private void UpdateRuleErrorDisplay(VisualElement ruleRow, Label errorLabel, PathRemapRule.RemapMode mode, string sourcePattern)
        {
            if (ruleRow == null || errorLabel == null) return;
            var tempRule = new PathRemapRule { mode = mode, sourcePattern = sourcePattern };
            bool isValid = tempRule.TryValidate(out string errorMessage);
            ruleRow.EnableInClassList("remap-rule-error", !isValid);
            if (isValid) { errorLabel.style.display = DisplayStyle.None; errorLabel.text = ""; errorLabel.tooltip = ""; }
            else { errorLabel.style.display = DisplayStyle.Flex; errorLabel.text = errorMessage; errorLabel.tooltip = errorMessage; }
        }

        private void UpdateRuleHint(Label hintLabel, PathRemapRule.RemapMode mode)
        {
            switch (mode)
            {
                case PathRemapRule.RemapMode.PrefixReplace: hintLabel.text = _strings.HintPrefixReplace; break;
                case PathRemapRule.RemapMode.CharacterSubstitution: hintLabel.text = _strings.HintCharSubstitution; break;
                case PathRemapRule.RemapMode.RegexReplace: hintLabel.text = _strings.HintRegexReplace; break;
            }
        }

        private void UpdateFieldTooltips(TextField sourceField, TextField destField, PathRemapRule.RemapMode mode)
        {
            switch (mode)
            {
                case PathRemapRule.RemapMode.PrefixReplace: sourceField.tooltip = _strings.TooltipPrefixSource; destField.tooltip = _strings.TooltipPrefixDest; break;
                case PathRemapRule.RemapMode.CharacterSubstitution: sourceField.tooltip = _strings.TooltipCharSource; destField.tooltip = _strings.TooltipCharDest; break;
                case PathRemapRule.RemapMode.RegexReplace: sourceField.tooltip = _strings.TooltipRegexSource; destField.tooltip = _strings.TooltipRegexDest; break;
            }
        }

        private void OnAddRuleClicked()
        {
            serializedObject.Update();
            int newIndex = _pathRemapRulesProp.arraySize;
            _pathRemapRulesProp.arraySize = newIndex + 1;
            var newRule = _pathRemapRulesProp.GetArrayElementAtIndex(newIndex);
            newRule.FindPropertyRelative("mode").enumValueIndex = (int)PathRemapRule.RemapMode.CharacterSubstitution;
            newRule.FindPropertyRelative("sourcePattern").stringValue = "";
            newRule.FindPropertyRelative("destinationPattern").stringValue = "";
            newRule.FindPropertyRelative("enabled").boolValue = true;
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

        #endregion

        #region actions

        private void OnRefreshManifestClicked()
        {
            var definition = (PBRemap)target;
            bool changed = PBRemapper.RefreshManifestIfLive(definition, null, registerUndo: true, force: true);
            _statusBox.text = changed ? $"参照情報を更新しました（{definition.Manifest.refs.Count} 件）" : "参照が生きていないため更新できません（移植元のシーンで実行してください）";
            _statusBox.messageType = HelpBoxMessageType.Info;
            _statusBox.style.display = DisplayStyle.Flex;
            QueueRefresh();
        }

        private void OnPreviewClicked()
        {
            var definition = (PBRemap)target;
            if (_detection == null) return;
            PBRemapPreviewWindow.Open(definition, _detection);
            if (_detection.IsLiveMode)
            {
                var previewData = PBRemapPreview.GeneratePreview(definition, _detection);
                PBRemapScenePreviewState.Instance.Activate(previewData, _detection);
            }
        }

        private void OnRemapClicked()
        {
            serializedObject.Update();
            var definition = (PBRemap)target;
            var settings = PBReplacerSettings.Load();
            if (settings.ShowConfirmDialog)
            {
                string sourceName = _detection?.Situation?.SourceRoot != null ? _detection.Situation.SourceRoot.name
                    : (definition.Manifest != null && !definition.Manifest.IsEmpty ? definition.Manifest.sourceRootName + " (参照情報)" : "(不明)");
                string destName = _detection?.DestinationAvatar?.name ?? "(不明)";
                if (!EditorUtility.DisplayDialog(_strings.DialogTitle, string.Format(_strings.DialogConfirmTemplate, sourceName, destName), _strings.DialogOk, _strings.DialogCancel))
                    return;
            }

            var result = PBRemapper.Remap(definition);
            result.Match(
                onSuccess: success =>
                {
                    string message = $"移植（リマップ）が完了しました\n\nリマップ済みコンポーネント: {success.RemappedComponentCount}\nリマップ済み参照: {success.RemappedReferenceCount}\nスケール: x{success.WorldScaleRatio:F3}";
                    if (success.AutoCreatedObjectCount > 0) message += $"\n自動作成オブジェクト: {success.AutoCreatedObjectCount}";
                    if (success.AmbiguousReferenceCount > 0) message += $"\n要選択（曖昧）: {success.AmbiguousReferenceCount}";
                    if (success.UnresolvedReferenceCount > 0) message += $"\n未解決参照: {success.UnresolvedReferenceCount}";
                    if (success.Warnings.Count > 0) message += $"\n\n警告 ({success.Warnings.Count}):\n" + string.Join("\n", success.Warnings.Take(12)) + (success.Warnings.Count > 12 ? "\n…" : "");
                    EditorUtility.DisplayDialog(_strings.DialogCompleteTitle, message, _strings.DialogCompleteOk);
                    _statusBox.text = $"移植完了: {success.RemappedReferenceCount} 参照をリマップ" + (success.AutoCreatedObjectCount > 0 ? $", {success.AutoCreatedObjectCount} オブジェクトを自動作成" : "")
                        + (success.UnresolvedReferenceCount + success.AmbiguousReferenceCount > 0 ? $", {success.UnresolvedReferenceCount + success.AmbiguousReferenceCount} 件は未解決" : "");
                    _statusBox.messageType = success.UnresolvedReferenceCount + success.AmbiguousReferenceCount > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
                    _statusBox.style.display = DisplayStyle.Flex;
                    QueueRefresh();
                },
                onFailure: error =>
                {
                    _statusBox.text = error;
                    _statusBox.messageType = HelpBoxMessageType.Error;
                    _statusBox.style.display = DisplayStyle.Flex;
                });
        }

        private static PBRemapPreviewWindow FindPreviewWindow()
        {
            var windows = Resources.FindObjectsOfTypeAll<PBRemapPreviewWindow>();
            return windows.Length > 0 ? windows[0] : null;
        }

        #endregion
    }
}
