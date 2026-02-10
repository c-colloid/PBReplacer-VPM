#if NDMF
using nadena.dev.ndmf;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// NDMF ビルドパス。
    /// アバタークローン上の TransplantDefinition を検出し、
    /// TransplantRemapper でボーン参照をリマップした後、
    /// TransplantDefinition コンポーネントを除去する。
    /// </summary>
    public class TransplantNDMFPass : Pass<TransplantNDMFPass>
    {
        public override string DisplayName => "Transplant Remap";

        protected override void Execute(BuildContext context)
        {
            var definitions = context.AvatarRootTransform
                .GetComponentsInChildren<TransplantDefinition>(true);

            if (definitions.Length == 0)
                return;

            foreach (var definition in definitions)
            {
                var result = TransplantRemapper.Remap(definition);

                result.Match(
                    onSuccess: success =>
                    {
                        if (success.Warnings.Count > 0)
                        {
                            foreach (var warning in success.Warnings)
                            {
                                Debug.LogWarning(
                                    $"[PBReplacer Transplant] {warning}");
                            }
                        }

                        Debug.Log(
                            $"[PBReplacer Transplant] {definition.gameObject.name}: " +
                            $"{success.RemappedReferenceCount} references remapped" +
                            (success.UnresolvedReferenceCount > 0
                                ? $", {success.UnresolvedReferenceCount} unresolved"
                                : ""));
                    },
                    onFailure: error =>
                    {
                        Debug.LogError(
                            $"[PBReplacer Transplant] Failed ({definition.gameObject.name}): {error}");
                    }
                );

                // TransplantDefinition はランタイムでは不要なため除去する。
                // NDMF はクローン上で動作するため、元のシーンには影響しない。
                Object.DestroyImmediate(definition);
            }
        }
    }
}
#endif
