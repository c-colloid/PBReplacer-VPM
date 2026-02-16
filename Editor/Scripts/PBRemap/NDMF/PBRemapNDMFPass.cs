//#if NDMF
using nadena.dev.ndmf;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// NDMF ビルドパス。
    /// アバタークローン上の PBRemap を検出し、
    /// PBRemapper でボーン参照をリマップした後、
    /// PBRemap コンポーネントを除去する。
    /// </summary>
    public class PBRemapNDMFPass : Pass<PBRemapNDMFPass>
    {
        public override string DisplayName => "PBRemap";

        protected override void Execute(BuildContext context)
        {
            var definitions = context.AvatarRootTransform
                .GetComponentsInChildren<PBRemap>(true);

            if (definitions.Length == 0)
                return;

            foreach (var definition in definitions)
            {
                var result = PBRemapper.Remap(definition);

                result.Match(
                    onSuccess: success =>
                    {
                        if (success.Warnings.Count > 0)
                        {
                            foreach (var warning in success.Warnings)
                            {
                                Debug.LogWarning(
                                    $"[PBReplacer PBRemap] {warning}");
                            }
                        }

                        Debug.Log(
                            $"[PBReplacer PBRemap] {definition.gameObject.name}: " +
                            $"{success.RemappedReferenceCount} references remapped" +
                            (success.UnresolvedReferenceCount > 0
                                ? $", {success.UnresolvedReferenceCount} unresolved"
                                : ""));
                    },
                    onFailure: error =>
                    {
                        Debug.LogError(
                            $"[PBReplacer PBRemap] Failed ({definition.gameObject.name}): {error}");
                    }
                );

                // PBRemap はランタイムでは不要なため除去する。
                // NDMF はクローン上で動作するため、元のシーンには影響しない。
                Object.DestroyImmediate(definition);
            }
        }
    }
}
//#endif
