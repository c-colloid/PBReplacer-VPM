using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace colloid.PBReplacer
{
	/// <summary>
	/// SceneView上にボーンマッピングの接続ラインとマーカーを描画する。
	/// PBRemapSceneOverlayからSceneView.duringSceneGuiに登録される。
	/// </summary>
	public static class PBRemapSceneRenderer
	{
		private static readonly Color ResolvedColor = new Color(0.4f, 0.85f, 0.4f, 0.8f);
		private static readonly Color UnresolvedColor = new Color(0.9f, 0.3f, 0.3f, 0.8f);
		private static readonly Color ResolvedLineColor = new Color(0.4f, 0.85f, 0.4f, 0.4f);
		private static readonly Color UnresolvedMarkerColor = new Color(0.95f, 0.75f, 0.2f, 0.9f);

		private const float BoneMarkerSize = 0.005f;

		private static GUIStyle _resolvedLabelStyle;
		private static GUIStyle _unresolvedLabelStyle;

		/// <summary>
		/// SceneView.duringSceneGui に登録するコールバック。
		/// </summary>
		public static void OnSceneGUI(SceneView sceneView)
		{
			var state = PBRemapScenePreviewState.Instance;
			if (!state.IsActive)
				return;

			if (state.VisualMappings == null || state.VisualMappings.Count == 0)
				return;

			var prevZTest = Handles.zTest;
			Handles.zTest = CompareFunction.Always;

			foreach (var visual in state.VisualMappings)
			{
				if (state.ShowUnresolvedOnly && visual.Resolved)
					continue;

				DrawBoneMapping(visual, state, sceneView);
			}

			Handles.zTest = prevZTest;
		}

		private static void DrawBoneMapping(
			BoneMappingVisual visual,
			PBRemapScenePreviewState state,
			SceneView sceneView)
		{
			if (visual.SourceTransform == null)
				return;

			Vector3 sourcePos = visual.SourceTransform.position;

			float distToCamera = Vector3.Distance(
				sourcePos, sceneView.camera.transform.position);
			float markerSize = distToCamera * BoneMarkerSize;

			if (visual.Resolved && visual.DestTransform != null)
			{
				Vector3 destPos = visual.DestTransform.position;

				// ソースボーンマーカー
				Handles.color = ResolvedColor;
				Handles.SphereHandleCap(
					0, sourcePos, Quaternion.identity,
					markerSize, EventType.Repaint);

				// デスティネーションボーンマーカー
				float destDist = Vector3.Distance(
					destPos, sceneView.camera.transform.position);
				float destMarkerSize = destDist * BoneMarkerSize;
				Handles.SphereHandleCap(
					0, destPos, Quaternion.identity,
					destMarkerSize, EventType.Repaint);

				// 接続ライン
				if (state.ShowConnectionLines)
				{
					Handles.color = ResolvedLineColor;
					Handles.DrawDottedLine(sourcePos, destPos, 4f);
				}

				// ラベル
				if (state.ShowBoneLabels)
				{
					EnsureLabelStyles();
					string boneName = GetBoneName(visual.SourcePath);
					Handles.Label(
						sourcePos + Vector3.up * markerSize * 2f,
						boneName, _resolvedLabelStyle);
				}
			}
			else
			{
				// 未解決: ソースのみに目立つマーカー
				Handles.color = UnresolvedColor;
				Handles.SphereHandleCap(
					0, sourcePos, Quaternion.identity,
					markerSize * 1.5f, EventType.Repaint);

				Handles.color = UnresolvedMarkerColor;
				Handles.DrawWireDisc(
					sourcePos, sceneView.camera.transform.forward,
					markerSize * 2f);

				if (state.ShowBoneLabels)
				{
					EnsureLabelStyles();
					string boneName = GetBoneName(visual.SourcePath);
					Handles.Label(
						sourcePos + Vector3.up * markerSize * 2f,
						boneName + " (\u672a\u89e3\u6c7a)", _unresolvedLabelStyle);
				}
			}
		}

		private static void EnsureLabelStyles()
		{
			if (_resolvedLabelStyle == null)
			{
				_resolvedLabelStyle = new GUIStyle(EditorStyles.miniLabel)
				{
					fontSize = 10,
					fontStyle = FontStyle.Bold,
					padding = new RectOffset(2, 2, 1, 1)
				};
				_resolvedLabelStyle.normal.textColor = ResolvedColor;
				_resolvedLabelStyle.normal.background = Texture2D.linearGrayTexture;
			}

			if (_unresolvedLabelStyle == null)
			{
				_unresolvedLabelStyle = new GUIStyle(EditorStyles.miniLabel)
				{
					fontSize = 10,
					fontStyle = FontStyle.Bold,
					padding = new RectOffset(2, 2, 1, 1)
				};
				_unresolvedLabelStyle.normal.textColor = UnresolvedColor;
				_unresolvedLabelStyle.normal.background = Texture2D.linearGrayTexture;
			}
		}

		private static string GetBoneName(string path)
		{
			if (string.IsNullOrEmpty(path))
				return "(unknown)";
			int lastSlash = path.LastIndexOf('/');
			return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
		}
	}
}
