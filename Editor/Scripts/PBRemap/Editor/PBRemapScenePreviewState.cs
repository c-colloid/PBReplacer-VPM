using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>ボーン対応1件の見え方の種類</summary>
    public enum BoneVisualStatus
    {
        Resolved,
        Manual,
        AutoCreate,
        Ambiguous,
        Unresolved,
    }

    /// <summary>
    /// SceneViewプレビューの共有状態を管理するシングルトン。
    /// プレビューデータ・表示フィルタ・選択/ホバー中の対応を保持し、Inspector / SceneView オーバーレイ / ツールで共有する。
    /// </summary>
    public class PBRemapScenePreviewState
    {
        private static PBRemapScenePreviewState _instance;
        public static PBRemapScenePreviewState Instance =>
            _instance ??= new PBRemapScenePreviewState();

        /// <summary>フィルター状態が変更された時に発火するイベント</summary>
        public event Action FilterStateChanged;

        /// <summary>プレビューデータが変更された時に発火するイベント（Activate/Deactivate）</summary>
        public event Action PreviewDataChanged;

        public bool IsActive { get; private set; }

        public PBRemapPreviewData PreviewData { get; private set; }
        public SourceDetector.DetectionResult Detection { get; private set; }
        /// <summary>プレビュー対象の PBRemap（手動対応の書き込み先）</summary>
        public PBRemap Definition { get; private set; }

        /// <summary>ワールド座標解決済みのボーンマッピングキャッシュ</summary>
        public List<BoneMappingVisual> VisualMappings { get; private set; } = new List<BoneMappingVisual>();

        // 表示設定
        public bool ShowConnectionLines { get; set; } = true;
        /// <summary>全ての対応に名前ラベルを出す（既定は問題のある対応とホバー/選択中だけ）</summary>
        public bool ShowBoneLabels { get; set; } = false;

        private bool _showResolved = true;
        private bool _showAutoCreatable = true;
        private bool _showAmbiguous = true;
        private bool _showUnresolved = true;

        public bool ShowResolved
        {
            get => _showResolved;
            set { if (_showResolved != value) { _showResolved = value; FilterStateChanged?.Invoke(); } }
        }

        public bool ShowAutoCreatable
        {
            get => _showAutoCreatable;
            set { if (_showAutoCreatable != value) { _showAutoCreatable = value; FilterStateChanged?.Invoke(); } }
        }

        public bool ShowAmbiguous
        {
            get => _showAmbiguous;
            set { if (_showAmbiguous != value) { _showAmbiguous = value; FilterStateChanged?.Invoke(); } }
        }

        public bool ShowUnresolved
        {
            get => _showUnresolved;
            set { if (_showUnresolved != value) { _showUnresolved = value; FilterStateChanged?.Invoke(); } }
        }

        /// <summary>選択中の対応（手動対応ツールで「次にクリックするボーンを割り当てる」対象）</summary>
        public string SelectedKey { get; set; }
        /// <summary>マウスが乗っている対応</summary>
        public string HoverKey { get; set; }

        // サマリー
        public int ResolvedCount { get; private set; }
        public int AutoCreatableCount { get; private set; }
        public int AmbiguousCount { get; private set; }
        public int UnresolvedCount { get; private set; }
        public int TotalCount { get; private set; }
        public int ProblemCount => AmbiguousCount + UnresolvedCount;

        public bool IsVisible(BoneMappingVisual v)
        {
            switch (v.Status)
            {
                case BoneVisualStatus.Resolved: return ShowResolved;
                case BoneVisualStatus.Manual: return true; // 自分で決めた対応は常に見せる
                case BoneVisualStatus.AutoCreate: return ShowAutoCreatable;
                case BoneVisualStatus.Ambiguous: return ShowAmbiguous;
                default: return ShowUnresolved;
            }
        }

        /// <summary>
        /// プレビューデータと検出結果を設定し、ビジュアルキャッシュを再構築する。
        /// </summary>
        public void Activate(PBRemapPreviewData previewData, SourceDetector.DetectionResult detection, PBRemap definition = null)
        {
            PreviewData = previewData;
            Detection = detection;
            if (definition != null) Definition = definition;
            IsActive = true;
            RebuildVisualCache();
            if (SelectedKey != null && VisualMappings.All(v => v.SourceKey != SelectedKey)) SelectedKey = null;
            PreviewDataChanged?.Invoke();
        }

        /// <summary>
        /// プレビューを非アクティブにし、キャッシュをクリアする。
        /// </summary>
        public void Deactivate()
        {
            IsActive = false;
            PreviewData = null;
            Detection = null;
            Definition = null;
            SelectedKey = null;
            HoverKey = null;
            VisualMappings.Clear();
            ResolvedCount = AutoCreatableCount = AmbiguousCount = UnresolvedCount = TotalCount = 0;
            SceneView.RepaintAll();
            PreviewDataChanged?.Invoke();
        }

        /// <summary>対象の PBRemap を再評価してプレビューを作り直す（手動対応の後など）</summary>
        public void Refresh()
        {
            if (Definition == null) { if (IsActive) Deactivate(); return; }
            var det = SourceDetector.Detect(Definition);
            if (det.IsFailure || !det.Value.IsLiveMode) { Deactivate(); return; }
            var preview = PBRemapPreview.GeneratePreview(Definition, det.Value);
            Activate(preview, det.Value, Definition);
        }

        /// <summary>手動対応を書き込み、プレビューを更新する</summary>
        public void AssignManual(string sourceKey, Transform target)
        {
            if (Definition == null || string.IsNullOrEmpty(sourceKey)) return;
            var v = VisualMappings.FirstOrDefault(x => x.SourceKey == sourceKey);
            PBRemapManualMapping.Set(Definition, sourceKey, v?.SourcePath, target, Detection?.DestinationAvatar);
            Refresh();
        }

        /// <summary>次の問題（要選択/未解決）の対応キー。無ければ null</summary>
        public string NextProblemKey(string after = null)
        {
            var problems = VisualMappings.Where(v => v.Status == BoneVisualStatus.Ambiguous || v.Status == BoneVisualStatus.Unresolved).Select(v => v.SourceKey).ToList();
            if (problems.Count == 0) return null;
            int i = after != null ? problems.IndexOf(after) : -1;
            return problems[(i + 1) % problems.Count];
        }

        /// <summary>
        /// BoneMappingのパスからTransformを解決し、ワールド座標をキャッシュする。
        /// Live Modeでのみ有効。
        /// </summary>
        public void RebuildVisualCache()
        {
            VisualMappings.Clear();
            ResolvedCount = AutoCreatableCount = AmbiguousCount = UnresolvedCount = TotalCount = 0;

            if (PreviewData == null || Detection == null) return;
            if (!Detection.IsLiveMode) return;

            var sourceArmature = Detection.SourceAvatarData != null ? Detection.SourceAvatarData.Armature.transform : null;
            var destArmature = Detection.DestAvatarData != null ? Detection.DestAvatarData.Armature.transform : null;

            foreach (var mapping in PreviewData.BoneMappings)
            {
                var visual = new BoneMappingVisual
                {
                    SourceKey = mapping.sourceKey,
                    SourcePath = mapping.sourceBonePath,
                    DestPath = mapping.destinationBonePath,
                    Message = mapping.errorMessage,
                    IsOuter = mapping.isOuter,
                    Candidates = mapping.candidateTransforms ?? new List<Transform>(),
                };
                visual.SourceTransform = mapping.sourceTransform != null
                    ? mapping.sourceTransform
                    : (sourceArmature != null ? BoneMapper.FindBoneByRelativePath(mapping.sourceBonePath, sourceArmature) : null);
                if (visual.SourceTransform == null) continue;

                if (mapping.resolved)
                {
                    visual.DestTransform = mapping.destinationTransform != null
                        ? mapping.destinationTransform
                        : (destArmature != null ? BoneMapper.FindBoneByRelativePath(mapping.destinationBonePath, destArmature) : null);
                    visual.Status = mapping.manual ? BoneVisualStatus.Manual : BoneVisualStatus.Resolved;
                    ResolvedCount++;
                }
                else if (mapping.autoCreatable)
                {
                    visual.AutoCreateParentTransform = mapping.autoCreateParentTransform;
                    if (visual.AutoCreateParentTransform == null && destArmature != null && !string.IsNullOrEmpty(mapping.autoCreateDestPath))
                    {
                        int lastSlash = mapping.autoCreateDestPath.LastIndexOf('/');
                        string parentDestPath = lastSlash >= 0 ? mapping.autoCreateDestPath.Substring(0, lastSlash) : "";
                        visual.AutoCreateParentTransform = BoneMapper.FindBoneByRelativePath(parentDestPath, destArmature);
                    }
                    visual.Status = BoneVisualStatus.AutoCreate;
                    AutoCreatableCount++;
                }
                else if (mapping.ambiguous)
                {
                    visual.Status = BoneVisualStatus.Ambiguous;
                    AmbiguousCount++;
                }
                else
                {
                    visual.Status = BoneVisualStatus.Unresolved;
                    UnresolvedCount++;
                }
                TotalCount++;
                VisualMappings.Add(visual);
            }

            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// 1つのボーンマッピングのSceneView描画用キャッシュ
    /// </summary>
    public class BoneMappingVisual
    {
        public string SourceKey;
        public string SourcePath;
        public string DestPath;
        public BoneVisualStatus Status;
        public string Message;
        public bool IsOuter;
        public Transform SourceTransform;
        public Transform DestTransform;
        public Transform AutoCreateParentTransform;
        public List<Transform> Candidates = new List<Transform>();

        public bool Resolved => Status == BoneVisualStatus.Resolved || Status == BoneVisualStatus.Manual;
        public bool AutoCreatable => Status == BoneVisualStatus.AutoCreate;
        public bool IsProblem => Status == BoneVisualStatus.Ambiguous || Status == BoneVisualStatus.Unresolved;
        public string SourceName { get { int i = (SourcePath ?? "").LastIndexOf('/'); return i >= 0 ? SourcePath.Substring(i + 1) : SourcePath; } }
    }
}
