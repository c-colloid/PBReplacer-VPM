using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace colloid.PBReplacer
{
    /// <summary>
    /// SceneView のツールバー型オーバーレイ。プレビューが有効なときだけ現れる。
    /// Unity 標準のツールバーと同じ「アイコンのトグル」だけで構成し、文字は件数のみ:
    ///   [線] [名前] | [✔ n] [＋ n] [▾ n] [✖ n] | [スポイト = 対応ツール] [目を閉じる = 終了]
    /// 件数トグルは Inspector のチップと同じ意味・同じ共有状態（クリックで表示の絞り込み）。
    /// </summary>
    [Overlay(typeof(SceneView), OverlayID, "PBRemap", defaultDisplay = true)]
    public class PBRemapSceneOverlay : ToolbarOverlay, ITransientOverlay
    {
        public const string OverlayID = "pbremap-scene-preview";

        public PBRemapSceneOverlay() : base(
            LinesToggle.Id, LabelsToggle.Id,
            ResolvedToggle.Id, AutoCreateToggle.Id, AmbiguousToggle.Id, UnresolvedToggle.Id,
            PickToolButton.Id, CloseButton.Id)
        { }

        public bool visible => PBRemapScenePreviewState.Instance.IsActive;

        public override void OnCreated()
        {
            SceneView.duringSceneGui += PBRemapSceneRenderer.OnSceneGUI;
            PBRemapScenePreviewState.Instance.PreviewDataChanged += SceneView.RepaintAll;
            PBRemapScenePreviewState.Instance.FilterStateChanged += SceneView.RepaintAll;
        }

        public override void OnWillBeDestroyed()
        {
            SceneView.duringSceneGui -= PBRemapSceneRenderer.OnSceneGUI;
            PBRemapScenePreviewState.Instance.PreviewDataChanged -= SceneView.RepaintAll;
            PBRemapScenePreviewState.Instance.FilterStateChanged -= SceneView.RepaintAll;
        }
    }

    /// <summary>件数付きのフィルタトグル（共有状態と同期）</summary>
    public abstract class PBRemapCountToggle : EditorToolbarToggle
    {
        protected static PBRemapScenePreviewState State => PBRemapScenePreviewState.Instance;

        protected PBRemapCountToggle(string iconName, string tip)
        {
            icon = PBRemapIcons.Get(iconName);
            tooltip = tip;
            RegisterCallback<AttachToPanelEvent>(_ => { State.PreviewDataChanged += Sync; State.FilterStateChanged += Sync; Sync(); });
            RegisterCallback<DetachFromPanelEvent>(_ => { State.PreviewDataChanged -= Sync; State.FilterStateChanged -= Sync; });
            this.RegisterValueChangedCallback(evt => { SetFlag(evt.newValue); SceneView.RepaintAll(); });
        }

        protected abstract int Count { get; }
        protected abstract bool Flag { get; }
        protected abstract void SetFlag(bool v);

        private void Sync()
        {
            int n = Count;
            text = n > 0 ? n.ToString() : "";
            style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            SetValueWithoutNotify(Flag);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class ResolvedToggle : PBRemapCountToggle
    {
        public const string Id = "PBRemap/Resolved";
        public ResolvedToggle() : base(PBRemapIcons.Resolved, "解決済みの対応を表示") { }
        protected override int Count => State.ResolvedCount;
        protected override bool Flag => State.ShowResolved;
        protected override void SetFlag(bool v) => State.ShowResolved = v;
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class AutoCreateToggle : PBRemapCountToggle
    {
        public const string Id = "PBRemap/AutoCreate";
        public AutoCreateToggle() : base(PBRemapIcons.AutoCreate, "自動作成される対応を表示") { }
        protected override int Count => State.AutoCreatableCount;
        protected override bool Flag => State.ShowAutoCreatable;
        protected override void SetFlag(bool v) => State.ShowAutoCreatable = v;
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class AmbiguousToggle : PBRemapCountToggle
    {
        public const string Id = "PBRemap/Ambiguous";
        public AmbiguousToggle() : base(PBRemapIcons.Ambiguous, "候補が複数ある対応を表示（候補をクリックで確定）") { }
        protected override int Count => State.AmbiguousCount;
        protected override bool Flag => State.ShowAmbiguous;
        protected override void SetFlag(bool v) => State.ShowAmbiguous = v;
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class UnresolvedToggle : PBRemapCountToggle
    {
        public const string Id = "PBRemap/Unresolved";
        public UnresolvedToggle() : base(PBRemapIcons.Unresolved, "対応先が無いボーンを表示（クリックで対応ツール）") { }
        protected override int Count => State.UnresolvedCount;
        protected override bool Flag => State.ShowUnresolved;
        protected override void SetFlag(bool v) => State.ShowUnresolved = v;
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class LinesToggle : EditorToolbarToggle
    {
        public const string Id = "PBRemap/Lines";
        public LinesToggle()
        {
            icon = PBRemapIcons.Get(PBRemapIcons.Linked);
            tooltip = "移植元 → 移植先 の線を表示";
            SetValueWithoutNotify(PBRemapScenePreviewState.Instance.ShowConnectionLines);
            this.RegisterValueChangedCallback(evt => { PBRemapScenePreviewState.Instance.ShowConnectionLines = evt.newValue; SceneView.RepaintAll(); });
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class LabelsToggle : EditorToolbarToggle
    {
        public const string Id = "PBRemap/Labels";
        public LabelsToggle()
        {
            icon = PBRemapIcons.Get(PBRemapIcons.Labels);
            tooltip = "全ての骨の名前を表示（通常は問題のある対応と、マウスを乗せた対応だけ）";
            SetValueWithoutNotify(PBRemapScenePreviewState.Instance.ShowBoneLabels);
            this.RegisterValueChangedCallback(evt => { PBRemapScenePreviewState.Instance.ShowBoneLabels = evt.newValue; SceneView.RepaintAll(); });
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class PickToolButton : EditorToolbarToggle
    {
        public const string Id = "PBRemap/PickTool";
        public PickToolButton()
        {
            icon = PBRemapIcons.Get(PBRemapIcons.Pick);
            tooltip = "ボーン対応ツール: 未解決の骨をクリック → 移植先の骨をクリックで決める（Esc で終了）";
            RegisterCallback<AttachToPanelEvent>(_ => { ToolManager.activeToolChanged += Sync; Sync(); });
            RegisterCallback<DetachFromPanelEvent>(_ => ToolManager.activeToolChanged -= Sync);
            this.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    var def = PBRemapScenePreviewState.Instance.Definition;
                    if (def != null && Selection.activeGameObject != def.gameObject) Selection.activeGameObject = def.gameObject;
                    ToolManager.SetActiveTool<PBRemapBoneMapTool>();
                }
                else if (ToolManager.activeToolType == typeof(PBRemapBoneMapTool)) ToolManager.RestorePreviousTool();
            });
        }
        private void Sync() => SetValueWithoutNotify(ToolManager.activeToolType == typeof(PBRemapBoneMapTool));
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    public class CloseButton : EditorToolbarButton
    {
        public const string Id = "PBRemap/Close";
        public CloseButton()
        {
            icon = PBRemapIcons.Get(PBRemapIcons.EyeOff);
            tooltip = "プレビューを閉じる";
            clicked += () =>
            {
                if (ToolManager.activeToolType == typeof(PBRemapBoneMapTool)) ToolManager.RestorePreviousTool();
                PBRemapScenePreviewState.Instance.Deactivate();
            };
        }
    }
}
