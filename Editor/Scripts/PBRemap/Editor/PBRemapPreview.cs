using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 移植プレビューの結果データ
    /// </summary>
    public class PBRemapPreviewData
    {
        public List<BoneMapping> BoneMappings { get; set; } = new List<BoneMapping>();
        public int TotalPhysBones { get; set; }
        public int TotalPhysBoneColliders { get; set; }
        public int TotalConstraints { get; set; }
        public int TotalContacts { get; set; }
        public int ResolvedBones { get; set; }
        public int UnresolvedBones { get; set; }
        public int AutoCreatableBones { get; set; }
        public int AmbiguousBones { get; set; }
        public float CalculatedScaleFactor { get; set; } = 1.0f;
        public string ScaleMethod { get; set; } = "";
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        /// <summary>解決計画（副作用なし）。UIの手動マッピング等に使う</summary>
        public ResolutionPlan Plan { get; set; }
    }

    /// <summary>
    /// 移植プレビュー生成ロジック。
    /// 解決計画（<see cref="PBRemapResolver"/>）を読み取り専用で作り、表示用データに変換する。
    /// </summary>
    public static class PBRemapPreview
    {
        /// <summary>
        /// PBRemapとDetectionResultに基づき移植プレビューを生成する。副作用は一切ない。
        /// </summary>
        public static PBRemapPreviewData GeneratePreview(PBRemap definition, SourceDetector.DetectionResult detection)
        {
            var preview = new PBRemapPreviewData();
            if (definition == null) { preview.Warnings.Add("PBRemapがnullです"); return preview; }
            if (detection == null) { preview.Warnings.Add("検出結果がnullです"); return preview; }

            var definitionRoot = definition.transform;
            preview.TotalPhysBones = definitionRoot.GetComponentsInChildren<VRCPhysBone>(true).Length;
            preview.TotalPhysBoneColliders = definitionRoot.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
            preview.TotalConstraints = definitionRoot.GetComponentsInChildren<VRCConstraintBase>(true).Length;
            preview.TotalContacts = definitionRoot.GetComponentsInChildren<ContactBase>(true).Length;

            if (detection.DestinationAvatar == null)
            {
                preview.Warnings.Add("デスティネーションアバターが検出できません");
                return preview;
            }

            var plan = PBRemapper.Plan(definition, detection.Situation);
            preview.Plan = plan;
            preview.CalculatedScaleFactor = plan.WorldScaleRatio;
            preview.ScaleMethod = plan.ScaleMethod;
            preview.Warnings.AddRange(plan.Warnings);
            preview.Errors.AddRange(plan.Errors);

            var destRoot = detection.DestinationAvatar.transform;
            var destArmature = detection.DestAvatarData != null ? detection.DestAvatarData.Armature.transform : destRoot;
            var sourceRoot = detection.SourceAvatar != null ? detection.SourceAvatar.transform : null;
            var sourceArmature = detection.SourceAvatarData != null ? detection.SourceAvatarData.Armature.transform : sourceRoot;

            // 表示は「移植元ボーン」単位でまとめる（同じボーンを複数コンポーネントが参照していても1行）
            var seen = new Dictionary<string, BoneMapping>();
            foreach (var res in plan.Resolutions)
            {
                string key = res.SourceKey;
                if (seen.TryGetValue(key, out var existing))
                {
                    existing.referenceKey += ", " + res.Ref.componentPath + "." + res.Ref.propertyPath;
                    continue;
                }

                var m = new BoneMapping
                {
                    sourceBonePath = res.SourceDisplayPath,
                    referenceKey = res.Ref.componentPath + "." + res.Ref.propertyPath,
                    sourceKey = res.SourceKey,
                    method = res.Method.ToString(),
                    destinationTransform = res.Target,
                    autoCreateParentTransform = res.AutoCreateParent,
                    isOuter = res.IsOuter,
                    manual = res.Status == ResolutionStatus.Manual,
                    candidateTransforms = new List<Transform>(res.Candidates),
                };
                // Live なら移植元Transformを引く（SceneView描画用）
                if (sourceRoot != null)
                {
                    var srcCtx = plan.Manifest?.GetContext(res.Ref.contextId);
                    // 外側コンテキスト（アバター内衣装に対するアバター）のパスは外側ルート基準
                    Transform baseRoot = sourceRoot;
                    if (srcCtx != null && srcCtx.scope == BoneContextScope.Outer && detection.Situation?.Source?.Outer != null)
                        baseRoot = detection.Situation.Source.Outer.transform;
                    Transform ctxArmature = srcCtx != null && !string.IsNullOrEmpty(srcCtx.armaturePathFromRoot) ? baseRoot.Find(srcCtx.armaturePathFromRoot) : baseRoot;
                    if (srcCtx != null && string.IsNullOrEmpty(srcCtx.armaturePathFromRoot)) ctxArmature = baseRoot;
                    m.sourceTransform = ctxArmature != null && !string.IsNullOrEmpty(res.Ref.relPath) ? ctxArmature.Find(res.Ref.relPath) : null;
                    if (m.sourceTransform == null && !string.IsNullOrEmpty(res.Ref.pathFromRoot)) m.sourceTransform = baseRoot.Find(res.Ref.pathFromRoot);
                }

                switch (res.Status)
                {
                    case ResolutionStatus.Resolved:
                    case ResolutionStatus.Manual:
                        m.resolved = true;
                        m.destinationBonePath = DisplayPath(res.Target, destArmature, destRoot);
                        preview.ResolvedBones++;
                        break;
                    case ResolutionStatus.AutoCreate:
                        m.resolved = false;
                        m.autoCreatable = true;
                        m.autoCreateDestPath = (res.AutoCreateParent != null ? DisplayPath(res.AutoCreateParent, destArmature, destRoot) + "/" : "") + res.Ref.boneName;
                        m.errorMessage = res.Message;
                        preview.AutoCreatableBones++;
                        preview.UnresolvedBones++;
                        break;
                    case ResolutionStatus.Ambiguous:
                        m.resolved = false;
                        m.ambiguous = true;
                        m.errorMessage = res.Message + ": " + string.Join(", ", res.Candidates.Select(c => DisplayPath(c, destArmature, destRoot)));
                        m.destinationBonePath = "";
                        preview.AmbiguousBones++;
                        preview.UnresolvedBones++;
                        break;
                    default:
                        m.resolved = false;
                        m.errorMessage = res.Message;
                        m.destinationBonePath = "";
                        preview.UnresolvedBones++;
                        break;
                }
                seen[key] = m;
                preview.BoneMappings.Add(m);
            }

            int trueUnresolved = preview.UnresolvedBones - preview.AutoCreatableBones;
            if (trueUnresolved > 0)
                preview.Warnings.Add($"{trueUnresolved} 個のボーンが解決できませんでした。パスリマップルールの追加、または手動マッピングで解決してください。");

            return preview;
        }

        private static string DisplayPath(Transform t, Transform armature, Transform root)
        {
            if (t == null) return "";
            var p = armature != null ? BoneMapper.GetRelativePath(t, armature) : null;
            if (p != null) return p;
            p = root != null ? BoneMapper.GetRelativePath(t, root) : null;
            return p ?? t.name;
        }
    }
}
