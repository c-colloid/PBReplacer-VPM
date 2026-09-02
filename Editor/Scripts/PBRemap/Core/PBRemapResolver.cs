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
        public float WorldScaleRatio = 1f;
        public string ScaleMethod = "";
        public List<string> Warnings = new List<string>();
        public List<string> Errors = new List<string>();

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
    }

    /// <summary>
    /// マニフェスト × 移植先ルート → 解決計画。
    /// 解決戦略（優先度順）:
    ///   0. 手動マッピング
    ///   1. Humanoid ID（移植先がHumanoid）
    ///   2. Humanoid祖先 + 相対パス（ルール/MA正規化を各セグメントに適用）
    ///   3. 同一衣装コンテキスト（衣装名一致）内の相対パス
    ///   4. コンテキストArmature基準の相対パス（ルール・双方向）
    ///   5. MA正規化名（prefix/suffixを剥いだ名前）での本体/衣装横断一致（一意なときのみ）
    ///   6. 名前一致（一意なときのみ、複数なら Ambiguous）
    ///   7. 自動作成（非スケルトンボーンかつ親が解決可能）
    /// </summary>
    public static class PBRemapResolver
    {
        public static ResolutionPlan Resolve(PBRemap definition, GameObject destinationRoot, RootInfo destInfo = null)
        {
            var plan = new ResolutionPlan { DestinationRoot = destinationRoot, DestinationInfo = destInfo, Manifest = definition.Manifest };

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

            var manifest = definition.Manifest;
            plan.DestinationContexts = PBRemapContextResolver.BuildContexts(destinationRoot);
            var destMain = plan.DestinationContexts.FirstOrDefault(c => c.Kind == BoneContextKind.Main);
            var destGeneric = plan.DestinationContexts[0];
            var rules = definition.PathRemapRules?.ToList() ?? new List<PathRemapRule>();
            var manual = definition.MappingOverrides ?? new List<ManualMapping>();
            var allDestBones = destinationRoot.GetComponentsInChildren<Transform>(true)
                .Where(t => t != definition.transform && !t.IsChildOf(definition.transform)).ToList();

            // 参照ごとに解決
            foreach (var r in manifest.refs)
            {
                var res = new ReferenceResolution { Ref = r };
                plan.Resolutions.Add(res);
                var srcCtx = manifest.GetContext(r.contextId);

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

                var target = ResolveBone(r, srcCtx, plan, rules, allDestBones, out var method, out var candidates);
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
                    var parent = ResolveBone(parentRef, srcCtx, plan, rules, allDestBones, out _, out var pc);
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
                    if (destCtx != null)
                    {
                        res.Status = ResolutionStatus.AutoCreate; res.Method = ResolutionMethod.AutoCreate; res.AutoCreateParent = destCtx.Armature;
                        res.Message = $"'{destCtx.Armature.name}' 配下に '{r.boneName}' を自動作成します";
                        continue;
                    }
                }

                res.Status = ResolutionStatus.Unresolved;
                res.Message = r.isSkeletonBone
                    ? $"ボーン '{r.boneName}' に対応する移植先ボーンが見つかりません（スケルトンボーンのため自動作成不可）"
                    : $"ボーン '{r.boneName}' に対応する移植先ボーンが見つかりません";
            }

            // 参照解決の後にスケール（解決済みペアを使う）
            ComputeScale(definition, plan, destMain);

            // 検証系の警告
            if (destInfo != null && destInfo.Kind == RootKind.Generic)
                plan.Warnings.Add($"移植先 '{destinationRoot.name}' は汎用オブジェクトとして扱います（Descriptor/Animator/MergeArmature無し）。");
            if (manifest.contexts.Any(c => c.kind == BoneContextKind.Costume) && !plan.DestinationContexts.Any(c => c.Kind == BoneContextKind.Costume))
                plan.Warnings.Add("移植元には衣装（MergeArmature）コンテキストがありますが、移植先に衣装がありません。衣装ボーンは本体Armature上で名前解決されます。");

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

        /// <summary>
        /// 移植元コンテキストに対応する移植先コンテキストを返す。
        /// Main→Main、Costume→同名衣装（無ければ null）、Generic→Generic。
        /// </summary>
        private static ContextInfo FindDestContext(BoneContext srcCtx, ResolutionPlan plan)
        {
            switch (srcCtx.kind)
            {
                case BoneContextKind.Main:
                    return plan.DestinationContexts.FirstOrDefault(c => c.Kind == BoneContextKind.Main)
                        ?? plan.DestinationContexts.FirstOrDefault(c => c.Kind == BoneContextKind.Costume && plan.DestinationInfo?.Kind == RootKind.MACostume)
                        ?? plan.DestinationContexts[0];
                case BoneContextKind.Costume:
                {
                    var costumes = plan.DestinationContexts.Where(c => c.Kind == BoneContextKind.Costume).ToList();
                    var byName = costumes.FirstOrDefault(c => c.CostumeName == srcCtx.costumeName);
                    if (byName != null) return byName;
                    if (costumes.Count == 1) return costumes[0];
                    var byPrefix = costumes.FirstOrDefault(c => c.MaPrefix == srcCtx.maPrefix && c.MaSuffix == srcCtx.maSuffix);
                    return byPrefix;
                }
                default:
                    return plan.DestinationContexts[0];
            }
        }

        private static Transform ResolveBone(BoneRef r, BoneContext srcCtx, ResolutionPlan plan, List<PathRemapRule> rules,
            List<Transform> allDestBones, out ResolutionMethod method, out List<Transform> candidates)
        {
            method = ResolutionMethod.None;
            candidates = new List<Transform>();
            var destMain = plan.DestinationContexts.FirstOrDefault(c => c.Kind == BoneContextKind.Main);
            var destCtx = FindDestContext(srcCtx, plan);

            // 1. Humanoid ID
            if (r.humanBone != HumanBodyBones.LastBone && destMain != null && destMain.IsHumanoid)
            {
                var b = destMain.Animator.GetBoneTransform(r.humanBone);
                if (b != null) { method = ResolutionMethod.Humanoid; return b; }
            }

            // 2. Humanoid祖先 + 相対パス
            if (r.nearestHumanoidAncestor != HumanBodyBones.LastBone && !string.IsNullOrEmpty(r.pathFromAncestor)
                && destMain != null && destMain.IsHumanoid)
            {
                var anc = destMain.Animator.GetBoneTransform(r.nearestHumanoidAncestor);
                if (anc != null)
                {
                    var t = FindByPathWithVariants(anc, r.pathFromAncestor, rules, srcCtx, destCtx);
                    if (t != null) { method = ResolutionMethod.HumanoidAncestorPath; return t; }
                }
            }

            // 3/4. コンテキストArmature基準の相対パス
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
                var t = FindByPathWithVariants(plan.DestinationRoot.transform, r.pathFromRoot, rules, srcCtx, destCtx);
                if (t != null) { method = ResolutionMethod.ContextPath; return t; }
            }

            // 5/6. 名前一致（正規化名を含む）。一意のときだけ採用
            var names = NameVariants(r.boneName, rules, srcCtx, destCtx);
            var found = new List<Transform>();
            foreach (var t in allDestBones)
            {
                if (names.Contains(t.name) || names.Contains(NormalizeDestName(t, plan)))
                    found.Add(t);
            }
            found = found.Distinct().ToList();
            if (found.Count == 1) { method = ResolutionMethod.UniqueName; return found[0]; }
            if (found.Count > 1)
            {
                // 同じコンテキスト内の候補に絞れるなら絞る
                var inCtx = destCtx != null ? found.Where(t => t.IsChildOf(destCtx.Armature)).ToList() : found;
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
            var allDestBones = plan.DestinationRoot.GetComponentsInChildren<Transform>(true).ToList();
            Transform bone = null;
            if (srcCtx != null)
                bone = ResolveBone(r, srcCtx, plan, rules, allDestBones, out _, out _);
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

        private static void ComputeScale(PBRemap definition, ResolutionPlan plan, ContextInfo destMain)
        {
            var manifest = plan.Manifest;
            float worldRatio = 1f;
            string methodName;

            if (definition.ScaleMode == PBRemapScaleMode.Manual)
            {
                worldRatio = definition.ManualScaleFactor;
                methodName = "手動";
            }
            else if (definition.ScaleMode == PBRemapScaleMode.None)
            {
                worldRatio = 1f;
                methodName = "なし";
            }
            else
            {
                float destHipsToHead = 0f;
                if (destMain != null && destMain.IsHumanoid)
                {
                    var hips = destMain.Animator.GetBoneTransform(HumanBodyBones.Hips);
                    var head = destMain.Animator.GetBoneTransform(HumanBodyBones.Head);
                    if (hips != null && head != null) destHipsToHead = Vector3.Distance(hips.position, head.position);
                }
                if (manifest.scaleReference.hipsToHead > 1e-5f && destHipsToHead > 1e-5f)
                {
                    worldRatio = destHipsToHead / manifest.scaleReference.hipsToHead;
                    methodName = "Hips-Head距離比";
                }
                else
                {
                    // 解決済み参照のボーン-親距離比の中央値
                    var ratios = new List<float>();
                    for (int i = 0; i < plan.Resolutions.Count && i < manifest.scaleReference.boneParentDistances.Count; i++)
                    {
                        var res = plan.Resolutions[i];
                        float src = manifest.scaleReference.boneParentDistances[i];
                        if (res.Target == null || res.Target.parent == null || src < 1e-4f) continue;
                        float dst = Vector3.Distance(res.Target.position, res.Target.parent.position);
                        if (dst < 1e-4f) continue;
                        ratios.Add(dst / src);
                    }
                    if (ratios.Count >= 1)
                    {
                        ratios.Sort();
                        worldRatio = ratios[ratios.Count / 2];
                        methodName = $"ボーン間距離比（{ratios.Count}組の中央値）";
                    }
                    else
                    {
                        worldRatio = 1f;
                        methodName = "算出不可（1.0）";
                        plan.Warnings.Add("スケール係数を算出できませんでした（Humanoidでなく、比較できるボーンもありません）。1.0 を使用します。");
                    }
                }
            }

            plan.WorldScaleRatio = worldRatio;
            plan.ScaleMethod = methodName;

            // 参照ごと: VRC SDK は radius 等に参照ボーンの lossyScale を乗算するため、その差を打ち消す
            foreach (var res in plan.Resolutions)
            {
                float srcLossy = PBRemapManifestBuilder.MaxComponent(res.Ref.lossyScale);
                Transform dstBone = res.Target ?? res.AutoCreateParent;
                float dstLossy = dstBone != null ? PBRemapManifestBuilder.MaxComponent(dstBone.lossyScale) : srcLossy;
                if (srcLossy < 1e-6f) srcLossy = 1f;
                if (dstLossy < 1e-6f) dstLossy = 1f;
                res.ScaleFactor = worldRatio * srcLossy / dstLossy;
            }

            if (worldRatio > 3f || worldRatio < 0.33f)
                plan.Warnings.Add($"スケール差異が大きいです (x{worldRatio:F2})。移植後のパラメータを確認してください。");
        }

        #endregion
    }
}
