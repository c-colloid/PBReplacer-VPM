using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.Rendering;

namespace colloid.PBReplacer
{
    /// <summary>
    /// SceneView 上にボーン対応を描く。
    ///
    /// 語彙（Inspector と同じ意味・同じ色）:
    ///   ○ 青の輪 = 移植元ボーン（今ここにあるもの）        ● 緑の点 = 移植先ボーン（これから指すもの）
    ///   青→緑の曲線と矢印 = 移植の向き                     ◎ 二重の輪 = 手動で決めた対応
    ///   ＋ 琥珀 = 移植先に無いので自動作成（親へ点線）      ▾ 琥珀の輪 = 候補が複数（各候補へ細い点線。候補をクリックで確定）
    ///   ✕ 赤の輪 = 対応先が無い（クリックで対応ツール）
    /// 文字は問題のある対応とホバー/選択中の対応のボーン名だけ。
    /// マーカーはクリックできる: 移植元/移植先 → Hierarchy で Ping、候補 → その候補で確定、未解決 → 対応ツールを起動。
    /// </summary>
    public static class PBRemapSceneRenderer
    {
        // Inspector / Hierarchy と共通の意味色
        public static readonly Color SourceColor = new Color(0.30f, 0.75f, 1.00f, 0.95f);
        public static readonly Color DestColor = new Color(0.45f, 0.90f, 0.35f, 0.95f);
        public static readonly Color ManualColor = new Color(0.55f, 0.70f, 1.00f, 0.95f);
        public static readonly Color AutoCreateColor = new Color(0.95f, 0.75f, 0.20f, 0.95f);
        public static readonly Color AmbiguousColor = new Color(0.95f, 0.62f, 0.15f, 0.95f);
        public static readonly Color UnresolvedColor = new Color(0.92f, 0.30f, 0.30f, 0.95f);
        public static readonly Color PickDotColor = new Color(1f, 1f, 1f, 0.35f);

        private const float MarkerScale = 0.055f;
        private const float LineWidth = 3f;
        private const float TangentRatio = 0.33f;
        private const float ArcHeightRatio = 0.12f;
        private const int CurveSegments = 20;
        private const float ArrowPosition = 0.65f;
        private const float HoverPixels = 12f;

        private static GUIStyle _labelStyle;
        private static readonly Dictionary<Color, GUIStyle> _labelStyles = new Dictionary<Color, GUIStyle>();

        /// <summary>SceneView.duringSceneGui に登録するコールバック。</summary>
        public static void OnSceneGUI(SceneView sceneView)
        {
            var state = PBRemapScenePreviewState.Instance;
            if (!state.IsActive || state.VisualMappings == null || state.VisualMappings.Count == 0) return;

            var prevZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            UpdateHover(state);
            bool toolActive = ToolManager.activeToolType == typeof(PBRemapBoneMapTool);

            // 問題のある対応（要選択/未解決）を最後に描く: 重なったときにクリックがそちらへ届く（同距離なら後に登録した方が勝つ）
            var ordered = new List<BoneMappingVisual>(state.VisualMappings);
            ordered.Sort((a, b) => (a.IsProblem ? 1 : 0).CompareTo(b.IsProblem ? 1 : 0));
            foreach (var visual in ordered)
            {
                if (!state.IsVisible(visual)) continue;
                bool emphasized = visual.SourceKey == state.HoverKey || visual.SourceKey == state.SelectedKey;
                DrawMapping(visual, state, emphasized, toolActive);
            }

            Handles.zTest = prevZTest;
        }

        private static void UpdateHover(PBRemapScenePreviewState state)
        {
            var e = Event.current;
            if (e.type != EventType.Repaint && e.type != EventType.MouseMove && e.type != EventType.Layout) return;
            string best = null; float bestD = HoverPixels;
            foreach (var v in state.VisualMappings)
            {
                if (!state.IsVisible(v) || v.SourceTransform == null) continue;
                float d = HandleUtility.DistanceToCircle(v.SourceTransform.position, 0f);
                if (v.DestTransform != null) d = Mathf.Min(d, HandleUtility.DistanceToCircle(v.DestTransform.position, 0f));
                if (d < bestD) { bestD = d; best = v.SourceKey; }
            }
            if (best != state.HoverKey)
            {
                state.HoverKey = best;
                SceneView.RepaintAll();
            }
        }

        private static float Size(Vector3 pos) => HandleUtility.GetHandleSize(pos) * MarkerScale;

        private static void DrawMapping(BoneMappingVisual v, PBRemapScenePreviewState state, bool emphasized, bool toolActive)
        {
            if (v.SourceTransform == null) return;
            Vector3 src = v.SourceTransform.position;
            float s = Size(src);
            var camFwd = SceneView.currentDrawingSceneView != null ? SceneView.currentDrawingSceneView.camera.transform.forward : Vector3.forward;
            float w = emphasized ? LineWidth * 1.8f : LineWidth;

            switch (v.Status)
            {
                case BoneVisualStatus.Resolved:
                case BoneVisualStatus.Manual:
                {
                    if (v.DestTransform == null) return;
                    Vector3 dst = v.DestTransform.position;
                    // 移植元: 青の輪 / 移植先: 緑の点（手動は二重の輪）
                    Ring(src, camFwd, s, SourceColor, emphasized ? 3f : 2f);
                    Dot(dst, Size(dst) * 0.8f, v.Status == BoneVisualStatus.Manual ? ManualColor : DestColor);
                    if (v.Status == BoneVisualStatus.Manual) Ring(dst, camFwd, Size(dst) * 1.5f, ManualColor, 1.5f);
                    if (state.ShowConnectionLines) Curve(src, dst, SourceColor, v.Status == BoneVisualStatus.Manual ? ManualColor : DestColor, w, true, false);
                    if (state.ShowBoneLabels || emphasized)
                    {
                        Label(src, s, v.SourceName, SourceColor);
                        Label(dst, Size(dst), v.DestTransform.name, DestColor);
                    }
                    if (ClickMarker(src, s, v)) Ping(v.SourceTransform);
                    if (ClickMarker(dst, Size(dst), v)) Ping(v.DestTransform);
                    break;
                }
                case BoneVisualStatus.AutoCreate:
                {
                    Ring(src, camFwd, s, SourceColor, emphasized ? 3f : 2f);
                    if (v.AutoCreateParentTransform != null)
                    {
                        Vector3 parent = v.AutoCreateParentTransform.position;
                        Plus(parent, camFwd, Size(parent), AutoCreateColor);
                        if (state.ShowConnectionLines) Curve(src, parent, SourceColor, AutoCreateColor, w * 0.8f, true, true);
                        if (state.ShowBoneLabels || emphasized) Label(parent, Size(parent), "+" + v.SourceName, AutoCreateColor);
                        if (ClickMarker(parent, Size(parent), v)) Ping(v.AutoCreateParentTransform);
                    }
                    if (state.ShowBoneLabels || emphasized) Label(src, s, v.SourceName, SourceColor);
                    if (ClickMarker(src, s, v)) Ping(v.SourceTransform);
                    break;
                }
                case BoneVisualStatus.Ambiguous:
                {
                    // 候補が複数: 琥珀の輪と、各候補への細い点線。候補をクリックで確定
                    Ring(src, camFwd, s * 1.2f, AmbiguousColor, emphasized ? 3.5f : 2.5f);
                    Label(src, s, v.SourceName, AmbiguousColor);
                    foreach (var c in v.Candidates)
                    {
                        if (c == null) continue;
                        Vector3 cp = c.position;
                        if (state.ShowConnectionLines) { Handles.color = AmbiguousColor; Handles.DrawDottedLine(src, cp, 4f); }
                        Ring(cp, camFwd, Size(cp), AmbiguousColor, 1.5f);
                        Label(cp, Size(cp), c.name, AmbiguousColor);
                        if (Handles.Button(cp, Quaternion.identity, 0f, Size(cp) * 2.4f, Handles.CircleHandleCap))
                        {
                            state.AssignManual(v.SourceKey, c);
                            GUIUtility.ExitGUI();
                        }
                    }
                    if (ClickMarker(src, s * 1.2f, v)) SelectForTool(state, v);
                    break;
                }
                default:
                {
                    // 対応先が無い: 赤の輪と ✕。クリックで対応ツール
                    Ring(src, camFwd, s * 1.2f, UnresolvedColor, emphasized ? 3.5f : 2.5f);
                    Cross(src, camFwd, s * 0.6f, UnresolvedColor);
                    Label(src, s, v.SourceName, UnresolvedColor);
                    if (ClickMarker(src, s * 1.2f, v)) SelectForTool(state, v);
                    break;
                }
            }
        }

        private static void SelectForTool(PBRemapScenePreviewState state, BoneMappingVisual v)
        {
            state.SelectedKey = v.SourceKey;
            if (ToolManager.activeToolType != typeof(PBRemapBoneMapTool)) ToolManager.SetActiveTool<PBRemapBoneMapTool>();
            SceneView.RepaintAll();
        }

        private static void Ping(Transform t)
        {
            if (t == null) return;
            EditorGUIUtility.PingObject(t.gameObject);
        }

        /// <summary>見えないボタン（マーカーの上でクリックを拾う）</summary>
        private static bool ClickMarker(Vector3 pos, float size, BoneMappingVisual v)
        {
            float pick = v.IsProblem ? size * 2.2f : size * 1.4f;
            return Handles.Button(pos, Quaternion.identity, 0f, pick, Handles.CircleHandleCap);
        }

        #region primitives

        public static void Ring(Vector3 pos, Vector3 normal, float radius, Color color, float thickness)
        {
            Handles.color = color;
            Handles.DrawWireDisc(pos, normal, radius, thickness);
        }

        public static void Dot(Vector3 pos, float radius, Color color)
        {
            Handles.color = color;
            Handles.SphereHandleCap(0, pos, Quaternion.identity, radius * 2f, EventType.Repaint);
        }

        public static void Plus(Vector3 pos, Vector3 normal, float radius, Color color)
        {
            var (right, up) = Basis(normal);
            Handles.color = color;
            Handles.DrawAAPolyLine(3f, pos - right * radius, pos + right * radius);
            Handles.DrawAAPolyLine(3f, pos - up * radius, pos + up * radius);
        }

        public static void Cross(Vector3 pos, Vector3 normal, float radius, Color color)
        {
            var (right, up) = Basis(normal);
            var a = (right + up).normalized * radius; var b = (right - up).normalized * radius;
            Handles.color = color;
            Handles.DrawAAPolyLine(3f, pos - a, pos + a);
            Handles.DrawAAPolyLine(3f, pos - b, pos + b);
        }

        private static (Vector3 right, Vector3 up) Basis(Vector3 normal)
        {
            Vector3 refUp = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(normal, refUp).normalized;
            Vector3 up = Vector3.Cross(right, normal).normalized;
            return (right, up);
        }

        /// <summary>移植元→移植先の曲線（色は元→先へ変わる）。矢印で向きを示す。点線は「まだ無いもの」</summary>
        public static void Curve(Vector3 from, Vector3 to, Color fromColor, Color toColor, float width, bool arrow, bool dotted)
        {
            float distance = Vector3.Distance(from, to);
            if (distance < 0.001f) return;
            Vector3 direction = (to - from).normalized;
            float tangentMag = distance * TangentRatio;
            var (right, perpUp) = Basis(direction);
            float arcHeight = distance * ArcHeightRatio;
            Vector3 startTangent = from + direction * tangentMag + perpUp * arcHeight;
            Vector3 endTangent = to - direction * tangentMag + perpUp * arcHeight;
            Vector3[] points = Handles.MakeBezierPoints(from, to, startTangent, endTangent, CurveSegments);
            for (int i = 0; i < points.Length - 1; i++)
            {
                if (dotted && (i % 2 == 1)) continue;
                float t = (float)i / (points.Length - 1);
                Handles.color = Color.Lerp(fromColor, toColor, t);
                Handles.DrawAAPolyLine(width, points[i], points[i + 1]);
            }
            if (!arrow) return;
            int arrowIdx = Mathf.Clamp((int)(points.Length * ArrowPosition), 1, points.Length - 2);
            Vector3 arrowPos = points[arrowIdx];
            Vector3 arrowDir = (points[arrowIdx + 1] - points[arrowIdx - 1]).normalized;
            if (arrowDir.sqrMagnitude > 0.001f)
            {
                Handles.color = Color.Lerp(fromColor, toColor, ArrowPosition);
                Handles.ConeHandleCap(0, arrowPos, Quaternion.LookRotation(arrowDir), HandleUtility.GetHandleSize(arrowPos) * 0.12f, EventType.Repaint);
            }
        }

        public static void Label(Vector3 pos, float size, string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            Handles.Label(pos + Vector3.up * size * 2.2f, text, Style(color));
        }

        private static GUIStyle Style(Color color)
        {
            if (_labelStyles.TryGetValue(color, out var st)) return st;
            st = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(3, 3, 1, 1),
                alignment = TextAnchor.MiddleCenter,
            };
            st.normal.textColor = color;
            st.normal.background = Texture2D.linearGrayTexture;
            _labelStyles[color] = st;
            return st;
        }

        #endregion
    }
}
