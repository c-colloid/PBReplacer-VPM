using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace colloid.PBReplacer
{
    /// <summary>
    /// PBRemap の Inspector。
    ///
    /// 上から:
    ///   ツール行      … 参照情報の更新 / SceneView プレビュー / 詳細設定（アイコンのみ）
    ///   流れ（strip） … [移植元] ──(移植)──▶ [移植先]。左→右がドラッグの向き。真ん中の → ボタンで移植する。
    ///                    移植先ノードは Hierarchy からのドロップを受け付ける（= その配下へ移動）。
    ///   候補          … 移植先が認識できないとき、候補をチップで示す（クリックで移動）
    ///   警告          … 本当に注意が必要なときだけ
    ///   チップ        … Console と同じ「アイコン＋件数」のフィルタ（✔ 解決 / ＋ 自動作成 / ⚠ 要選択 / ✖ 未解決）とスケール
    ///   対応表        … 問題のある行だけを既定表示。ObjectField へボーンをドロップして手動対応
    ///   詳細設定      … 歯車で開閉（ドロップ時の動作 / スケール / ルール / 手動指定 / 参照情報）
    ///
    /// 説明文は置かず、意味はアイコン・色・向き・ツールチップで伝える。
    /// </summary>
    [CustomEditor(typeof(PBRemap))]
    public class PBRemapEditor : Editor
    {
        private const string PrefAdvanced = "PBReplacer.PBRemap.Advanced";

        private VisualElement _root;
        private VisualElement _tools;
        private Button _refreshButton, _eyeButton, _gearButton;

        private VisualElement _strip;
        private VisualElement _nodeSrc, _nodeDst;
        private Image _srcIcon, _srcBadge, _dstIcon, _dstBadge;
        private Label _srcName, _dstName, _srcSub, _dstSub;
        private VisualElement _lineLeft, _lineRight;
        private Button _applyButton;
        private Image _applyIcon, _connectorState;

        private VisualElement _candidates;
        private HelpBox _warningBox;
        private VisualElement _chips;
        private VisualElement _mappingTable;
        private HelpBox _statusBox;
        private VisualElement _advanced;

        private ListView _rulesListView;
        private bool _showRuleHints;
        private EnumField _scaleModeField;
        private FloatField _scaleFactorField;
        private Label _manifestInfo;

        private SerializedProperty _pathRemapRulesProp;
        private SerializedProperty _mappingOverridesProp;

        private SourceDetector.DetectionResult _detection;
        private PBRemapPreviewData _preview;
        private bool _refreshQueued;

        private StringResources _strings;

        private struct StringResources
        {
            public string HintPrefixReplace, HintCharSubstitution, HintRegexReplace;
            public string TooltipPrefixSource, TooltipPrefixDest, TooltipCharSource, TooltipCharDest, TooltipRegexSource, TooltipRegexDest;
            public string DialogTitle, DialogConfirmTemplate, DialogOk, DialogCancel;
        }

        private static PBRemapScenePreviewState Filter => PBRemapScenePreviewState.Instance;

        #region create

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
            var commonSheet = Resources.Load<StyleSheet>("USS/PBReplacerCommon");
            if (commonSheet != null) _root.styleSheets.Add(commonSheet);
            var styleSheet = Resources.Load<StyleSheet>("USS/PBRemap");
            if (styleSheet != null) _root.styleSheets.Add(styleSheet);
            PBReplacerFonts.Apply(_root);
            LoadStringResources();

            _tools = _root.Q<VisualElement>("tools");
            _strip = _root.Q<VisualElement>("strip");
            _nodeSrc = _root.Q<VisualElement>("node-src");
            _nodeDst = _root.Q<VisualElement>("node-dst");
            _srcIcon = _root.Q<Image>("node-src-icon");
            _srcBadge = _root.Q<Image>("node-src-badge");
            _dstIcon = _root.Q<Image>("node-dst-icon");
            _dstBadge = _root.Q<Image>("node-dst-badge");
            _srcName = _root.Q<Label>("node-src-name");
            _dstName = _root.Q<Label>("node-dst-name");
            _srcSub = _root.Q<Label>("node-src-sub");
            _dstSub = _root.Q<Label>("node-dst-sub");
            _lineLeft = _root.Q<VisualElement>("line-left");
            _lineRight = _root.Q<VisualElement>("line-right");
            _applyButton = _root.Q<Button>("apply-button");
            _applyIcon = _root.Q<Image>("apply-icon");
            _connectorState = _root.Q<Image>("connector-state");
            _candidates = _root.Q<VisualElement>("candidates");
            _warningBox = _root.Q<HelpBox>("warning-box");
            _chips = _root.Q<VisualElement>("chips");
            _mappingTable = _root.Q<VisualElement>("mapping-table");
            _statusBox = _root.Q<HelpBox>("status-box");
            _advanced = _root.Q<VisualElement>("advanced");
            _rulesListView = _root.Q<ListView>("remap-rules-list");
            _scaleModeField = _root.Q<EnumField>("scale-mode-field");
            _scaleFactorField = _root.Q<FloatField>("scale-factor-field");
            _manifestInfo = _root.Q<Label>("manifest-info");

            PBRemapIcons.Set(_applyIcon, PBRemapIcons.Apply);
            _applyButton.clicked += OnApplyClicked;

            // ツール行
            _refreshButton = PBRemapIcons.IconButton(PBRemapIcons.Refresh, "参照情報を取り直す（移植元にいるときだけ）", OnRefreshManifestClicked);
            _eyeButton = PBRemapIcons.IconButton(PBRemapIcons.EyeOff, "SceneView に対応線を表示", OnEyeClicked);
            _gearButton = PBRemapIcons.IconButton(PBRemapIcons.Settings, "詳細設定", OnGearClicked);
            _tools.Add(_refreshButton); _tools.Add(_eyeButton); _tools.Add(_gearButton);
            bool adv = EditorPrefs.GetBool(PrefAdvanced, false);
            _advanced.style.display = adv ? DisplayStyle.Flex : DisplayStyle.None;
            _gearButton.EnableInClassList("pbremap-icon-button--on", adv);

            // 移植先ノード: Hierarchy からのドロップ = その配下へ移動
            RegisterDropTarget(_nodeDst, definition);

            // 詳細設定
            _root.Bind(serializedObject);
            SetupRemapRulesListView();
            var addRuleButton = _root.Q<Button>("add-rule-button");
            if (addRuleButton != null) { addRuleButton.Add(PBRemapIcons.Image(PBRemapIcons.AutoCreate, 14)); addRuleButton.clicked += OnAddRuleClicked; }
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
            _root.Q<ObjectField>("source-root-override")?.RegisterValueChangedCallback(_ => QueueRefresh());
            _root.Q<ObjectField>("dest-root-override")?.RegisterValueChangedCallback(_ => QueueRefresh());
            var srcOv = _root.Q<ObjectField>("source-root-override"); if (srcOv != null) srcOv.objectType = typeof(GameObject);
            var dstOv = _root.Q<ObjectField>("dest-root-override"); if (dstOv != null) dstOv.objectType = typeof(GameObject);
            _scaleModeField?.RegisterValueChangedCallback(_ => { UpdateScaleFieldVisibility(); QueueRefresh(); });
            UpdateScaleFieldVisibility();

            RefreshAll();

            _root.TrackSerializedObjectValue(serializedObject, _ => QueueRefresh());
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            Filter.FilterStateChanged += OnFilterChanged;
            return _root;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Filter.FilterStateChanged -= OnFilterChanged;
            // SceneView プレビューは Inspector を閉じても残す（SceneView 上の操作で選択が変わるため）。
            // 閉じるのはオーバレイの「目を閉じる」か、対象が無くなったとき
        }

        private void LoadStringResources()
        {
            string Text(string name) => _root.Q<Label>(name)?.text ?? "";
            _strings = new StringResources
            {
                HintPrefixReplace = Text("str-hint-prefix-replace"), HintCharSubstitution = Text("str-hint-char-substitution"), HintRegexReplace = Text("str-hint-regex-replace"),
                TooltipPrefixSource = Text("str-tooltip-prefix-source"), TooltipPrefixDest = Text("str-tooltip-prefix-dest"),
                TooltipCharSource = Text("str-tooltip-char-source"), TooltipCharDest = Text("str-tooltip-char-dest"),
                TooltipRegexSource = Text("str-tooltip-regex-source"), TooltipRegexDest = Text("str-tooltip-regex-dest"),
                DialogTitle = Text("str-dialog-title"), DialogConfirmTemplate = Text("str-dialog-confirm-template"),
                DialogOk = Text("str-dialog-ok"), DialogCancel = Text("str-dialog-cancel"),
            };
        }

        private void OnHierarchyChanged() => QueueRefresh();
        private void OnUndoRedo() => QueueRefresh();
        private void OnFilterChanged() { if (_root != null) UpdateMappingTable((PBRemap)target); }

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

        #endregion

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
                ShowStatus(detectResult.Error, HelpBoxMessageType.Error);
                return;
            }
            _detection = detectResult.Value;
            var s = _detection.Situation;

            _preview = null;
            if (s.CanResolve) _preview = PBRemapPreview.GeneratePreview(definition, _detection);

            UpdateStrip(definition, s);
            UpdateCandidates(definition, s);
            UpdateChips(definition, s);
            UpdateMappingTable(definition);
            UpdateWarnings(definition, s);
            UpdateManifestInfo(definition);
            UpdateTools(s);

            var previewWindow = FindPreviewWindow();
            if (previewWindow != null) previewWindow.UpdateDetection(_detection);
            if (PBRemapScenePreviewState.Instance.IsActive && _detection.IsLiveMode && _preview != null && PBRemapScenePreviewState.Instance.Definition == definition)
                PBRemapScenePreviewState.Instance.Activate(_preview, _detection, definition);
        }

        /// <summary>ノードの名前（1行目: ホーム名 / 2行目: 外側の名前）</summary>
        private static void SetNodeText(Label name, Label sub, string main, string outer)
        {
            name.text = main ?? "";
            sub.text = outer ?? "";
            sub.style.display = string.IsNullOrEmpty(outer) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static void SetNodeText(Label name, Label sub, RootInfo info)
        {
            SetNodeText(name, sub, info != null && info.Root != null ? info.Root.name : "", info != null && info.Outer != null ? info.Outer.name : "");
        }

        private static void SetNodeText(Label name, Label sub, PBRemapManifest m)
        {
            SetNodeText(name, sub, m != null ? m.sourceRootName : "", m != null ? m.outerRootName : "");
        }

        /// <summary>流れ（移植元 → 移植先）の見た目を状態に合わせる</summary>
        private void UpdateStrip(PBRemap definition, PBRemapSituation s)
        {
            foreach (var c in new[] { "pbremap-strip--home", "pbremap-strip--displaced", "pbremap-strip--broken", "pbremap-strip--error" })
                _strip.RemoveFromClassList(c);
            _nodeSrc.RemoveFromClassList("pbremap-node--ghost");
            _nodeDst.RemoveFromClassList("pbremap-node--empty");
            _nodeDst.RemoveFromClassList("pbremap-node--ghost");
            _lineLeft.RemoveFromClassList("pbremap-line--active"); _lineRight.RemoveFromClassList("pbremap-line--active");
            _lineLeft.RemoveFromClassList("pbremap-line--home"); _lineRight.RemoveFromClassList("pbremap-line--home");
            _srcBadge.style.display = DisplayStyle.None;
            _dstBadge.style.display = DisplayStyle.None;

            // 移植先ノード
            if (s.Destination != null && s.Destination.IsFound)
            {
                PBRemapIcons.Set(_dstIcon, PBRemapIcons.ForKind(s.Destination.Kind));
                SetNodeText(_dstName, _dstSub, s.Destination);
                _nodeDst.tooltip = $"移植先（このオブジェクトの置き場所）: {s.Destination.DisplayName}\n種別: {PBRemapContextResolver.KindLabel(s.Destination.Kind)}"
                    + (s.Destination.HasOuter ? $"\n外側: {s.Destination.Outer.name}（アバターボーンへの参照はこちらで解決）" : "")
                    + (s.Destination.Method == AvatarDetectionMethod.Manual ? "\n（詳細設定で手動指定）" : "")
                    + "\n\nHierarchy からアバター/衣装/小物をここへドロップすると、その配下へ移動します";
            }
            else
            {
                PBRemapIcons.Set(_dstIcon, PBRemapIcons.Unlinked);
                SetNodeText(_dstName, _dstSub, "", "");
                _nodeDst.AddToClassList("pbremap-node--empty");
                _nodeDst.tooltip = "移植先が見つかりません。アバター/衣装/小物の配下へドラッグするか、Hierarchy からここへドロップしてください";
            }

            bool showApply = false, ready = false, partial = false;
            string applyTip = "";
            switch (s.State)
            {
                case PBRemapState.AtHome:
                    _strip.AddToClassList("pbremap-strip--home");
                    PBRemapIcons.Set(_srcIcon, PBRemapIcons.Self);
                    SetNodeText(_srcName, _srcSub, definition.gameObject.name, "");
                    _nodeSrc.tooltip = "この AvatarDynamics は右のルートに接続されています。\n別のアバター/衣装/小物へドラッグ＆ドロップすると移植できます";
                    PBRemapIcons.Set(_connectorState, PBRemapIcons.Linked, "接続済み" + (definition.Applied != null && definition.Applied.isApplied
                        ? $"\n最後の移植: {definition.Applied.sourceRootName} → {definition.Applied.destinationRootName} (x{definition.Applied.worldScaleRatio:F3})" : ""));
                    _connectorState.style.display = DisplayStyle.Flex;
                    _lineLeft.AddToClassList("pbremap-line--home"); _lineRight.AddToClassList("pbremap-line--home");
                    break;
                case PBRemapState.Displaced:
                    _strip.AddToClassList("pbremap-strip--displaced");
                    if (s.Source != null && s.Source.Root != null)
                    {
                        PBRemapIcons.Set(_srcIcon, PBRemapIcons.ForKind(s.Source.Kind));
                        SetNodeText(_srcName, _srcSub, s.Source);
                        _nodeSrc.tooltip = $"移植元（参照が今指している場所）: {s.Source.DisplayName}";
                    }
                    else
                    {
                        // 一部の参照が失われている: 参照情報の移植元を表示
                        var m = definition.Manifest;
                        string lostFrom = m != null && !string.IsNullOrEmpty(m.lostSourceName) ? m.lostSourceName : (m != null ? m.SourceDisplayName : "");
                        PBRemapIcons.Set(_srcIcon, PBRemapIcons.Unlinked);
                        SetNodeText(_srcName, _srcSub, lostFrom.Contains(" › ") ? lostFrom.Substring(lostFrom.IndexOf(" › ") + 3) : lostFrom, lostFrom.Contains(" › ") ? lostFrom.Substring(0, lostFrom.IndexOf(" › ")) : "");
                        _nodeSrc.AddToClassList("pbremap-node--ghost");
                        _nodeSrc.tooltip = $"{s.LostReferences} 件の参照が失われています（元の参照先: {lostFrom}）。\n参照情報から右のルートへ解決します";
                        _srcBadge.style.display = DisplayStyle.Flex; PBRemapIcons.Set(_srcBadge, PBRemapIcons.Unlinked);
                    }
                    _connectorState.style.display = DisplayStyle.None;
                    _lineLeft.AddToClassList("pbremap-line--active"); _lineRight.AddToClassList("pbremap-line--active");
                    showApply = true;
                    break;
                case PBRemapState.Broken:
                    _strip.AddToClassList(s.HasManifest ? "pbremap-strip--broken" : "pbremap-strip--error");
                    if (s.HasManifest)
                    {
                        PBRemapIcons.Set(_srcIcon, PBRemapIcons.ForKind(definition.Manifest.sourceRootKind));
                        SetNodeText(_srcName, _srcSub, definition.Manifest);
                        _nodeSrc.AddToClassList("pbremap-node--ghost");
                        _srcBadge.style.display = DisplayStyle.Flex; PBRemapIcons.Set(_srcBadge, PBRemapIcons.Unlinked);
                        _nodeSrc.tooltip = $"参照情報に記録された移植元: {definition.Manifest.SourceDisplayName}（Prefab/別シーン。参照そのものは失われています）";
                        showApply = true;
                    }
                    else
                    {
                        PBRemapIcons.Set(_srcIcon, PBRemapIcons.Error);
                        SetNodeText(_srcName, _srcSub, "?", "");
                        _nodeSrc.tooltip = "参照が失われており、参照情報もありません。\n移植元のシーンでこのオブジェクトを選択して ↻ で参照情報を取り直してから持ち出してください";
                    }
                    _connectorState.style.display = DisplayStyle.None;
                    break;
                case PBRemapState.NoDestination:
                    _strip.AddToClassList("pbremap-strip--error");
                    if (s.Source != null && s.Source.Root != null) { PBRemapIcons.Set(_srcIcon, PBRemapIcons.ForKind(s.Source.Kind)); SetNodeText(_srcName, _srcSub, s.Source); }
                    else if (s.HasManifest) { PBRemapIcons.Set(_srcIcon, PBRemapIcons.ForKind(definition.Manifest.sourceRootKind)); SetNodeText(_srcName, _srcSub, definition.Manifest); _nodeSrc.AddToClassList("pbremap-node--ghost"); }
                    else { PBRemapIcons.Set(_srcIcon, PBRemapIcons.Self); SetNodeText(_srcName, _srcSub, definition.gameObject.name, ""); }
                    _nodeSrc.tooltip = "移植元: " + _srcName.text;
                    PBRemapIcons.Set(_connectorState, PBRemapIcons.Unlinked, "移植先がありません");
                    _connectorState.style.display = DisplayStyle.Flex;
                    break;
                default: // NoReferences
                    _strip.AddToClassList("pbremap-strip--home");
                    PBRemapIcons.Set(_srcIcon, PBRemapIcons.Empty);
                    SetNodeText(_srcName, _srcSub, definition.gameObject.name, "");
                    _nodeSrc.tooltip = "移植する対象がありません。PhysBone/PhysBoneCollider/Constraint/Contact を持つオブジェクトをこの配下に置いてください（PBReplacer の Apply で生成される AvatarDynamics を推奨）";
                    _connectorState.style.display = DisplayStyle.None;
                    break;
            }

            // ビルド時のみ適用: 「今は触らない、NDMF ビルド（再生）時に移植される」を → の隣に再生アイコンで示す
            if (showApply && definition.ApplyMode == PBRemapApplyMode.BuildOnly)
            {
                string when = PBRemapPlayModeApplier.IsHandledByNdmf(definition)
                    ? "NDMF ビルド（再生）時に非破壊で移植されます。編集中のシーンは変わりません"
                    : "再生時に非破壊で移植されます（VRC アバター配下ではないため NDMF の対象外。PBRemap が再生開始時に適用し、VRChat へのビルドには含まれません）";
                PBRemapIcons.Set(_connectorState, PBRemapIcons.Build, "ビルド時のみ移植（BuildOnly）: " + when + "\n今すぐ移植するなら → を押します（詳細設定で「ドロップ時」を変更できます）");
                _connectorState.style.display = DisplayStyle.Flex;
            }

            // → ボタン
            var plan = _preview?.Plan;
            if (showApply && plan != null && plan.CanApply)
            {
                ready = plan.IsFullyResolved;
                partial = !ready;
                applyTip = ready
                    ? $"移植する: {plan.ResolvedCount} 参照を付け替え" + (plan.AutoCreateCount > 0 ? $"、{plan.AutoCreateCount} オブジェクトを自動作成" : "") + $"\nスケール x{plan.WorldScaleRatio:F3}（{plan.ScaleMethod}）\nCtrl+Z で取り消せます"
                    : $"移植する（{plan.AmbiguousCount + plan.UnresolvedCount} 件は未解決のまま残ります）\n下の表で対応付けると解決できます\nCtrl+Z で取り消せます";
                if (plan.Warnings.Any(w => w.StartsWith("VRC Constraint は")))
                    applyTip += "\n\nConstraint は参照の付け替えのみで、オフセットは再計算しません（向きが違う場合は再ベイク）";
            }
            else if (showApply && plan != null)
                applyTip = string.Join("\n", plan.Errors);
            _applyButton.style.display = showApply ? DisplayStyle.Flex : DisplayStyle.None;
            _applyButton.SetEnabled(plan != null && plan.CanApply);
            _applyButton.EnableInClassList("pbremap-apply--ready", ready);
            _applyButton.EnableInClassList("pbremap-apply--partial", partial);
            _applyButton.tooltip = applyTip;
        }

        private void UpdateCandidates(PBRemap definition, PBRemapSituation s)
        {
            _candidates.Clear();
            var cands = s.State == PBRemapState.NoDestination && s.Destination != null ? s.Destination.Candidates : null;
            if (cands == null || cands.Count == 0) { _candidates.style.display = DisplayStyle.None; return; }
            _candidates.style.display = DisplayStyle.Flex;
            _candidates.Add(PBRemapIcons.Image(PBRemapIcons.Info, 14, "移植先の候補。クリックするとその配下へ移動します"));
            foreach (var c in cands)
            {
                var go = c;
                var kind = PBRemapContextResolver.ClassifyRootCandidate(go.transform);
                var b = new Button(() => MoveUnder(definition, go)) { tooltip = $"{go.name} の配下へ移動（Ctrl+Z で戻せます）" };
                b.AddToClassList("pbremap-candidate");
                b.Add(PBRemapIcons.Image(PBRemapIcons.ForKind(kind), 16));
                b.Add(new Label(go.name));
                _candidates.Add(b);
            }
        }

        private void UpdateChips(PBRemap definition, PBRemapSituation s)
        {
            _chips.Clear();
            var plan = _preview?.Plan;
            if (plan == null || plan.Resolutions.Count == 0) { _chips.style.display = DisplayStyle.None; return; }
            _chips.style.display = DisplayStyle.Flex;

            int unresolved = plan.CountOf(ResolutionStatus.Unresolved) + plan.CountOf(ResolutionStatus.ExternalObject);
            if (plan.ResolvedCount > 0) _chips.Add(Chip(PBRemapIcons.Resolved, plan.ResolvedCount, "resolved", "解決済み（クリックで表示を切り替え）", Filter.ShowResolved, v => Filter.ShowResolved = v));
            if (plan.AutoCreateCount > 0) _chips.Add(Chip(PBRemapIcons.AutoCreate, plan.AutoCreateCount, "auto", "移植先に無いので自動作成する（クリックで表示を切り替え）", Filter.ShowAutoCreatable, v => Filter.ShowAutoCreatable = v));
            if (plan.AmbiguousCount > 0) _chips.Add(Chip(PBRemapIcons.Ambiguous, plan.AmbiguousCount, "ambiguous", "同名の候補が複数あり、選択が必要（クリックで表示を切り替え）", Filter.ShowAmbiguous, v => Filter.ShowAmbiguous = v));
            if (unresolved > 0) _chips.Add(Chip(PBRemapIcons.Unresolved, unresolved, "unresolved", "対応するボーンが見つからない（クリックで表示を切り替え）", Filter.ShowUnresolved, v => Filter.ShowUnresolved = v));

            int manualCount = definition.MappingOverrides?.Count ?? 0;
            if (manualCount > 0)
            {
                var m = new Button(OnClearManualClicked) { tooltip = $"手動で対応付けた {manualCount} 件をすべて取り消す" };
                m.AddToClassList("pbremap-chip"); m.AddToClassList("pbremap-chip--manual");
                m.Add(PBRemapIcons.Image(PBRemapIcons.Manual, 12));
                var cnt = new Label(manualCount.ToString()); cnt.AddToClassList("pbremap-chip-count"); m.Add(cnt);
                m.Add(PBRemapIcons.Image(PBRemapIcons.Trash, 12));
                _chips.Add(m);
            }

            // スケール
            var scale = new Button(() => ShowScaleMenu(definition));
            scale.AddToClassList("pbremap-chip"); scale.AddToClassList("pbremap-chip--scale");
            scale.Add(PBRemapIcons.Image(PBRemapIcons.Scale, 12));
            var sl = new Label(definition.ScaleMode == PBRemapScaleMode.None ? "×1" : $"×{plan.WorldScaleRatio:F2}");
            sl.AddToClassList("pbremap-chip-count"); scale.Add(sl);
            scale.tooltip = $"スケール補正: {ModeLabel(definition.ScaleMode)}\n世界寸法比 x{plan.WorldScaleRatio:F4}（{plan.ScaleMethod}）"
                + (Mathf.Abs(plan.OuterScaleRatio - plan.WorldScaleRatio) > 1e-4f ? $"\n外側（アバターボーン参照）x{plan.OuterScaleRatio:F4}" : "")
                + "\nradius 等は「元値 × 比 × 移植元/移植先ボーンの lossyScale 比」で適用\nクリックでモードを変更";
            _chips.Add(scale);
            if (definition.ScaleMode == PBRemapScaleMode.Manual)
            {
                var f = new FloatField { value = definition.ManualScaleFactor, tooltip = "手動の世界寸法比" };
                f.AddToClassList("pbremap-chip-field");
                f.RegisterValueChangedCallback(evt =>
                {
                    serializedObject.Update();
                    serializedObject.FindProperty("manualScaleFactor").floatValue = evt.newValue;
                    serializedObject.ApplyModifiedProperties();
                    QueueRefresh();
                });
                _chips.Add(f);
            }
        }

        private static string ModeLabel(PBRemapScaleMode m) => m == PBRemapScaleMode.Auto ? "自動" : m == PBRemapScaleMode.Manual ? "手動" : "なし";

        private void ShowScaleMenu(PBRemap definition)
        {
            var menu = new GenericMenu();
            foreach (PBRemapScaleMode m in Enum.GetValues(typeof(PBRemapScaleMode)))
            {
                var mode = m;
                menu.AddItem(new GUIContent(ModeLabel(mode)), definition.ScaleMode == mode, () =>
                {
                    serializedObject.Update();
                    serializedObject.FindProperty("scaleMode").enumValueIndex = (int)mode;
                    serializedObject.ApplyModifiedProperties();
                    UpdateScaleFieldVisibility();
                    QueueRefresh();
                });
            }
            menu.ShowAsContext();
        }

        private static VisualElement Chip(string icon, int count, string kind, string tooltip, bool on, Action<bool> setOn)
        {
            var chip = new Button(() => setOn(!on)) { tooltip = tooltip };
            chip.AddToClassList("pbremap-chip");
            chip.AddToClassList("pbremap-chip--" + kind);
            chip.EnableInClassList("pbremap-chip--off", !on);
            chip.Add(PBRemapIcons.Image(icon, 12));
            var label = new Label(count.ToString());
            label.AddToClassList("pbremap-chip-count");
            chip.Add(label);
            return chip;
        }

        /// <summary>ボーン対応表。移植元ボーン単位で1行。フィルタで問題のある行だけを見せる</summary>
        private void UpdateMappingTable(PBRemap definition)
        {
            _mappingTable.Clear();
            var plan = _preview?.Plan;
            if (plan == null || plan.Resolutions.Count == 0) { _mappingTable.style.display = DisplayStyle.None; return; }

            // 問題のある行（要選択/未解決）を先頭に、次に自動作成、最後に解決済み
            int Rank(ReferenceResolution r) => r.Status == ResolutionStatus.Ambiguous || r.Status == ResolutionStatus.Unresolved || r.Status == ResolutionStatus.ExternalObject ? 0 : r.Status == ResolutionStatus.AutoCreate ? 1 : 2;
            var groups = plan.Resolutions.GroupBy(r => r.SourceKey).OrderBy(g => Rank(g.First())).ToList();
            int shown = 0;
            foreach (var g in groups)
            {
                var res = g.First();
                bool visible;
                string rowClass; string icon;
                switch (res.Status)
                {
                    case ResolutionStatus.Resolved: case ResolutionStatus.Manual: visible = Filter.ShowResolved; rowClass = ""; icon = res.Status == ResolutionStatus.Manual ? PBRemapIcons.Manual : PBRemapIcons.Resolved; break;
                    case ResolutionStatus.AutoCreate: visible = Filter.ShowAutoCreatable; rowClass = "pbremap-row--auto"; icon = PBRemapIcons.AutoCreate; break;
                    case ResolutionStatus.Ambiguous: visible = Filter.ShowAmbiguous; rowClass = "pbremap-row--ambiguous"; icon = PBRemapIcons.Ambiguous; break;
                    default: visible = Filter.ShowUnresolved; rowClass = "pbremap-row--unresolved"; icon = PBRemapIcons.Unresolved; break;
                }
                if (!visible) continue;
                shown++;

                var row = new VisualElement();
                row.AddToClassList("pbremap-row");
                if (rowClass != "") row.AddToClassList(rowClass);

                var st = PBRemapIcons.Image(icon, 14);
                st.AddToClassList("pbremap-row-icon");
                st.tooltip = StatusTip(res);
                row.Add(st);

                var src = new Label(res.Ref.boneName);
                src.AddToClassList("pbremap-row-src");
                if (res.IsOuter) src.AddToClassList("pbremap-row-src--outer");
                src.tooltip = (res.IsOuter ? "（外側: アバターのボーン）\n" : "") + "移植元: " + res.SourceDisplayPath + "\n参照元:\n  " + string.Join("\n  ", g.Select(x => x.Ref.componentPath + "." + x.Ref.propertyPath));
                row.Add(src);

                var arrow = PBRemapIcons.Image(PBRemapIcons.Apply, 12);
                arrow.AddToClassList("pbremap-row-arrow");
                row.Add(arrow);

                var destField = new ObjectField { objectType = typeof(Transform), allowSceneObjects = true };
                destField.SetValueWithoutNotify(res.Target);
                destField.tooltip = StatusTip(res) + "\nHierarchy からボーンをドロップすると手動で対応付けます";
                var key = res.SourceKey; var displayPath = res.SourceDisplayPath;
                destField.RegisterValueChangedCallback(evt => SetManualMapping(definition, key, displayPath, evt.newValue as Transform));
                row.Add(destField);

                if (res.Status == ResolutionStatus.Ambiguous)
                {
                    var candidates = res.Candidates;
                    var pick = new Button(() =>
                    {
                        var menu = new GenericMenu();
                        foreach (var c in candidates)
                        {
                            var cc = c;
                            menu.AddItem(new GUIContent(BoneMapper.GetRelativePath(cc, plan.DestinationRoot.transform) ?? cc.name), false, () => SetManualMapping(definition, key, displayPath, cc));
                        }
                        menu.ShowAsContext();
                    }) { tooltip = $"{candidates.Count} 件の候補から選ぶ" };
                    pick.AddToClassList("pbremap-row-action");
                    pick.Add(PBRemapIcons.Image(PBRemapIcons.Dropdown, 12));
                    row.Add(pick);
                }
                else if (res.Status == ResolutionStatus.AutoCreate)
                {
                    var ghost = PBRemapIcons.Image(PBRemapIcons.AutoCreate, 12);
                    ghost.AddToClassList("pbremap-row-action");
                    ghost.tooltip = res.Message + "\n（Transform を指定すると自動作成の代わりにそれを使います）";
                    row.Add(ghost);
                }
                _mappingTable.Add(row);
            }
            _mappingTable.style.display = shown > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static string StatusTip(ReferenceResolution res)
        {
            switch (res.Status)
            {
                case ResolutionStatus.Resolved: return $"解決済み（{MethodLabel(res.Method)}）";
                case ResolutionStatus.Manual: return "手動で対応付け済み";
                case ResolutionStatus.AutoCreate: return res.Message;
                case ResolutionStatus.Ambiguous: return res.Message;
                case ResolutionStatus.ExternalObject: return res.Message;
                default: return res.Message;
            }
        }

        private static string MethodLabel(ResolutionMethod m)
        {
            switch (m)
            {
                case ResolutionMethod.Humanoid: return "Humanoid ボーン";
                case ResolutionMethod.HumanoidAncestorPath: return "Humanoid 祖先 + 相対パス";
                case ResolutionMethod.CostumeContextPath: return "衣装内の相対パス";
                case ResolutionMethod.NormalizedNameInMain: return "prefix/suffix を除いた名前";
                case ResolutionMethod.ContextPath: return "相対パス";
                case ResolutionMethod.RemapRulePath: return "対応ルール";
                case ResolutionMethod.UniqueName: return "一意な名前";
                default: return m.ToString();
            }
        }

        private void UpdateWarnings(PBRemap definition, PBRemapSituation s)
        {
            var warnings = new List<string>(_detection.Warnings);
            if (definition.transform.parent != null && definition.transform.parent.GetComponentInParent<PBRemap>(true) != null)
                warnings.Add("このPBRemapは別のPBRemapの配下にあります。外側のPBRemapが一括で扱うため、ここでは無視されます。");
            if (definition.GetComponentsInChildren<PBRemap>(true).Any(x => x != definition))
                warnings.Add("配下に別のPBRemapがあります。その配下のコンポーネントはこのPBRemapの対象外です。");
            if (PrefabUtility.IsPartOfPrefabInstance(definition) && definition.Manifest != null && !definition.Manifest.IsEmpty && s.State == PBRemapState.AtHome)
                warnings.Add("参照情報はPrefabインスタンスのオーバーライドとして保存されています（Revert All Overrides で失われます）。");
            if (_preview != null) { warnings.AddRange(_preview.Errors); warnings.AddRange(_preview.Warnings); }
            // チップ/表/候補で既に伝わっている内容は文字で繰り返さない
            warnings = warnings.Where(w => !string.IsNullOrEmpty(w)
                    && !w.StartsWith("VRC Constraint は")
                    && !w.Contains("個のボーンが解決できませんでした")
                    && !w.StartsWith("移植先の候補:"))
                .Distinct().ToList();
            if (warnings.Count > 0)
            {
                _warningBox.text = string.Join("\n", warnings);
                _warningBox.messageType = _preview != null && _preview.Errors.Count > 0 ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
                _warningBox.style.display = DisplayStyle.Flex;
            }
            else _warningBox.style.display = DisplayStyle.None;
        }

        private void UpdateTools(PBRemapSituation s)
        {
            _refreshButton.SetEnabled(s.State == PBRemapState.AtHome || s.State == PBRemapState.Displaced);
            bool eyeOn = PBRemapScenePreviewState.Instance.IsActive;
            _eyeButton.SetEnabled(s.CanResolve && _detection.IsLiveMode);
            PBRemapIcons.Set(_eyeButton.Q<Image>(), eyeOn ? PBRemapIcons.Eye : PBRemapIcons.EyeOff);
            _eyeButton.EnableInClassList("pbremap-icon-button--on", eyeOn);
        }

        private void UpdateScaleFieldVisibility()
        {
            var definition = (PBRemap)target;
            if (_scaleFactorField != null) _scaleFactorField.style.display = definition.ScaleMode == PBRemapScaleMode.Manual ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateManifestInfo(PBRemap definition)
        {
            if (_manifestInfo == null) return;
            var m = definition.Manifest;
            if (m == null || m.IsEmpty) { _manifestInfo.text = "（なし）"; return; }
            var ctxs = string.Join(", ", m.contexts.Where(c => c.id != 0 && m.refs.Any(r => r.contextId == c.id)).Select(c => $"{(c.scope == BoneContextScope.Outer ? "外側/" : "")}{c.kind}:{c.armatureName}{(c.isHumanoid ? "(Humanoid)" : "")}{(string.IsNullOrEmpty(c.maPrefix + c.maSuffix) ? "" : $"[{c.maPrefix}*{c.maSuffix}]")}"));
            _manifestInfo.text = $"移植元: {m.SourceDisplayName} ({m.sourceRootKind})\n取得: {m.capturedAtUtc}\n参照: {m.refs.Count} 件 / {ctxs}\nHips-Head: {m.scaleReference.hipsToHead:F4}" + (m.scaleReference.outerHipsToHead > 0 ? $" / 外側 {m.scaleReference.outerHipsToHead:F4}" : "");
        }

        private void ShowStatus(string text, HelpBoxMessageType type)
        {
            _statusBox.text = text; _statusBox.messageType = type; _statusBox.style.display = DisplayStyle.Flex;
        }

        #endregion

        #region drop / move

        private void RegisterDropTarget(VisualElement node, PBRemap definition)
        {
            node.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                var go = DraggedRoot(definition);
                if (go == null) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                node.AddToClassList("pbremap-node--drop-hover");
                evt.StopPropagation();
            });
            node.RegisterCallback<DragLeaveEvent>(_ => node.RemoveFromClassList("pbremap-node--drop-hover"));
            node.RegisterCallback<DragExitedEvent>(_ => node.RemoveFromClassList("pbremap-node--drop-hover"));
            node.RegisterCallback<DragPerformEvent>(evt =>
            {
                node.RemoveFromClassList("pbremap-node--drop-hover");
                var go = DraggedRoot(definition);
                if (go == null) return;
                DragAndDrop.AcceptDrag();
                MoveUnder(definition, go);
                evt.StopPropagation();
            });
        }

        private static GameObject DraggedRoot(PBRemap definition)
        {
            foreach (var o in DragAndDrop.objectReferences)
            {
                var go = o as GameObject;
                if (go == null || EditorUtility.IsPersistent(go)) continue;
                if (go == definition.gameObject || go.transform.IsChildOf(definition.transform)) continue;
                return go;
            }
            return null;
        }

        private void MoveUnder(PBRemap definition, GameObject parent)
        {
            if (parent == null || definition == null) return;
            Undo.SetTransformParent(definition.transform, parent.transform, "PBRemap を移動");
            Selection.activeGameObject = definition.gameObject;
            EditorGUIUtility.PingObject(parent);
            PBRemapTracker.Invalidate();
            QueueRefresh();
        }

        #endregion

        #region manual mapping

        private void SetManualMapping(PBRemap definition, string sourceKey, string sourcePath, Transform targetTransform)
        {
            PBRemapManualMapping.Set(definition, sourceKey, sourcePath, targetTransform, _detection?.DestinationAvatar);
            QueueRefresh();
        }

        private void OnClearManualClicked()
        {
            PBRemapManualMapping.Clear((PBRemap)target);
            QueueRefresh();
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
            var deleteButton = new Button { name = "rule-delete", tooltip = "このルールを削除" };
            deleteButton.AddToClassList("remap-rule-delete-button");
            deleteButton.Add(PBRemapIcons.Image(PBRemapIcons.Trash, 12));
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
            if (changed) Flash();
            else ShowStatus("参照が生きていないため更新できません（移植元のシーンで実行してください）", HelpBoxMessageType.Info);
            PBRemapTracker.Invalidate();
            QueueRefresh();
        }

        private void OnEyeClicked()
        {
            var definition = (PBRemap)target;
            if (PBRemapScenePreviewState.Instance.IsActive)
            {
                PBRemapScenePreviewState.Instance.Deactivate();
            }
            else if (_detection != null && _detection.IsLiveMode)
            {
                var previewData = PBRemapPreview.GeneratePreview(definition, _detection);
                PBRemapScenePreviewState.Instance.Activate(previewData, _detection, definition);
                SceneView.RepaintAll();
            }
            UpdateTools(_detection?.Situation ?? new PBRemapSituation());
        }

        private void OnGearClicked()
        {
            bool show = _advanced.style.display != DisplayStyle.Flex;
            _advanced.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            _gearButton.EnableInClassList("pbremap-icon-button--on", show);
            EditorPrefs.SetBool(PrefAdvanced, show);
        }

        private void OnApplyClicked()
        {
            serializedObject.Update();
            var definition = (PBRemap)target;
            var settings = PBReplacerSettings.Load();
            if (settings.ShowConfirmDialog)
            {
                string sourceName = _detection?.Situation != null ? _detection.Situation.SourceDisplayName(definition) : "";
                string destName = _detection?.Situation?.DestinationDisplayName ?? "";
                if (!EditorUtility.DisplayDialog(_strings.DialogTitle, string.Format(_strings.DialogConfirmTemplate, sourceName, destName), _strings.DialogOk, _strings.DialogCancel))
                    return;
            }

            _statusBox.style.display = DisplayStyle.None;
            var result = PBRemapper.Remap(definition);
            result.Match(
                onSuccess: success =>
                {
                    Flash();
                    int left = success.UnresolvedReferenceCount + success.AmbiguousReferenceCount;
                    if (left > 0)
                        ShowStatus($"{left} 件は未解決のまま残っています（表で対応付けて再度 → を押すと解決できます）", HelpBoxMessageType.Warning);
                    QueueRefresh();
                },
                onFailure: error => ShowStatus(error, HelpBoxMessageType.Error));
        }

        /// <summary>成功時の短いフィードバック（緑に光って戻る）</summary>
        private void Flash()
        {
            if (_strip == null) return;
            _strip.AddToClassList("pbremap-strip--flash");
            _strip.schedule.Execute(() => _strip.RemoveFromClassList("pbremap-strip--flash")).StartingIn(450);
        }

        private static PBRemapPreviewWindow FindPreviewWindow()
        {
            var windows = Resources.FindObjectsOfTypeAll<PBRemapPreviewWindow>();
            return windows.Length > 0 ? windows[0] : null;
        }

        #endregion
    }
}
