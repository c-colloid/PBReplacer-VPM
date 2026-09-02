using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// NDMF ビルドパス。
    /// アバタークローン上の PBRemap のうち、参照が移植先を指していない（Displaced / Broken）ものだけを解決・適用し、
    /// PBRemap コンポーネントを除去する。既にホーム（AtHome）のものは何もしない（二重適用防止）。
    /// </summary>
    public class PBRemapNDMFPass : Pass<PBRemapNDMFPass>
    {
        public override string DisplayName => "PBRemap";

        protected override void Execute(BuildContext context)
        {
            var definitions = context.AvatarRootTransform.GetComponentsInChildren<PBRemap>(true);
            if (definitions.Length == 0)
                return;

            foreach (var definition in definitions)
            {
                try
                {
                    ProcessOne(context, definition);
                }
                catch (System.Exception ex)
                {
                    ErrorReport.ReportException(ex, $"PBRemap: {definition.gameObject.name}");
                }
                finally
                {
                    // PBRemap はランタイムでは不要なため除去する。NDMF はクローン上で動作するため元のシーンには影響しない。
                    Object.DestroyImmediate(definition);
                }
            }
        }

        private static void ProcessOne(BuildContext context, PBRemap definition)
        {
            var situation = PBRemapper.Inspect(definition);
            string name = definition.gameObject.name;

            switch (situation.State)
            {
                case PBRemapState.AtHome:
                case PBRemapState.NoReferences:
                    // 既に接続済み／対象なし
                    return;
                case PBRemapState.NoDestination:
                    ErrorReport.ReportError(PBRemapErrorLocalizer.Instance, ErrorSeverity.NonFatal, "pbremap.no_destination", name);
                    return;
            }

            var plan = PBRemapper.Plan(definition, situation);
            if (plan.Errors.Count > 0)
            {
                ErrorReport.ReportError(PBRemapErrorLocalizer.Instance, ErrorSeverity.NonFatal, "pbremap.plan_failed", name, string.Join(" / ", plan.Errors));
                return;
            }

            var apply = PBRemapApplier.Apply(definition, plan, registerUndo: false);
            if (apply.IsFailure)
            {
                ErrorReport.ReportError(PBRemapErrorLocalizer.Instance, ErrorSeverity.NonFatal, "pbremap.apply_failed", name, apply.Error);
                return;
            }

            var r = apply.Value;
            if (r.Unresolved > 0 || r.Ambiguous > 0)
            {
                var unresolved = plan.Resolutions
                    .Where(x => x.Status == ResolutionStatus.Unresolved || x.Status == ResolutionStatus.Ambiguous || x.Status == ResolutionStatus.ExternalObject)
                    .Select(x => $"{x.Ref.componentPath}.{x.Ref.propertyPath} → {x.SourceDisplayPath}: {x.Message}");
                ErrorReport.ReportError(PBRemapErrorLocalizer.Instance, ErrorSeverity.NonFatal, "pbremap.unresolved", name,
                    $"{r.Unresolved + r.Ambiguous}", string.Join("\n", unresolved));
            }

            Debug.Log($"[PBReplacer PBRemap] {name}: {r.RemappedReferences} references remapped (scale x{r.WorldScaleRatio:F3}, {r.ScaleMethod})" +
                      (r.AutoCreated > 0 ? $", {r.AutoCreated} objects auto-created" : "") +
                      (r.Unresolved > 0 ? $", {r.Unresolved} unresolved" : "") +
                      (r.Ambiguous > 0 ? $", {r.Ambiguous} ambiguous" : ""));
        }
    }

    /// <summary>NDMF ErrorReport 用の簡易ローカライザ（Localizer は sealed のため保持型）</summary>
    public static class PBRemapErrorLocalizer
    {
        private static nadena.dev.ndmf.localization.Localizer _instance;
        public static nadena.dev.ndmf.localization.Localizer Instance => _instance ??= Create();

        private static readonly System.Collections.Generic.Dictionary<string, string> Ja = new System.Collections.Generic.Dictionary<string, string>
        {
            { "pbremap.no_destination", "PBRemap '{0}': 移植先が特定できません。PBRemapをアバターの子階層に配置してください。" },
            { "pbremap.plan_failed", "PBRemap '{0}': 解決に失敗しました。{1}" },
            { "pbremap.apply_failed", "PBRemap '{0}': 適用に失敗しました。{1}" },
            { "pbremap.unresolved", "PBRemap '{0}': {1} 件の参照が解決できませんでした。該当コンポーネントは移植元を参照したままです。\n{2}" },
            { "pbremap.no_destination:description", "PBRemap のドロップ先が アバター/衣装/小物 として認識できません。" },
            { "pbremap.plan_failed:description", "移植元の参照情報（マニフェスト）が無いか、移植先の構造が想定外です。" },
            { "pbremap.apply_failed:description", "適用処理で例外が発生しました。" },
            { "pbremap.unresolved:description", "パスリマップルールの追加、または Inspector の手動マッピングで解決してください。" },
        };

        private static nadena.dev.ndmf.localization.Localizer Create()
        {
            return new nadena.dev.ndmf.localization.Localizer("ja-jp", () => new System.Collections.Generic.List<(string, System.Func<string, string>)>
            {
                ("ja-jp", key => Ja.TryGetValue(key, out var v) ? v : null),
                ("en-us", key => Ja.TryGetValue(key, out var v) ? v : null),
            });
        }
    }
}
