using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace colloid.PBReplacer
{
    public class TransplantPreviewWindow : EditorWindow
    {
        private TransplantDefinition _definition;
        private SourceDetector.DetectionResult _detection;
        private TransplantPreviewData _preview;

        private Label _summaryLabel;
        private ScrollView _boneScrollView;
        private VisualElement _warningsContainer;

        public static TransplantPreviewWindow Open(
            TransplantDefinition definition,
            SourceDetector.DetectionResult detection)
        {
            var window = GetWindow<TransplantPreviewWindow>(true, "移植プレビュー");
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
            _preview = TransplantPreview.GeneratePreview(_definition, _detection);
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

            var styleSheet = Resources.Load<StyleSheet>("USS/TransplantDefinition");
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            _summaryLabel = new Label();
            _summaryLabel.AddToClassList("preview-summary-label");
            root.Add(_summaryLabel);

            _boneScrollView = new ScrollView(ScrollViewMode.Vertical);
            _boneScrollView.style.flexGrow = 1;
            root.Add(_boneScrollView);

            _warningsContainer = new VisualElement();
            root.Add(_warningsContainer);

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
