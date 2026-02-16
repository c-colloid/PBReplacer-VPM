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

		private Label _summaryLabel;
		private Toggle _showLinesToggle;
		private Toggle _showLabelsToggle;
		private Toggle _showUnresolvedOnlyToggle;

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

			// サマリー
			_summaryLabel = new Label();
			_summaryLabel.AddToClassList("overlay-summary-label");
			root.Add(_summaryLabel);

			// トグル群
			_showLinesToggle = CreateToggle(
				"\u63a5\u7d9a\u30e9\u30a4\u30f3\u3092\u8868\u793a",
				PBRemapScenePreviewState.Instance.ShowConnectionLines,
				evt =>
				{
					PBRemapScenePreviewState.Instance.ShowConnectionLines = evt.newValue;
					SceneView.RepaintAll();
				});
			root.Add(_showLinesToggle);

			_showLabelsToggle = CreateToggle(
				"\u30dc\u30fc\u30f3\u540d\u30e9\u30d9\u30eb\u3092\u8868\u793a",
				PBRemapScenePreviewState.Instance.ShowBoneLabels,
				evt =>
				{
					PBRemapScenePreviewState.Instance.ShowBoneLabels = evt.newValue;
					SceneView.RepaintAll();
				});
			root.Add(_showLabelsToggle);

			_showUnresolvedOnlyToggle = CreateToggle(
				"\u672a\u89e3\u6c7a\u306e\u307f\u8868\u793a",
				PBRemapScenePreviewState.Instance.ShowUnresolvedOnly,
				evt =>
				{
					PBRemapScenePreviewState.Instance.ShowUnresolvedOnly = evt.newValue;
					SceneView.RepaintAll();
				});
			root.Add(_showUnresolvedOnlyToggle);

			UpdateSummary();

			// 定期的にサマリーを更新
			root.schedule.Execute(UpdateSummary).Every(500);

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

		private void UpdateSummary()
		{
			if (_summaryLabel == null)
				return;

			var state = PBRemapScenePreviewState.Instance;
			if (!state.IsActive)
			{
				_summaryLabel.text = "";
				return;
			}

			int autoCreatable = state.AutoCreatableCount;
			int unresolved = state.TotalCount - state.ResolvedCount - autoCreatable;
			_summaryLabel.text =
				$"\u89e3\u6c7a\u6e08\u307f: {state.ResolvedCount}/{state.TotalCount}";

			if (autoCreatable > 0)
				_summaryLabel.text += $"  (\u4f5c\u6210\u4e88\u5b9a: {autoCreatable})";
			if (unresolved > 0)
				_summaryLabel.text += $"  (\u672a\u89e3\u6c7a: {unresolved})";
		}
	}
}
