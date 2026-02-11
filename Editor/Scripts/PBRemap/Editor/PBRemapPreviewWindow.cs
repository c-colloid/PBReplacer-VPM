using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace colloid.PBReplacer
{
    public class PBRemapPreviewWindow : EditorWindow
    {
        private PBRemapDefinition _definition;
        private SourceDetector.DetectionResult _detection;
        private PBRemapPreviewData _preview;

        private Label _summaryLabel;
        private ScrollView _boneScrollView;
        private VisualElement _warningsContainer;

        public static PBRemapPreviewWindow Open(
            PBRemapDefinition definition,
            SourceDetector.DetectionResult detection)
        {
            var window = GetWindow<PBRemapPreviewWindow>(true, "移植プレビュー");
            window.minSize = new Vector2(520, 300);
            window._definition = definition;
            window._detection = detection;
            window.RefreshPreview();
            return window;
        }

        public void RefreshPreview()
        {
            if (_definition == null || _detection == null)
                return;
            _preview = PBRemapPreview.GeneratePreview(_definition, _detection);
            Rebuild();
        }

        public void UpdateDetection(SourceDetector.DetectionResult detection)
        {
            _detection = detection;
            RefreshPreview();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingTop = 4;
            root.style.paddingBottom = 4;

            // UXMLをロード
            var visualTree = Resources.Load<VisualTreeAsset>("UXML/PBRemapPreview");
            if (visualTree != null)
            {
                visualTree.CloneTree(root);
            }

            // USSをロード（UXMLから読めない場合のフォールバック）
            var styleSheet = Resources.Load<StyleSheet>("USS/PBRemapDefinition");
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // 要素を取得
            _summaryLabel = root.Q<Label>("preview-summary");
            _boneScrollView = root.Q<ScrollView>("preview-bone-scroll");
            _warningsContainer = root.Q<VisualElement>("preview-warnings");

            if (_preview != null)
                Rebuild();
        }

        private void Rebuild()
        {
            if (_summaryLabel == null)
                return;

            if (_preview == null)
            {
                _summaryLabel.text = "";
                _boneScrollView.Clear();
                _warningsContainer.Clear();
                return;
            }

            int totalBones = _preview.ResolvedBones + _preview.UnresolvedBones;
            _summaryLabel.text =
                $"PB: {_preview.TotalPhysBones}  PBC: {_preview.TotalPhysBoneColliders}  " +
                $"Constraint: {_preview.TotalConstraints}  Contact: {_preview.TotalContacts}\n" +
                $"ボーン解決: {_preview.ResolvedBones}/{totalBones}  |  " +
                $"スケール: {_preview.CalculatedScaleFactor:F3}";

            _boneScrollView.Clear();
            foreach (var mapping in _preview.BoneMappings)
            {
                var row = new VisualElement();
                row.AddToClassList("preview-bone-item");

                var sourceLabel = new Label(mapping.sourceBonePath);
                sourceLabel.tooltip = mapping.sourceBonePath;
                row.Add(sourceLabel);

                var arrow = new Label("\u2192");
                arrow.AddToClassList("preview-bone-arrow");
                row.Add(arrow);

                var destLabel = new Label();
                row.Add(destLabel);

                if (mapping.resolved)
                {
                    destLabel.text = mapping.destinationBonePath;
                    destLabel.tooltip = mapping.destinationBonePath;
                    row.AddToClassList("preview-bone-resolved");
                }
                else
                {
                    destLabel.text = mapping.errorMessage ?? "未解決";
                    row.AddToClassList("preview-bone-unresolved");
                }

                _boneScrollView.Add(row);
            }

            _warningsContainer.Clear();
            foreach (var warning in _preview.Warnings)
            {
                var row = new VisualElement();
                row.AddToClassList("preview-warning-item");
                var label = new Label(warning);
                label.style.whiteSpace = WhiteSpace.Normal;
                row.Add(label);
                _warningsContainer.Add(row);
            }
        }
    }
}
