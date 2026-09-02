using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// 移植リマップ結果データ（互換API）
    /// </summary>
    public class RemapResult
    {
        public int RemappedComponentCount { get; set; }
        public int RemappedReferenceCount { get; set; }
        public int UnresolvedReferenceCount { get; set; }
        public int AmbiguousReferenceCount { get; set; }
        public int AutoCreatedObjectCount { get; set; }
        public float WorldScaleRatio { get; set; } = 1f;
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>PBRemapの状態</summary>
    public enum PBRemapState
    {
        /// <summary>配下にVRCコンポーネントの外部参照が無い</summary>
        NoReferences,
        /// <summary>参照が自分を含むルート内に解決している（移植元にいる／適用済み）</summary>
        AtHome,
        /// <summary>参照がルート外を指している（別ルートへドロップされた）</summary>
        Displaced,
        /// <summary>参照が失われている（Prefab化/別シーン）。マニフェストがあれば解決可能</summary>
        Broken,
        /// <summary>移植先が特定できない</summary>
        NoDestination,
    }

    /// <summary>PBRemapの現在の状況（UI/NDMF共通）</summary>
    public class PBRemapSituation
    {
        public PBRemapState State;
        public RootInfo Destination;
        public RootInfo Source;             // Live のときのみ
        public PBRemapManifestBuilder.ScanResult Scan;
        public bool HasManifest;
        public bool ManifestMatchesDestination; // マニフェストの移植元 == 移植先（適用済み/ホーム）
        /// <summary>生きている参照はあるが、一部が失われている（衣装Prefabのアバターボーン参照など）</summary>
        public bool PartiallyLost;
        public int LostReferences;
        public List<string> Warnings = new List<string>();

        public GameObject DestinationRoot => Destination?.Root;
        public GameObject SourceRoot => Source?.Root;
        public bool CanResolve => State == PBRemapState.Displaced || (State == PBRemapState.Broken && HasManifest);

        /// <summary>移植先の表示名（外側があれば "Avatar › Costume"）</summary>
        public string DestinationDisplayName => Destination != null ? Destination.DisplayName : "";
        /// <summary>移植元の表示名（Live なら実体、そうでなければマニフェスト）</summary>
        public string SourceDisplayName(PBRemap def) => Source != null && Source.Root != null ? Source.DisplayName : (def != null && def.Manifest != null && !def.Manifest.IsEmpty ? def.Manifest.SourceDisplayName : "");
    }

    /// <summary>
    /// PBRemapのファサード。状況判定 → マニフェスト確保 → 解決計画 → 適用。
    /// </summary>
    public static class PBRemapper
    {
        /// <summary>
        /// 現在の状況を判定する（副作用なし）。
        /// </summary>
        public static PBRemapSituation Inspect(PBRemap definition)
        {
            var s = new PBRemapSituation();
            if (definition == null) { s.State = PBRemapState.NoReferences; return s; }

            // 移植先（PBRemap が属する最も近い単位 = ホーム。外側の単位があれば Outer に入る）
            s.Destination = definition.DestinationRootOverride != null
                ? RootInfo.For(definition.DestinationRootOverride)
                : PBRemapContextResolver.FindRoot(definition.transform, excludeSelf: true);

            // 参照の状態（現在のホーム/外側に整合しているかで分類）
            s.Scan = PBRemapManifestBuilder.Scan(definition, s.Destination);
            s.HasManifest = definition.Manifest != null && !definition.Manifest.IsEmpty;
            s.LostReferences = s.Scan.LostKeys.Count;
            s.Warnings.AddRange(s.Scan.Warnings);

            bool sourceOverridden = definition.SourceRootOverride != null && s.Scan.State == PBRemapManifestBuilder.ReferenceState.Live;
            s.Source = sourceOverridden ? RootInfo.For(definition.SourceRootOverride) : s.Scan.SourceRoot;

            if (s.Destination == null || !s.Destination.IsFound)
            {
                s.State = PBRemapState.NoDestination;
                if (s.Destination != null && s.Destination.Candidates.Count > 0)
                    s.Warnings.Add("移植先の候補: " + string.Join(", ", s.Destination.Candidates.Select(c => c.name)));
                return s;
            }

            switch (s.Scan.State)
            {
                case PBRemapManifestBuilder.ReferenceState.NoExternalReferences:
                    s.State = PBRemapState.NoReferences;
                    break;
                case PBRemapManifestBuilder.ReferenceState.Live:
                    if (sourceOverridden)
                        s.State = s.Source != null && s.Source.Root == s.Destination.Root ? PBRemapState.AtHome : PBRemapState.Displaced;
                    else if (s.Scan.HasForeign)
                        s.State = PBRemapState.Displaced;
                    else if (s.Scan.LostKeys.Count > 0 && s.HasManifest && !AppliedTo(definition, s.Destination))
                    {
                        // 生きている参照はホームに整合しているが、一部が失われている（衣装Prefabを別アバターへ置いた等）
                        s.State = PBRemapState.Displaced;
                        s.PartiallyLost = true;
                        s.Source = null;
                    }
                    else
                    {
                        s.State = PBRemapState.AtHome;
                        // 適用済みで、失われた参照に対応するものが移植先に無かった場合はホーム扱い（未適用と誤認しない）。残りは警告で示す
                        if (s.Scan.LostKeys.Count > 0)
                            s.Warnings.Add($"{s.Scan.LostKeys.Count} 件の参照は移植元で失われており、移植先に対応するものもありませんでした（空のままです）");
                    }
                    break;
                default:
                    s.State = PBRemapState.Broken;
                    break;
            }
            s.ManifestMatchesDestination = s.HasManifest && definition.Manifest.sourceRootInstanceId == s.Destination.Root.GetInstanceID();
            if (s.State == PBRemapState.Broken && !s.HasManifest)
                s.Warnings.Add("参照が失われており、移植元の参照情報（マニフェスト）もありません。移植元のシーンでPBRemapを選択して参照情報を更新してください。");
            return s;
        }

        /// <summary>この移植先へ既に適用済みか（失われたままの参照を「未適用」と誤認しないため）</summary>
        private static bool AppliedTo(PBRemap definition, RootInfo destination)
        {
            var a = definition.Applied;
            if (a == null || !a.isApplied || destination == null || destination.Root == null) return false;
            return a.destinationRootInstanceId == destination.Root.GetInstanceID() || a.destinationRootName == destination.Root.name;
        }

        /// <summary>
        /// 移植元にいる（AtHome/Displaced）ならマニフェストを取り直して保存する。
        /// 移植元にいるときに常に最新の情報を持つための処理（P1）。
        /// </summary>
        /// <returns>更新した場合 true</returns>
        public static bool RefreshManifestIfLive(PBRemap definition, PBRemapSituation situation = null, bool registerUndo = false, bool force = false)
        {
            situation ??= Inspect(definition);
            if (situation.Scan == null || situation.Scan.State != PBRemapManifestBuilder.ReferenceState.Live) return false;

            // 手動指定の移植元がある場合はそれをルートとして扱う
            var scan = situation.Scan;
            if (definition.SourceRootOverride != null)
            {
                scan.SourceRoot = RootInfo.For(definition.SourceRootOverride);
                scan.Contexts = PBRemapContextResolver.BuildContexts(scan.SourceRoot);
            }

            var manifest = PBRemapManifestBuilder.Build(definition, scan);
            if (manifest == null) return false;
            if (!force && ManifestEquivalent(definition.Manifest, manifest)) return false;

            if (registerUndo) Undo.RecordObject(definition, "PBRemap 参照情報更新");
            definition.SetManifest(manifest);
            MarkDirty(definition);
            return true;
        }

        /// <summary>
        /// 変更を保存対象にする。Prefabインスタンス上では明示的なオーバーライドとして記録する
        /// （記録しないと保存されないことがある。Revert All Overrides で消える点は Inspector で案内する）。
        /// </summary>
        public static void MarkDirty(PBRemap definition)
        {
            if (definition == null) return;
            // 再生中（NDMF apply-on-play / 再生時適用）の変更は保存されないので、Prefab オーバーライドの記録は編集時だけ行う
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(definition))
                PrefabUtility.RecordPrefabInstancePropertyModifications(definition);
            EditorUtility.SetDirty(definition);
        }

        private static bool ManifestEquivalent(PBRemapManifest a, PBRemapManifest b)
        {
            if (a == null || b == null) return false;
            if (a.sourceRootName != b.sourceRootName || a.outerRootName != b.outerRootName || a.refs.Count != b.refs.Count || a.contexts.Count != b.contexts.Count || a.originals.Count != b.originals.Count) return false;
            for (int i = 0; i < a.refs.Count; i++)
            {
                var x = a.refs[i]; var y = b.refs[i];
                if (x.Key != y.Key || x.contextId != y.contextId || x.relPath != y.relPath || x.boneName != y.boneName
                    || x.humanBone != y.humanBone || x.nearestHumanoidAncestor != y.nearestHumanoidAncestor || x.pathFromAncestor != y.pathFromAncestor
                    || x.isSkeletonBone != y.isSkeletonBone || x.targetComponentType != y.targetComponentType || x.pathFromRoot != y.pathFromRoot
                    || (x.lossyScale - y.lossyScale).sqrMagnitude > 1e-8f
                    || (x.localPosition - y.localPosition).sqrMagnitude > 1e-10f
                    || Quaternion.Angle(x.localRotation, y.localRotation) > 0.01f
                    || (x.localScale - y.localScale).sqrMagnitude > 1e-10f)
                    return false;
            }
            for (int i = 0; i < a.contexts.Count; i++)
            {
                var x = a.contexts[i]; var y = b.contexts[i];
                if (x.id != y.id || x.kind != y.kind || x.scope != y.scope || x.armaturePathFromRoot != y.armaturePathFromRoot || x.maPrefix != y.maPrefix || x.maSuffix != y.maSuffix || x.costumeName != y.costumeName)
                    return false;
            }
            for (int i = 0; i < a.originals.Count; i++)
            {
                var x = a.originals[i]; var y = b.originals[i];
                if (x.componentPath != y.componentPath || !Mathf.Approximately(x.radius, y.radius) || !Mathf.Approximately(x.height, y.height)
                    || (x.position - y.position).sqrMagnitude > 1e-10f || (x.endpointPosition - y.endpointPosition).sqrMagnitude > 1e-10f)
                    return false;
            }
            if (Mathf.Abs(a.scaleReference.hipsToHead - b.scaleReference.hipsToHead) > 1e-5f) return false;
            return true;
        }

        /// <summary>
        /// 旧形式データがあればマニフェストへ移行する。
        /// </summary>
        public static bool MigrateLegacyIfNeeded(PBRemap definition)
        {
            if (definition == null) return false;
            bool changed = false;
            if ((definition.Manifest == null || definition.Manifest.IsEmpty) && definition.SerializedBoneReferences.Count > 0)
            {
                var m = PBRemapManifestBuilder.MigrateLegacy(definition.SerializedBoneReferences, definition.SourceAvatarScale);
                if (m != null) { definition.SetManifest(m); definition.ClearLegacyData(); changed = true; }
            }
            if (!definition.AutoCalculateScale)
            {
                definition.MigrateLegacyScaleSettings();
                changed = true;
            }
            if (changed) MarkDirty(definition);
            return changed;
        }

        /// <summary>
        /// 解決計画を作る（副作用なし）。マニフェストが無くLiveなら一時的に生成して使う。
        /// </summary>
        public static ResolutionPlan Plan(PBRemap definition, PBRemapSituation situation = null)
        {
            situation ??= Inspect(definition);
            var plan = new ResolutionPlan();
            if (situation.State == PBRemapState.NoDestination || situation.DestinationRoot == null)
            {
                plan.Errors.Add("移植先が特定できません。" + (situation.Destination != null && situation.Destination.Candidates.Count > 0
                    ? "候補: " + string.Join(", ", situation.Destination.Candidates.Select(c => c.name)) + "。PBRemapをその子階層に移動するか、詳細設定で手動指定してください。"
                    : "PBRemapをアバター/衣装/小物の子階層に配置するか、詳細設定で手動指定してください。"));
                return plan;
            }
            if (situation.State == PBRemapState.NoReferences)
            {
                plan.Errors.Add("移植対象の参照がありません。PBRemapの子階層にPhysBone等のコンポーネントを配置してください。");
                return plan;
            }
            if (situation.State == PBRemapState.AtHome)
            {
                plan.Errors.Add($"参照は既に '{situation.DestinationDisplayName}' に接続されています（移植の必要はありません）。");
                return plan;
            }

            // Live なら最新のマニフェストで解決（保存はしない）。失われた参照は既存マニフェストから引き継がれる
            PBRemapManifest manifest = definition.Manifest;
            if (situation.Scan.State == PBRemapManifestBuilder.ReferenceState.Live)
            {
                var fresh = PBRemapManifestBuilder.Build(definition, situation.Scan);
                if (fresh != null) manifest = fresh;
            }
            if (manifest == null || manifest.IsEmpty)
            {
                plan.Errors.Add("移植元の参照情報（マニフェスト）がありません。移植元のシーンでPBRemapを選択して「参照情報を更新」してください。");
                return plan;
            }

            using (new ManifestScope(definition, manifest))
            {
                plan = PBRemapResolver.Resolve(definition, situation.DestinationRoot, situation.Destination);
            }
            plan.Warnings.InsertRange(0, situation.Warnings);
            return plan;
        }

        /// <summary>一時的にマニフェストを差し替えるスコープ（保存しない）</summary>
        private class ManifestScope : IDisposable
        {
            private readonly PBRemap _def;
            private readonly PBRemapManifest _orig;
            public ManifestScope(PBRemap def, PBRemapManifest temp) { _def = def; _orig = def.Manifest; if (!ReferenceEquals(_orig, temp)) def.SetManifest(temp); }
            public void Dispose() { if (!ReferenceEquals(_def.Manifest, _orig)) _def.SetManifest(_orig); }
        }

        /// <summary>
        /// 状況判定 → マニフェスト確保 → 解決 → 適用 を一括で行う（互換API）。
        /// </summary>
        public static Result<RemapResult, string> Remap(PBRemap definition, bool registerUndo = true)
        {
            if (definition == null) return Result<RemapResult, string>.Failure("PBRemapがnullです");
            MigrateLegacyIfNeeded(definition);
            var situation = Inspect(definition);

            // Live なら適用前にマニフェストを保存（持ち出しに備える）
            RefreshManifestIfLive(definition, situation, registerUndo);

            var plan = Plan(definition, situation);
            if (plan.Errors.Count > 0)
                return Result<RemapResult, string>.Failure(string.Join("\n", plan.Errors));

            var apply = PBRemapApplier.Apply(definition, plan, registerUndo);
            if (apply.IsFailure) return Result<RemapResult, string>.Failure(apply.Error);

            var a = apply.Value;
            return Result<RemapResult, string>.Success(new RemapResult
            {
                RemappedComponentCount = a.RemappedComponents,
                RemappedReferenceCount = a.RemappedReferences,
                UnresolvedReferenceCount = a.Unresolved,
                AmbiguousReferenceCount = a.Ambiguous,
                AutoCreatedObjectCount = a.AutoCreated,
                WorldScaleRatio = a.WorldScaleRatio,
                Warnings = a.Warnings,
            });
        }

        /// <summary>
        /// AvatarDataからスケール基準値（Hips→Head距離 or lossyScale.y）を算出する（互換）。
        /// </summary>
        public static float CalculateAvatarScale(AvatarData avatarData)
        {
            var animator = avatarData.AvatarAnimator;
            if (animator != null && animator.isHuman)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (hips != null && head != null)
                {
                    float distance = Vector3.Distance(hips.position, head.position);
                    if (distance > 1e-6f) return distance;
                }
            }
            return avatarData.Armature.transform.lossyScale.y;
        }
    }
}
