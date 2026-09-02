using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>参照1件の解決状態</summary>
    public enum ResolutionStatus
    {
        /// <summary>移植先ボーンに一意に解決した</summary>
        Resolved,
        /// <summary>移植先に存在しないが、親が解決済みで自動作成できる（非スケルトンボーン）</summary>
        AutoCreate,
        /// <summary>複数候補があり自動確定できない</summary>
        Ambiguous,
        /// <summary>解決できない</summary>
        Unresolved,
        /// <summary>ボーンではない参照（PBRemap外のコンポーネント等）で、移植先に対応物が無い</summary>
        ExternalObject,
        /// <summary>ユーザーの手動マッピングで確定</summary>
        Manual,
    }

    /// <summary>解決に使った方法（UI表示・デバッグ用）</summary>
    public enum ResolutionMethod
    {
        None,
        Manual,
        Humanoid,
        HumanoidAncestorPath,
        CostumeContextPath,
        NormalizedNameInMain,
        ContextPath,
        RemapRulePath,
        UniqueName,
        AutoCreate,
    }

    /// <summary>参照1件の解決結果</summary>
    public class ReferenceResolution
    {
        public BoneRef Ref;
        public ResolutionStatus Status = ResolutionStatus.Unresolved;
        public ResolutionMethod Method = ResolutionMethod.None;
        public Transform Target;
        /// <summary>AutoCreate 時の親（移植先）</summary>
        public Transform AutoCreateParent;
        public List<Transform> Candidates = new List<Transform>();
        public string Message = "";
        /// <summary>この参照に適用するスケール係数（radius等の乗数）</summary>
        public float ScaleFactor = 1f;
        /// <summary>移植元コンテキストが外側の単位（アバター内衣装に対するアバター）のものか</summary>
        public bool IsOuter;

        public string SourceDisplayPath => string.IsNullOrEmpty(Ref.relPath) ? (string.IsNullOrEmpty(Ref.pathFromRoot) ? Ref.boneName : Ref.pathFromRoot) : Ref.relPath;
        public string SourceKey => $"{Ref.contextId}:{Ref.relPath}|{Ref.pathFromRoot}";
    }

    /// <summary>
    /// マニフェストと移植先ルートから作られる解決計画。副作用は無い。
    /// </summary>
    public class ResolutionPlan
    {
        public GameObject DestinationRoot;
        public RootInfo DestinationInfo;
        public List<ContextInfo> DestinationContexts = new List<ContextInfo>();
        public PBRemapManifest Manifest;
        public List<ReferenceResolution> Resolutions = new List<ReferenceResolution>();
        /// <summary>ホーム側（Self）の世界寸法比</summary>
        public float WorldScaleRatio = 1f;
        /// <summary>外側（Outer）参照の世界寸法比</summary>
        public float OuterScaleRatio = 1f;
        public string ScaleMethod = "";
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();
        /// <summary>移植先ホーム配下の Transform（PBRemap 配下を除く）</summary>
        public List<Transform> SelfBones = new List<Transform>();
        /// <summary>移植先の外側単位配下の Transform（ホーム配下を除く）</summary>
        public List<Transform> OuterBones = new List<Transform>();

        public int CountOf(ResolutionStatus s) => Resolutions.Count(r => r.Status == s);
        public int ResolvedCount => Resolutions.Count(r => r.Status == ResolutionStatus.Resolved || r.Status == ResolutionStatus.Manual);
        public int AutoCreateCount => CountOf(ResolutionStatus.AutoCreate);
        public int AmbiguousCount => CountOf(ResolutionStatus.Ambiguous);
        public int UnresolvedCount => CountOf(ResolutionStatus.Unresolved) + CountOf(ResolutionStatus.ExternalObject);
        public bool IsFullyResolved => AmbiguousCount == 0 && UnresolvedCount == 0 && Errors.Count == 0;
        public bool CanApply => Errors.Count == 0 && Resolutions.Count > 0;

        /// <summary>解決済み移植元ボーン(relPath key) → 移植先 Transform（自動作成予定は含まない）</summary>
        public Dictionary<string, Transform> ResolvedByKey =>
            Resolutions.Where(r => r.Target != null).GroupBy(r => r.SourceKey).ToDictionary(g => g.Key, g => g.First().Target);

        public ContextInfo SelfMain => DestinationContexts.FirstOrDefault(c => c.Scope == ContextScope.Self && c.Kind == BoneContextKind.Main);
        public ContextInfo OuterMain => DestinationContexts.FirstOrDefault(c => c.Scope == ContextScope.Outer && c.Kind == BoneContextKind.Main);
        public ContextInfo SelfGeneric => DestinationContexts.FirstOrDefault(c => c.Scope == ContextScope.Self && c.Kind == BoneContextKind.Generic);
        public ContextInfo OuterGeneric => DestinationContexts.FirstOrDefault(c => c.Scope == ContextScope.Outer && c.Kind == BoneContextKind.Generic);
    }

    /// <summary>
    /// マニフェスト × 移植先ルート → 解決計画。
    /// 解決戦略（優先度順）:
    ///   0. 手動マッピング
    ///   1. Humanoid ID（対応する本体がHumanoid）
    ///   2. Humanoid祖先 + 相対パス（ルール/MA正規化を各セグメントに適用）
    ///   3. 同一衣装コンテキスト（衣装名一致）内の相対パス
    ///   4. コンテキストArmature基準の相対パス（ルール・双方向）
    ///   5. MA正規化名（prefix/suffixを剥いだ名前）での本体/衣装横断一致（一意なときのみ）
    ///   6. 名前一致（一意なときのみ、複数なら Ambiguous）
    ///   7. 自動作成（非スケルトンボーンかつ親が解決可能）
    /// 移植元コンテキストの Scope（ホーム自身 / 外側の単位）に応じて、移植先のホーム側/外側のコンテキストへ対応付ける。
    /// </summary>
    public static class PBRemapResolver
    {
        public static ResolutionPlan Resolve(PBRemap definition, GameObject destinationRoot, RootInfo destInfo = null)
        {
            var plan = new ResolutionPlan { DestinationRoot = destinationRoot, Manifest = definition.Manifest };

            if (destinationRoot == null)
            {
                plan.Errors.Add("移植先が特定できません。PBRemapをアバター（または衣装/小物）の子階層に配置するか、手動指定してください。");
                return plan;
            }
            if (definition.Manifest == null || definition.Manifest.IsEmpty)
            {
                plan.Errors.Add("移植元の参照情報（マニフェスト）がありません。移植元のシーンでPBRemapを選択して「参照情報を更新」するか、PBReplacerで再生成してください。");
                return plan;
            }
            if (destInfo == null || destInfo.Root != destinationRoot)
                destInfo = RootInfo.For(destinationRoot, destInfo != null ? destInfo.Method : AvatarDetectionMethod.None);
            plan.DestinationInfo = destInfo;

            var manifest = definition.Manifest;
            plan.DestinationContexts = PBRemapContextResolver.BuildContexts(destInfo);
            var rules = definition.PathRemapRules?.ToList() ?? new List<PathRemapRule>();
            var manual = definition.MappingOverrides ?? new List<ManualMapping>();
            var homeT = destinationRoot.transform;
            plan.SelfBones = homeT.GetComponentsInChildren<Transform>(true)
                .Where(t => t != definition.transform && !t.IsChildOf(definition.transform)).ToList();
            if (destInfo.Outer != null && destInfo.Outer != destinationRoot)
                plan.OuterBones = destInfo.Outer.transform.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != homeT && !t.IsChildOf(homeT)).ToList();

            // 参照ごとに解決
            foreach (var r in manifest.refs)
            {
                var srcCtx = manifest.GetContext(r.contextId);
                var res = new ReferenceResolution { Ref = r, IsOuter = srcCtx != null && srcCtx.scope == BoneContextScope.Outer };
                plan.Resolutions.Add(res);

                // コンポーネント参照（ボーン外のPBC等）: 移植先に同じ相対パス＋型があればそれ、無ければ ExternalObject
                if (!string.IsNullOrEmpty(r.targetComponentType))
                {
                    ResolveComponentReference(res, r, srcCtx, plan, rules, manual);
                    continue;
                }

                // 0. 手動
                var mm = manual.FirstOrDefault(m => m.sourceKey == res.SourceKey && (m.target != null || !string.IsNullOrEmpty(m.targetPathFromRoot)));
                if (mm != null)
                {
                    var t = mm.target != null ? mm.target : destinationRoot.transform.Find(mm.targetPathFromRoot);
                    if (t != null) { res.Target = t; res.Status = ResolutionStatus.Manual; res.Method = ResolutionMethod.Manual; continue; }
                }

                if (srcCtx == null)
                {
                    // ルート外参照（別アバターへの参照等）
                    res.Status = ResolutionStatus.ExternalObject;
                    res.Message = $"移植元ルートの外にあるオブジェクト '{r.boneName}' への参照です";
                    continue;
                }

                var target = ResolveBone(r, srcCtx, plan, rules, out var method, out var candidates);
                if (target != null)
                {
                    res.Target = target; res.Status = ResolutionStatus.Resolved; res.Method = method;
                    continue;
                }
                if (candidates.Count > 1)
                {
                    res.Status = ResolutionStatus.Ambiguous; res.Candidates = candidates;
                    res.Message = $"同名ボーン '{r.boneName}' が {candidates.Count} 件あります。手動で選択してください";
                    continue;
                }

                // 7. 自動作成
                if (!r.isSkeletonBone && !string.IsNullOrEmpty(r.relPath) && r.relPath.Contains("/"))
                {
                    var parentRef = ParentOf(r);
                    var parent = ResolveBone(parentRef, srcCtx, plan, rules, out _, out var pc);
                    if (parent != null)
                    {
                        res.Status = ResolutionStatus.AutoCreate; res.Method = ResolutionMethod.AutoCreate; res.AutoCreateParent = parent;
                        res.Message = $"'{parent.name}' 配下に '{r.boneName}' を自動作成します";
                        continue;
                    }
                }
                else if (!r.isSkeletonBone && !string.IsNullOrEmpty(r.relPath) && !r.relPath.Contains("/") && srcCtx.kind != BoneContextKind.Generic)
                {
                    // Armature 直下のヘルパー
                    var destCtx = FindDestContext(srcCtx, plan);
                    if (destCtx != null && destCtx.Armature != null)
                    {
                        res.Status = ResolutionStatus.AutoCreate; res.Method = ResolutionMethod.AutoCreate; res.AutoCreateParent = destCtx.Armature;
                        res.Message = $"'{destCtx.Armature.name}' 配下に '{r.boneName}' を自動作成します";
                        continue;
                    }
                }

                res.Status = ResolutionStatus.Unresolved;
                if (srcCtx.scope == BoneContextScope.Outer && plan.OuterMain == null && plan.SelfMain == null)
                    res.Message = $"ボーン '{r.boneName}' は移植元の外側（{manifest.outerRootName}）のものですが、移植先の外側にアバターがありません";
                else
                    res.Message = r.isSkeletonBone
                        ? $"ボーン '{r.boneName}' に対応する移植先ボーンが見つかりません（スケルトンボーンのため自動作成不可）"
                        : $"ボーン '{r.boneName}' に対応する移植先ボーンが見つかりません";
            }

            // 参照解決の後にスケール（解決済みペアを使う）
            ComputeScale(definition, plan);

            // 検証系の警告
            if (destInfo != null && destInfo.Kind == RootKind.Generic)
                plan.Warnings.Add($"移植先 '{destinationRoot.name}' は汎用オブジェクトとして扱います（Descriptor/Animator/MergeArmature無し）。");
            bool srcHasSelfCostume = manifest.contexts.Any(c => c.kind == BoneContextKind.Costume && c.scope == BoneContextScope.Self && manifest.refs.Any(r => r.contextId == c.id));
            if (srcHasSelfCostume && !plan.DestinationContexts.Any(c => c.Kind == BoneContextKind.Costume))
                plan.Warnings.Add("移植元には衣装（MergeArmature）コンテキストがありますが、移植先に衣装がありません。衣装ボーンは本体Armature上で名前解決されます。");
            if (manifest.refs.Any(r => r.componentType.Contains("Constraint")))
                plan.Warnings.Add("VRC Constraint は参照の付け替えのみ行い、位置/回転オフセットは再計算しません。移植先でボーンの向きが異なる場合は Constraint のオフセットを再ベイクしてください。");

            return plan;
        }

        #region bone resolution

        private static BoneRef ParentOf(BoneRef r)
        {
            int i = r.relPath.LastIndexOf('/');
            var parentRel = i >= 0 ? r.relPath.Substring(0, i) : "";
            var segs = parentRel.Split('/');
            int j = r.pathFromAncestor.LastIndexOf('/');
            // 親が Humanoid 祖先そのもの（pathFromAncestor にセグメントが1つしか無い）なら、親は Humanoid ID で解決できる
            bool parentIsHumanoidAncestor = r.nearestHumanoidAncestor != HumanBodyBones.LastBone && !string.IsNullOrEmpty(r.pathFromAncestor) && j < 0;
            return new BoneRef
            {
                contextId = r.contextId,
                relPath = parentRel,
                boneName = segs.Length > 0 ? segs[segs.Length - 1] : "",
                humanBone = parentIsHumanoidAncestor ? r.nearestHumanoidAncestor : HumanBodyBones.LastBone,
                nearestHumanoidAncestor = parentIsHumanoidAncestor ? HumanBodyBones.LastBone : r.nearestHumanoidAncestor,
                pathFromAncestor = j > 0 ? r.pathFromAncestor.Substring(0, j) : "",
                pathFromRoot = r.pathFromRoot.Contains("/") ? r.pathFromRoot.Substring(0, r.pathFromRoot.LastIndexOf('/')) : "",
                isSkeletonBone = true,
            };
        }

        private static ContextInfo MatchCostume(BoneContext srcCtx, List<ContextInfo> costumes, ResolutionPlan plan)
        {
            if (costumes == null || costumes.Count == 0) return null;
            var byName = costumes.Where(c => !string.IsNullOrEmpty(srcCtx.costumeName) && c.CostumeName == srcCtx.costumeName).ToList();
            if (byName.Count == 1) return byName[0];
            if (byName.Count > 1)
            {
                AddWarningOnce(plan, $"移植先に同名の衣装 '{srcCtx.costumeName}' が {byName.Count} 着あります。最初の1着を使います。特定の衣装へ移す場合は、その衣装の配下へ直接ドロップしてください。");
                return byName[0];
            }
            if (costumes.Count == 1)
            {
                // ホームが衣装そのもの（衣装の配下へドロップ）なら名前が違っても意図は明確。アバター配下で唯一の衣装に名前不一致で当てる場合は警告
                if (costumes[0].Scope == ContextScope.Self && plan.DestinationInfo != null && plan.DestinationInfo.Kind == RootKind.MACostume) return costumes[0];
                AddWarningOnce(plan, $"移植元の衣装 '{srcCtx.costumeName}' と移植先の衣装 '{costumes[0].CostumeName}' は名前が異なります。移植先に衣装が1着だけなのでその衣装へ対応付けます（意図と異なる場合は衣装の配下へ直接ドロップしてください）。");
                return costumes[0];
            }
            // 複数の衣装があり名前で特定できない: prefix/suffix が一致するものが1着だけならそれ。複数なら決めない（先頭を黙って採用しない）
            var byAffix = costumes.Where(c => c.MaPrefix == srcCtx.maPrefix && c.MaSuffix == srcCtx.maSuffix).ToList();
            if (byAffix.Count == 1) return byAffix[0];
            AddWarningOnce(plan, $"移植先に衣装が {costumes.Count} 着ありますが、移植元の衣装 '{srcCtx.costumeName}' に対応する衣装を特定できません。対象の衣装の配下へ直接ドロップするか、表で手動対応付けしてください。");
            return null;
        }

        private static void AddWarningOnce(ResolutionPlan plan, string text)
        {
            if (!plan.Warnings.Contains(text)) plan.Warnings.Add(text);
        }

        /// <summary>
        /// 移植元コンテキストに対応する移植先コンテキストを返す。
        /// Scope（ホーム自身/外側）と種別に応じて、移植先のホーム側/外側へ対応付ける。
        ///   Self/Main   → 移植先ホームの本体 → 外側の本体（アバター級のAvatarDynamicsを衣装へ落とした場合）→ ホームの単一衣装 → ホーム
        ///   Self/Costume→ ホーム側の衣装（名前/単一/prefix）→ 外側の衣装 → ホームの本体（S14: 衣装→本体）→ 外側の本体 → ホーム
        ///   Outer/Main  → 移植先の外側の本体 → ホームの本体（衣装のAvatarDynamicsをアバター直下へ落とした場合）→ 外側 → ホーム
        ///   Outer/Costume（兄弟衣装）→ 外側の衣装 → ホーム側の衣装 → 外側の本体 → ホームの本体
        ///   Generic     → 同じ側の Generic
        /// </summary>
        public static ContextInfo FindDestContext(BoneContext srcCtx, ResolutionPlan plan)
        {
            var ctxs = plan.DestinationContexts;
            var selfCostumes = ctxs.Where(c => c.Scope == ContextScope.Self && c.Kind == BoneContextKind.Costume).ToList();
            var outerCostumes = ctxs.Where(c => c.Scope == ContextScope.Outer && c.Kind == BoneContextKind.Costume).ToList();
            bool srcOuter = srcCtx.scope == BoneContextScope.Outer;
            switch (srcCtx.kind)
            {
                case BoneContextKind.Main:
                    return srcOuter
                        ? plan.OuterMain ?? plan.SelfMain ?? plan.OuterGeneric ?? plan.SelfGeneric
                        : plan.SelfMain ?? plan.OuterMain ?? (selfCostumes.Count == 1 && plan.DestinationInfo != null && plan.DestinationInfo.Kind == RootKind.MACostume ? selfCostumes[0] : null) ?? plan.SelfGeneric;
                case BoneContextKind.Costume:
                    return srcOuter
                        ? MatchCostume(srcCtx, outerCostumes, plan) ?? MatchCostume(srcCtx, selfCostumes, plan) ?? plan.OuterMain ?? plan.SelfMain ?? plan.SelfGeneric
                        : MatchCostume(srcCtx, selfCostumes, plan) ?? MatchCostume(srcCtx, outerCostumes, plan) ?? plan.SelfMain ?? plan.OuterMain ?? plan.SelfGeneric;
                default:
                    return srcOuter ? plan.OuterGeneric ?? plan.SelfGeneric : plan.SelfGeneric;
            }
        }

        /// <summary>Humanoid ID 解決に使う本体コンテキスト（移植元の Scope に近い側を優先）</summary>
        private static ContextInfo PickHumanoidMain(BoneContext srcCtx, ResolutionPlan plan)
        {
            var self = plan.SelfMain; var outer = plan.OuterMain;
            if (self != null && !self.IsHumanoid) self = null;
            if (outer != null && !outer.IsHumanoid) outer = null;
            return srcCtx.scope == BoneContextScope.Outer ? outer ?? self : self ?? outer;
        }

        private static ContextInfo MainOfSameSide(ContextInfo destCtx, ResolutionPlan plan)
        {
            if (destCtx == null) return plan.SelfMain ?? plan.OuterMain;
            return destCtx.Scope == ContextScope.Outer ? plan.OuterMain ?? plan.SelfMain : plan.SelfMain ?? plan.OuterMain;
        }

        private static List<Transform> BonesOfSide(ContextInfo destCtx, ResolutionPlan plan)
        {
            if (destCtx != null && destCtx.Scope == ContextScope.Outer && plan.OuterBones.Count > 0) return plan.OuterBones;
            return plan.SelfBones;
        }

        private static Transform ResolveBone(BoneRef r, BoneContext srcCtx, ResolutionPlan plan, List<PathRemapRule> rules,
            out ResolutionMethod method, out List<Transform> candidates)
        {
            method = ResolutionMethod.None;
            candidates = new List<Transform>();
            var destCtx = FindDestContext(srcCtx, plan);
            var humanoidMain = PickHumanoidMain(srcCtx, plan);

            // 1. Humanoid ID
            if (r.humanBone != HumanBodyBones.LastBone && humanoidMain != null)
            {
                var b = humanoidMain.Animator.GetBoneTransform(r.humanBone);
                if (b != null) { method = ResolutionMethod.Humanoid; return b; }
            }

            // 2. Humanoid祖先 + 相対パス
            if (r.nearestHumanoidAncestor != HumanBodyBones.LastBone && !string.IsNullOrEmpty(r.pathFromAncestor) && humanoidMain != null)
            {
                var anc = humanoidMain.Animator.GetBoneTransform(r.nearestHumanoidAncestor);
                if (anc != null)
                {
                    var t = FindByPathWithVariants(anc, r.pathFromAncestor, rules, srcCtx, destCtx);
                    if (t != null) { method = ResolutionMethod.HumanoidAncestorPath; return t; }
                }
            }

            // 3/4. コンテキストArmature基準の相対パス
            var destMain = MainOfSameSide(destCtx, plan);
            if (destCtx != null && destCtx.Armature != null && !string.IsNullOrEmpty(r.relPath))
            {
                var t = FindByPathWithVariants(destCtx.Armature, r.relPath, rules, srcCtx, destCtx);
                if (t != null)
                {
                    method = srcCtx.kind == BoneContextKind.Costume && destCtx.Kind == BoneContextKind.Costume ? ResolutionMethod.CostumeContextPath : ResolutionMethod.ContextPath;
                    return t;
                }
                // 衣装→本体（マージ後の名前空間）: 衣装Armature基準の相対パスを本体Armature基準で試す
                if (srcCtx.kind == BoneContextKind.Costume && destMain != null && destMain.Armature != null && destCtx != destMain)
                {
                    var t2 = FindByPathWithVariants(destMain.Armature, r.relPath, rules, srcCtx, destMain);
                    if (t2 != null) { method = ResolutionMethod.NormalizedNameInMain; return t2; }
                }
            }
            // ルート基準の相対パス（Genericコンテキスト / コンテキスト無し）
            if (!string.IsNullOrEmpty(r.pathFromRoot))
            {
                var baseT = destCtx != null && destCtx.RootTransform != null ? destCtx.RootTransform : plan.DestinationRoot.transform;
                var t = FindByPathWithVariants(baseT, r.pathFromRoot, rules, srcCtx, destCtx);
                if (t != null) { method = ResolutionMethod.ContextPath; return t; }
            }

            // 5/6. 名前一致（正規化名を含む）。一意のときだけ採用
            var names = NameVariants(r.boneName, rules, srcCtx, destCtx);
            var found = new List<Transform>();
            foreach (var t in BonesOfSide(destCtx, plan))
            {
                if (names.Contains(t.name) || names.Contains(NormalizeDestName(t, plan)))
                    found.Add(t);
            }
            found = found.Distinct().ToList();
            if (found.Count == 1) { method = ResolutionMethod.UniqueName; return found[0]; }
            if (found.Count > 1)
            {
                // 同じコンテキスト内の候補に絞れるなら絞る
                var inCtx = destCtx != null && destCtx.Armature != null ? found.Where(t => t.IsChildOf(destCtx.Armature)).ToList() : found;
                if (inCtx.Count == 1) { method = ResolutionMethod.UniqueName; return inCtx[0]; }
                candidates = found;
            }
            return null;
        }

        /// <summary>
        /// 相対パスの各セグメントにルール（順方向/逆方向）とMA prefix/suffix正規化を適用した候補で Transform.Find を試みる。
        /// </summary>
        private static Transform FindByPathWithVariants(Transform baseTransform, string relPath, List<PathRemapRule> rules, BoneContext srcCtx, ContextInfo destCtx)
        {
            if (baseTransform == null) return null;
            if (string.IsNullOrEmpty(relPath)) return baseTransform;

            var segments = relPath.Split('/');
            Transform current = baseTransform;
            foreach (var seg in segments)
            {
                Transform next = null;
                foreach (var variant in NameVariants(seg, rules, srcCtx, destCtx))
                {
                    next = current.Find(variant);
                    if (next != null) break;
                }
                if (next == null)
                {
                    // 子の正規化名と比較（移植先側にprefix/suffixがある場合）
                    foreach (Transform child in current)
                    {
                        var norm = StripAffix(child.name, destCtx?.MaPrefix, destCtx?.MaSuffix);
                        if (NameVariants(seg, rules, srcCtx, destCtx).Contains(norm)) { next = child; break; }
                    }
                }
                if (next == null) return null;
                current = next;
            }
            return current;
        }

        /// <summary>
        /// 1セグメント名の候補: 原名 / ルール順方向 / ルール逆方向 / MA prefix,suffix を剥いだ名 / 移植先 prefix,suffix を付けた名
        /// </summary>
        private static HashSet<string> NameVariants(string name, List<PathRemapRule> rules, BoneContext srcCtx, ContextInfo destCtx)
        {
            var set = new HashSet<string> { name };
            var stripped = StripAffix(name, srcCtx?.maPrefix, srcCtx?.maSuffix);
            set.Add(stripped);
            if (destCtx != null && (!string.IsNullOrEmpty(destCtx.MaPrefix) || !string.IsNullOrEmpty(destCtx.MaSuffix)))
                set.Add(destCtx.MaPrefix + stripped + destCtx.MaSuffix);
            if (rules != null && rules.Count > 0)
            {
                foreach (var baseName in set.ToList())
                {
                    string f = baseName, b = baseName;
                    foreach (var rule in rules) { f = rule.Apply(f); b = rule.ApplyReverse(b); }
                    set.Add(f); set.Add(b);
                }
            }
            set.RemoveWhere(string.IsNullOrEmpty);
            return set;
        }

        public static string StripAffix(string name, string prefix, string suffix)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string n = name;
            if (!string.IsNullOrEmpty(prefix) && n.StartsWith(prefix, StringComparison.Ordinal)) n = n.Substring(prefix.Length);
            if (!string.IsNullOrEmpty(suffix) && n.EndsWith(suffix, StringComparison.Ordinal)) n = n.Substring(0, n.Length - suffix.Length);
            return n;
        }

        private static string NormalizeDestName(Transform t, ResolutionPlan plan)
        {
            var ctx = PBRemapContextResolver.ClassifyBone(t, plan.DestinationContexts);
            if (ctx != null && ctx.Kind == BoneContextKind.Costume) return StripAffix(t.name, ctx.MaPrefix, ctx.MaSuffix);
            return t.name;
        }

        private static void ResolveComponentReference(ReferenceResolution res, BoneRef r, BoneContext srcCtx, ResolutionPlan plan, List<PathRemapRule> rules, List<ManualMapping> manual)
        {
            // 移植先の同じ相対位置に同型コンポーネントがあればそれを使う（例: 移植先に既にある手のPBC）
            Transform bone = null;
            if (srcCtx != null)
                bone = ResolveBone(r, srcCtx, plan, rules, out _, out _);
            if (bone != null)
            {
                var comp = bone.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == r.targetComponentType);
                if (comp != null)
                {
                    res.Target = bone; res.Status = ResolutionStatus.Resolved; res.Method = ResolutionMethod.ContextPath;
                    res.Message = $"移植先の同位置にある {r.targetComponentType} を参照します";
                    return;
                }
            }
            res.Status = ResolutionStatus.ExternalObject;
            res.Message = $"AvatarDynamics外の {r.targetComponentType} ('{r.boneName}') を参照しています。移植先に対応するコンポーネントがありません（参照は解除されます）";
        }

        #endregion

        #region scale

        /// <summary>
        /// スケール比を移植元コンテキストごとに求める。
        /// 衣装のAvatarDynamicsをアバター間で移す場合、衣装ボーンの参照は衣装同士の寸法比（ボーン間距離）、
        /// アバターボーンへの参照はアバター同士の寸法比（Hips-Head距離）で補正する。
        ///   1. コンテキストが Humanoid 本体で、対応する移植先も Humanoid → Hips-Head 距離比
        ///   2. そのコンテキストの解決済み参照のボーン-親距離比の中央値
        ///   3. マニフェスト全体の Hips-Head 距離比（ホーム → 外側）
        ///   4. 1.0（警告）
        /// </summary>
        private static void ComputeScale(PBRemap definition, ResolutionPlan plan)
        {
            var manifest = plan.Manifest;
            var ratioByCtx = new Dictionary<int, (float ratio, string method)>();
            bool warnedUnavailable = false;

            (float ratio, string method) RatioFor(BoneContext ctx)
            {
                int key = ctx != null ? ctx.id : -1;
                if (ratioByCtx.TryGetValue(key, out var cached)) return cached;
                (float, string) result;
                if (definition.ScaleMode == PBRemapScaleMode.Manual) result = (definition.ManualScaleFactor, "手動");
                else if (definition.ScaleMode == PBRemapScaleMode.None) result = (1f, "なし");
                else
                {
                    var humanoidMain = ctx != null ? PickHumanoidMain(ctx, plan) : (plan.SelfMain != null && plan.SelfMain.IsHumanoid ? plan.SelfMain : plan.OuterMain);
                    float destHH = HipsToHead(humanoidMain);
                    float srcHH = ctx != null ? ctx.hipsToHead : 0f;
                    if (srcHH > 1e-5f && destHH > 1e-5f) result = (destHH / srcHH, "Hips-Head距離比");
                    else if (TryMedianRatio(plan, manifest, key, out var med, out var n)) result = (med, $"ボーン間距離比（{n}組の中央値）");
                    else if (manifest.scaleReference.hipsToHead > 1e-5f && destHH > 1e-5f) result = (destHH / manifest.scaleReference.hipsToHead, "Hips-Head距離比");
                    else if (manifest.scaleReference.outerHipsToHead > 1e-5f && destHH > 1e-5f) result = (destHH / manifest.scaleReference.outerHipsToHead, "外側の Hips-Head距離比");
                    else
                    {
                        result = (1f, "算出不可（1.0）");
                        if (!warnedUnavailable)
                        {
                            plan.Warnings.Add("スケール係数を算出できませんでした（Humanoidでなく、比較できるボーンもありません）。1.0 を使用します。");
                            warnedUnavailable = true;
                        }
                    }
                }
                ratioByCtx[key] = result;
                return result;
            }

            // 参照ごと: VRC SDK は radius 等に参照ボーンの lossyScale を乗算するため、その差を打ち消す
            foreach (var res in plan.Resolutions)
            {
                var ctx = manifest.GetContext(res.Ref.contextId);
                var (ratio, _) = RatioFor(ctx);
                float srcLossy = PBRemapManifestBuilder.MaxComponent(res.Ref.lossyScale);
                Transform dstBone = res.Target ?? res.AutoCreateParent;
                float dstLossy = dstBone != null ? PBRemapManifestBuilder.MaxComponent(dstBone.lossyScale) : srcLossy;
                if (srcLossy < 1e-6f) srcLossy = 1f;
                if (dstLossy < 1e-6f) dstLossy = 1f;
                res.ScaleFactor = ratio * srcLossy / dstLossy;
            }

            // 代表値: 参照数が最も多いホーム側コンテキスト（無ければ全体で最多）
            var primaryCtx = manifest.refs.Where(r => r.contextId >= 0)
                .GroupBy(r => r.contextId).Select(g => (ctx: manifest.GetContext(g.Key), n: g.Count()))
                .Where(x => x.ctx != null)
                .OrderByDescending(x => x.ctx.scope == BoneContextScope.Self ? 1 : 0).ThenByDescending(x => x.n)
                .Select(x => x.ctx).FirstOrDefault();
            var (primary, primaryMethod) = RatioFor(primaryCtx);
            plan.WorldScaleRatio = primary;
            plan.ScaleMethod = primaryMethod;
            var outerCtx = manifest.contexts.FirstOrDefault(c => c.scope == BoneContextScope.Outer && manifest.refs.Any(r => r.contextId == c.id));
            if (outerCtx != null)
            {
                var (outer, outerMethod) = RatioFor(outerCtx);
                plan.OuterScaleRatio = outer;
                if (Mathf.Abs(outer - primary) > 1e-4f || outerMethod != primaryMethod)
                    plan.ScaleMethod = $"{primaryMethod} / 外側: x{outer:F3} ({outerMethod})";
            }
            else plan.OuterScaleRatio = primary;

            var extreme = ratioByCtx.Values.Where(v => v.ratio > 3f || v.ratio < 0.33f).Select(v => v.ratio).ToList();
            if (extreme.Count > 0)
                plan.Warnings.Add($"スケール差異が大きいです (x{extreme[0]:F2})。移植後のパラメータを確認してください。");
        }

        private static float HipsToHead(ContextInfo main) => PBRemapContextResolver.HipsToHead(main);

        /// <summary>解決済み参照のボーン-親距離比の中央値（コンテキストIDで絞る）</summary>
        private static bool TryMedianRatio(ResolutionPlan plan, PBRemapManifest manifest, int contextId, out float ratio, out int count)
        {
            var ratios = new List<float>();
            for (int i = 0; i < plan.Resolutions.Count && i < manifest.scaleReference.boneParentDistances.Count; i++)
            {
                var res = plan.Resolutions[i];
                if (res.Ref.contextId != contextId) continue;
                float src = manifest.scaleReference.boneParentDistances[i];
                if (res.Target == null || res.Target.parent == null || src < 1e-4f) continue;
                float dst = Vector3.Distance(res.Target.position, res.Target.parent.position);
                if (dst < 1e-4f) continue;
                ratios.Add(dst / src);
            }
            count = ratios.Count;
            if (ratios.Count == 0) { ratio = 1f; return false; }
            ratios.Sort();
            ratio = ratios[ratios.Count / 2];
            return true;
        }

        #endregion
    }
}
