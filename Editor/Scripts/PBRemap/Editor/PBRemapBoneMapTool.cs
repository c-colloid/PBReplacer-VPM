using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.Rendering;

namespace colloid.PBReplacer
{
    /// <summary>
    /// SceneView で「対応先が無い / 候補が複数」のボーンを指差しで決めるツール。
    ///
    ///   1. 赤（✕）または琥珀（▾）の輪をクリック → その対応が選ばれ、マウスへ向かう線が伸びる（「ここから、どこへ？」）
    ///   2. 移植先の骨に出る小さな点のどれかをクリック → 対応が決まり、線が緑になる（Ctrl+Z で戻る）
    ///   3. 問題が残っていれば次の対応が自動で選ばれる。Esc で終了
    /// 文字は骨の名前だけ。ツールバー（Unity 標準の Tools オーバーレイ）にもスポイトのアイコンで並ぶ。
    /// </summary>
    [EditorTool("PBRemap ボーン対応", typeof(PBRemap))]
    public class PBRemapBoneMapTool : EditorTool
    {
        private static GUIContent _icon;
        private const float PickDotScale = 0.03f;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (_icon == null)
                    _icon = new GUIContent(PBRemapIcons.Get(PBRemapIcons.Pick), "PBRemap ボーン対応: 未解決のボーンをクリックし、移植先の骨をクリックして決める");
                return _icon;
            }
        }

        public override void OnActivated()
        {
            var state = PBRemapScenePreviewState.Instance;
            var def = target as PBRemap;
            if (!state.IsActive && def != null)
            {
                var det = SourceDetector.Detect(def);
                if (det.IsSuccess && det.Value.IsLiveMode)
                    state.Activate(PBRemapPreview.GeneratePreview(def, det.Value), det.Value, def);
            }
            if (state.SelectedKey == null) state.SelectedKey = state.NextProblemKey();
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            PBRemapScenePreviewState.Instance.SelectedKey = null;
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            var state = PBRemapScenePreviewState.Instance;
            if (!(window is SceneView sceneView) || !state.IsActive) return;
            var e = Event.current;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (state.SelectedKey != null) { state.SelectedKey = null; SceneView.RepaintAll(); }
                else ToolManager.RestorePreviousTool();
                e.Use();
                return;
            }

            var prevZ = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            var selected = state.SelectedKey != null ? state.VisualMappings.FirstOrDefault(v => v.SourceKey == state.SelectedKey) : null;
            var camFwd = sceneView.camera.transform.forward;

            if (selected != null && selected.SourceTransform != null)
            {
                // 選ばれた移植元を強調し、マウスへ向かう線（どこへ？）
                Vector3 src = selected.SourceTransform.position;
                float s = HandleUtility.GetHandleSize(src) * 0.09f;
                var color = selected.Status == BoneVisualStatus.Ambiguous ? PBRemapSceneRenderer.AmbiguousColor : PBRemapSceneRenderer.UnresolvedColor;
                PBRemapSceneRenderer.Ring(src, camFwd, s, color, 4f);
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                var plane = new Plane(-camFwd, src);
                if (plane.Raycast(ray, out float t))
                {
                    Vector3 mouse = ray.GetPoint(t);
                    Handles.color = color;
                    Handles.DrawDottedLine(src, mouse, 5f);
                    Handles.ConeHandleCap(0, mouse, Quaternion.LookRotation((mouse - src).sqrMagnitude > 1e-6f ? (mouse - src).normalized : camFwd), HandleUtility.GetHandleSize(mouse) * 0.1f, EventType.Repaint);
                }

                // 移植先の骨に小さな点（クリックで確定）
                var plan = state.PreviewData?.Plan;
                if (plan != null)
                {
                    var bones = new List<Transform>(plan.SelfBones);
                    bones.AddRange(plan.OuterBones);
                    foreach (var b in bones)
                    {
                        if (b == null || b == selected.SourceTransform) continue;
                        Vector3 p = b.position;
                        float ps = HandleUtility.GetHandleSize(p) * PickDotScale;
                        bool hover = HandleUtility.DistanceToCircle(p, 0f) < 10f;
                        Handles.color = hover ? PBRemapSceneRenderer.DestColor : PBRemapSceneRenderer.PickDotColor;
                        if (hover) PBRemapSceneRenderer.Label(p, ps, b.name, PBRemapSceneRenderer.DestColor);
                        if (Handles.Button(p, Quaternion.identity, ps, ps * 3f, Handles.DotHandleCap))
                        {
                            state.AssignManual(selected.SourceKey, b);
                            state.SelectedKey = state.NextProblemKey(selected.SourceKey);
                            if (state.SelectedKey == selected.SourceKey) state.SelectedKey = null;
                            SceneView.RepaintAll();
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }

            Handles.zTest = prevZ;
        }
    }
}
