using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace colloid.PBReplacer
{
	/// <summary>
	/// SceneView内にPBRemapプレビューのコントロールパネルを表示するOverlay。
	/// ITransientOverlayにより、プレビューがアクティブな時のみ表示される。
	/// </summary>
	[Overlay(typeof(SceneView), OverlayID, "PBRemap Preview")]
	public class PBRemapSceneOverlay : Overlay, ITransientOverlay
	{
		public const string OverlayID = "pbremap-scene-preview";

		public bool visible => PBRemapScenePreviewState.Instance.IsActive;

		private VisualElement _summaryContainer;
		private VisualElement _filterBar;
		private Toggle _showLinesToggle;
		private Toggle _showLabelsToggle;

		// キャッシュ（定期更新で変更検知に使用）
		private int _cachedResolved;
		private int _cachedAutoCreatable;
		private int _cachedUnresolved;
		private bool _cachedShowResolved;
		private bool _cachedShowAutoCreatable;
		private bool _cachedShowUnresolved;

		// 色定数
		private static readonly Color ResolvedColor = new Color(0.39f, 0.78f, 0.39f);
		private static readonly Color AutoCreatableColor = new Color(0.86f, 0.71f, 0.20f);
		private static readonly Color UnresolvedColor = new Color(0.86f, 0.31f, 0.31f);

		public override void OnCreated()
		{
			SceneView.duringSceneGui += PBRemapSceneRenderer.OnSceneGUI;
		}

		public override void OnWillBeDestroyed()
		{
			SceneView.duringSceneGui -= PBRemapSceneRenderer.OnSceneGUI;
		}

		public override VisualElement CreatePanelContent()
		{
			var root = new VisualElement();
			root.AddToClassList("overlay-panel-root");

			var styleSheet = Resources.Load<StyleSheet>("USS/PBRemapOverlay");
			if (styleSheet != null)
				root.styleSheets.Add(styleSheet);

			// サマリー（カラーチップ表示）
			_summaryContainer = new VisualElement();
			_summaryContainer.AddToClassList("overlay-summary-container");
			root.Add(_summaryContainer);

			// フィルターバー（3状態トグル）
			_filterBar = new VisualElement();
			_filterBar.AddToClassList("overlay-filter-bar");
			root.Add(_filterBar);

			// 表示設定トグル
			_showLinesToggle = CreateToggle(
				"接続ラインを表示",
				PBRemapScenePreviewState.Instance.ShowConnectionLines,
				evt =>
				{
					PBRemapScenePreviewState.Instance.ShowConnectionLines = evt.newValue;
					SceneView.RepaintAll();
				});
			root.Add(_showLinesToggle);

			_showLabelsToggle = CreateToggle(
				"ボーン名ラベルを表示",
				PBRemapScenePreviewState.Instance.ShowBoneLabels,
				evt =>
				{
					PBRemapScenePreviewState.Instance.ShowBoneLabels = evt.newValue;
					SceneView.RepaintAll();
				});
			root.Add(_showLabelsToggle);

			UpdatePanel();

			// 定期的にパネルを更新
			root.schedule.Execute(UpdatePanel).Every(500);

			return root;
		}

		private Toggle CreateToggle(string label, bool initialValue,
			EventCallback<ChangeEvent<bool>> callback)
		{
			var toggle = new Toggle(label);
			toggle.value = initialValue;
			toggle.AddToClassList("overlay-toggle-row");
			toggle.RegisterValueChangedCallback(callback);
			return toggle;
		}

		private void UpdatePanel()
		{
			if (_summaryContainer == null || _filterBar == null)
				return;

			var state = PBRemapScenePreviewState.Instance;
			if (!state.IsActive)
			{
				_summaryContainer.Clear();
				_filterBar.Clear();
				return;
			}

			int resolved = state.ResolvedCount;
			int autoCreatable = state.AutoCreatableCount;
			int unresolved = state.TotalCount - resolved - autoCreatable;

			// カウントもフィルター状態も変わっていなければ再構築不要
			if (resolved == _cachedResolved
				&& autoCreatable == _cachedAutoCreatable
				&& unresolved == _cachedUnresolved
				&& state.ShowResolved == _cachedShowResolved
				&& state.ShowAutoCreatable == _cachedShowAutoCreatable
				&& state.ShowUnresolved == _cachedShowUnresolved
				&& _summaryContainer.childCount > 0)
				return;

			_cachedResolved = resolved;
			_cachedAutoCreatable = autoCreatable;
			_cachedUnresolved = unresolved;
			_cachedShowResolved = state.ShowResolved;
			_cachedShowAutoCreatable = state.ShowAutoCreatable;
			_cachedShowUnresolved = state.ShowUnresolved;

			// サマリー再構築
			_summaryContainer.Clear();
			var header = new Label($"ボーン解決: {resolved}/{state.TotalCount}");
			header.AddToClassList("overlay-summary-header");
			_summaryContainer.Add(header);

			var chipRow = new VisualElement();
			chipRow.AddToClassList("overlay-chip-row");
			if (resolved > 0)
				chipRow.Add(CreateSummaryChip(resolved, "解決", ResolvedColor));
			if (autoCreatable > 0)
				chipRow.Add(CreateSummaryChip(autoCreatable, "作成予定", AutoCreatableColor));
			if (unresolved > 0)
				chipRow.Add(CreateSummaryChip(unresolved, "未解決", UnresolvedColor));
			_summaryContainer.Add(chipRow);

			// フィルターバー再構築
			_filterBar.Clear();
			var filterLabel = new Label("表示:");
			filterLabel.AddToClassList("overlay-filter-label");
			_filterBar.Add(filterLabel);

			_filterBar.Add(CreateFilterChip(
				resolved, "解決済み", ResolvedColor, state.ShowResolved,
				v => { state.ShowResolved = v; _cachedResolved = -1; UpdatePanel(); SceneView.RepaintAll(); }));
			_filterBar.Add(CreateFilterChip(
				autoCreatable, "作成予定", AutoCreatableColor, state.ShowAutoCreatable,
				v => { state.ShowAutoCreatable = v; _cachedAutoCreatable = -1; UpdatePanel(); SceneView.RepaintAll(); }));
			_filterBar.Add(CreateFilterChip(
				unresolved, "未解決", UnresolvedColor, state.ShowUnresolved,
				v => { state.ShowUnresolved = v; _cachedUnresolved = -1; UpdatePanel(); SceneView.RepaintAll(); }));
		}

		private static VisualElement CreateSummaryChip(int count, string label, Color color)
		{
			var chip = new VisualElement();
			chip.AddToClassList("overlay-summary-chip");

			var dot = new VisualElement();
			dot.AddToClassList("overlay-chip-dot");
			dot.style.backgroundColor = color;
			chip.Add(dot);

			var text = new Label($"{count} {label}");
			text.AddToClassList("overlay-chip-text");
			text.style.color = color;
			chip.Add(text);

			return chip;
		}

		private static VisualElement CreateFilterChip(
			int count, string label, Color color, bool active, System.Action<bool> onToggle)
		{
			var chip = new VisualElement();
			chip.AddToClassList("overlay-filter-chip");
			chip.AddToClassList(active ? "overlay-filter-chip-active" : "overlay-filter-chip-inactive");

			var borderColor = active ? color : new Color(1, 1, 1, 0.08f);
			chip.style.borderTopColor = borderColor;
			chip.style.borderBottomColor = borderColor;
			chip.style.borderLeftColor = borderColor;
			chip.style.borderRightColor = borderColor;

			var dot = new VisualElement();
			dot.AddToClassList("overlay-chip-dot");
			dot.style.backgroundColor = active
				? (StyleColor)color
				: new StyleColor(new Color(color.r, color.g, color.b, 0.3f));
			chip.Add(dot);

			var text = new Label($"{count} {label}");
			text.AddToClassList("overlay-chip-text");
			text.style.color = active
				? (StyleColor)color
				: new StyleColor(new Color(0.6f, 0.6f, 0.6f));
			chip.Add(text);

			chip.RegisterCallback<ClickEvent>(evt => onToggle(!active));

			return chip;
		}
	}
}
