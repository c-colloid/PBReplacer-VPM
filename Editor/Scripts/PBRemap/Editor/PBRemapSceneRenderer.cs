using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace colloid.PBReplacer
{
	/// <summary>
	/// SceneView上にボーンマッピングの接続ラインとマーカーを描画する。
	/// PBRemapSceneOverlayからSceneView.duringSceneGuiに登録される。
	/// ノードエディタ風のベジェ曲線＋グラデーション＋矢印で方向を表現する。
	/// </summary>
	public static class PBRemapSceneRenderer
	{
		// ソース側カラー（シアン系）
		private static readonly Color SourceColor = new Color(0.3f, 0.75f, 1.0f, 0.85f);
		// デスティネーション側カラー（暖色系グリーン）
		private static readonly Color DestColor = new Color(0.45f, 0.9f, 0.35f, 0.85f);

		private static readonly Color SourceMarkerColor = new Color(0.3f, 0.75f, 1.0f, 0.9f);
		private static readonly Color DestMarkerColor = new Color(0.45f, 0.9f, 0.35f, 0.9f);

		private static readonly Color UnresolvedColor = new Color(0.9f, 0.3f, 0.3f, 0.8f);
		private static readonly Color UnresolvedMarkerColor = new Color(0.95f, 0.75f, 0.2f, 0.9f);

		// 自動作成予定カラー（黄色系）
		private static readonly Color AutoCreateColor = new Color(0.95f, 0.75f, 0.2f, 0.85f);
		private static readonly Color AutoCreateMarkerColor = new Color(0.95f, 0.75f, 0.2f, 0.9f);

		private const float BoneMarkerSize = 0.01f;
		private const float LineWidth = 3f;
		private const float TangentRatio = 0.33f;
		private const float ArcHeightRatio = 0.12f;
		private const int CurveSegments = 20;
		private const float ArrowPosition = 0.65f;

		private static GUIStyle _resolvedLabelStyle;
		private static GUIStyle _unresolvedLabelStyle;
		private static GUIStyle _autoCreateLabelStyle;

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

				// ソースボーンマーカー（球）
				Handles.color = SourceMarkerColor;
				Handles.SphereHandleCap(
					0, sourcePos, Quaternion.identity,
					markerSize, EventType.Repaint);

				// デスティネーションボーンマーカー（球）
				Handles.color = DestMarkerColor;
				Handles.SphereHandleCap(
					0, destPos, Quaternion.identity,
					destMarkerSize, EventType.Repaint);

				// グラデーション付きベジェ曲線 + 矢印
				if (state.ShowConnectionLines)
				{
					DrawGradientBezierWithArrow(sourcePos, destPos);
				}

				// ラベル
				if (state.ShowBoneLabels)
				{
					EnsureLabelStyles();
					SetTextColor(SourceMarkerColor);
					string boneName = GetBoneName(visual.SourcePath);
					Handles.Label(
						sourcePos + Vector3.up * markerSize * 2f,
						boneName, _resolvedLabelStyle);
						
					SetTextColor(DestMarkerColor);
					boneName = GetBoneName(visual.DestPath);
					Handles.Label(
						destPos + Vector3.up * markerSize * 2f,
						boneName, _resolvedLabelStyle);
				}
			}
			else if (visual.AutoCreatable && visual.AutoCreateParentTransform != null)
			{
				// 自動作成予定: ソースから親ボーン（dest側）への接続
				Vector3 parentPos = visual.AutoCreateParentTransform.position;
				float parentDist = Vector3.Distance(
					parentPos, sceneView.camera.transform.position);
				float parentMarkerSize = parentDist * BoneMarkerSize;

				// ソースボーンマーカー（黄色の球）
				Handles.color = AutoCreateMarkerColor;
				Handles.SphereHandleCap(
					0, sourcePos, Quaternion.identity,
					markerSize, EventType.Repaint);

				// 親ボーンマーカー（黄色の小さな球）
				Handles.color = AutoCreateMarkerColor;
				Handles.SphereHandleCap(
					0, parentPos, Quaternion.identity,
					parentMarkerSize * 0.7f, EventType.Repaint);

				// 接続線: ソース → dest親
				if (state.ShowConnectionLines)
				{
					DrawAutoCreateBezier(sourcePos, parentPos);
				}

				if (state.ShowBoneLabels)
				{
					EnsureLabelStyles();
					string boneName = GetBoneName(visual.SourcePath);
					Handles.Label(
						sourcePos + Vector3.up * markerSize * 2f,
						boneName + " (\u4f5c\u6210\u4e88\u5b9a)", _autoCreateLabelStyle);
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
		/// ソースからデスティネーションへ向かうグラデーション付きベジェ曲線と
		/// 曲線中腹の矢印を描画する。
		/// - MakeBezierPointsで曲線をサンプリングし、セグメント毎に色補間
		/// - ソース→デスト方向にシアン→グリーンのグラデーション
		/// - 65%地点にConeHandleCapで方向を補強
		/// </summary>
		private static void DrawGradientBezierWithArrow(Vector3 sourcePos, Vector3 destPos)
		{
			float distance = Vector3.Distance(sourcePos, destPos);
			if (distance < 0.001f)
				return;

			// 3D空間用タンジェント計算
			// ソース-デスト軸に垂直な面で弧を描く
			Vector3 direction = (destPos - sourcePos).normalized;
			float tangentMag = distance * TangentRatio;

			// 接続方向にほぼ垂直な「上」ベクトルを安定的に計算
			Vector3 refUp = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f
				? Vector3.forward
				: Vector3.up;
			Vector3 right = Vector3.Cross(direction, refUp).normalized;
			Vector3 perpUp = Vector3.Cross(right, direction).normalized;

			float arcHeight = distance * ArcHeightRatio;
			Vector3 startTangent = sourcePos + direction * tangentMag + perpUp * arcHeight;
			Vector3 endTangent = destPos - direction * tangentMag + perpUp * arcHeight;

			// ベジェ曲線をサンプリング
			Vector3[] points = Handles.MakeBezierPoints(
				sourcePos, destPos, startTangent, endTangent, CurveSegments);

			// グラデーション描画: セグメント毎に色を補間
			for (int i = 0; i < points.Length - 1; i++)
			{
				float t = (float)i / (points.Length - 1);
				Handles.color = Color.Lerp(SourceColor, DestColor, t);
				Handles.DrawAAPolyLine(LineWidth, points[i], points[i + 1]);
			}

			// 曲線中腹に矢印を配置（方向を補強）
			int arrowIdx = Mathf.Clamp(
				(int)(points.Length * ArrowPosition), 1, points.Length - 2);
			Vector3 arrowPos = points[arrowIdx];
			Vector3 arrowDir = (points[arrowIdx + 1] - points[arrowIdx - 1]).normalized;

			if (arrowDir.sqrMagnitude > 0.001f)
			{
				float arrowSize = HandleUtility.GetHandleSize(arrowPos) * 0.12f;
				Handles.color = Color.Lerp(SourceColor, DestColor, ArrowPosition);
				Handles.ConeHandleCap(
					0, arrowPos, Quaternion.LookRotation(arrowDir),
					arrowSize, EventType.Repaint);
			}
		}

		/// <summary>
		/// 自動作成予定ボーン用のベジェ曲線を描画する。
		/// 単色の黄色でソースから親ボーンへの接続を描画する。
		/// </summary>
		private static void DrawAutoCreateBezier(Vector3 sourcePos, Vector3 parentPos)
		{
			float distance = Vector3.Distance(sourcePos, parentPos);
			if (distance < 0.001f)
				return;

			Vector3 direction = (parentPos - sourcePos).normalized;
			float tangentMag = distance * TangentRatio;

			Vector3 refUp = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f
				? Vector3.forward
				: Vector3.up;
			Vector3 right = Vector3.Cross(direction, refUp).normalized;
			Vector3 perpUp = Vector3.Cross(right, direction).normalized;

			float arcHeight = distance * ArcHeightRatio;
			Vector3 startTangent = sourcePos + direction * tangentMag + perpUp * arcHeight;
			Vector3 endTangent = parentPos - direction * tangentMag + perpUp * arcHeight;

			Vector3[] points = Handles.MakeBezierPoints(
				sourcePos, parentPos, startTangent, endTangent, CurveSegments);

			// 黄色の単色ベジェ曲線
			Handles.color = AutoCreateColor;
			for (int i = 0; i < points.Length - 1; i++)
			{
				Handles.DrawAAPolyLine(LineWidth * 0.8f, points[i], points[i + 1]);
			}

			// 曲線中腹に矢印
			int arrowIdx = Mathf.Clamp(
				(int)(points.Length * ArrowPosition), 1, points.Length - 2);
			Vector3 arrowPos = points[arrowIdx];
			Vector3 arrowDir = (points[arrowIdx + 1] - points[arrowIdx - 1]).normalized;

			if (arrowDir.sqrMagnitude > 0.001f)
			{
				float arrowSize = HandleUtility.GetHandleSize(arrowPos) * 0.10f;
				Handles.color = AutoCreateColor;
				Handles.ConeHandleCap(
					0, arrowPos, Quaternion.LookRotation(arrowDir),
					arrowSize, EventType.Repaint);
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
				_resolvedLabelStyle.normal.textColor = SourceMarkerColor;
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

			if (_autoCreateLabelStyle == null)
			{
				_autoCreateLabelStyle = new GUIStyle(EditorStyles.miniLabel)
				{
					fontSize = 10,
					fontStyle = FontStyle.Bold,
					padding = new RectOffset(2, 2, 1, 1)
				};
				_autoCreateLabelStyle.normal.textColor = AutoCreateMarkerColor;
				_autoCreateLabelStyle.normal.background = Texture2D.linearGrayTexture;
			}
		}
		
		private static void SetTextColor(Color textColor)
		{
			_resolvedLabelStyle.normal.textColor = textColor;
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
