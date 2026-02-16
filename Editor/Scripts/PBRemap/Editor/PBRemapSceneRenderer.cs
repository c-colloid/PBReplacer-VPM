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
		private static readonly Color ResolvedLineColor = new Color(0.4f, 0.85f, 0.4f, 0.5f);
		private static readonly Color UnresolvedMarkerColor = new Color(0.95f, 0.75f, 0.2f, 0.9f);

		private const float BoneMarkerSize = 0.01f;
		private const float BezierWidth = 3f;
		private const float CurveHeightRatio = 0.25f;
		private const float ArrowSizeRatio = 2.5f;

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
				float destDist = Vector3.Distance(
					destPos, sceneView.camera.transform.position);
				float destMarkerSize = destDist * BoneMarkerSize;

				// ソースボーンマーカー（小さめの球）
				Handles.color = ResolvedColor;
				Handles.SphereHandleCap(
					0, sourcePos, Quaternion.identity,
					markerSize * 0.8f, EventType.Repaint);

				// 接続ベジェ曲線 + 矢印
				if (state.ShowConnectionLines)
				{
					DrawBezierArrow(sourcePos, destPos, destMarkerSize);
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

		/// <summary>
		/// ソースからデスティネーションへ向かうベジェ曲線と矢印先端を描画する。
		/// 上方に弧を描くノードエディタ風の曲線で方向性を表現する。
		/// </summary>
		private static void DrawBezierArrow(Vector3 sourcePos, Vector3 destPos, float destMarkerSize)
		{
			float distance = Vector3.Distance(sourcePos, destPos);
			if (distance < 0.001f)
				return;

			// 曲線の上方向オフセットを計算
			float curveHeight = distance * CurveHeightRatio;
			Vector3 upOffset = Vector3.up * curveHeight;

			// ベジェ制御点: 上方にアーチを描く
			Vector3 startTangent = sourcePos + upOffset;
			Vector3 endTangent = destPos + upOffset;

			// ベジェ曲線を描画
			Handles.DrawBezier(
				sourcePos, destPos,
				startTangent, endTangent,
				ResolvedLineColor, null, BezierWidth);

			// 矢印先端: ベジェ終端の接線方向にコーンを配置
			// 3次ベジェの終端接線 = 3 * (endPoint - endTangent)
			Vector3 arrowDir = (destPos - endTangent).normalized;
			if (arrowDir.sqrMagnitude < 0.001f)
				arrowDir = (destPos - sourcePos).normalized;

			Quaternion arrowRot = Quaternion.LookRotation(arrowDir);
			float arrowSize = destMarkerSize * ArrowSizeRatio;

			Handles.color = ResolvedColor;
			Handles.ConeHandleCap(
				0, destPos, arrowRot,
				arrowSize, EventType.Repaint);
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
