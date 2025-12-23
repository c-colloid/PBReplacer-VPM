using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(colloid.PBReplacer.NDMF.AvatarDynamicsPlugin))]

namespace colloid.PBReplacer.NDMF
{
    /// <summary>
    /// NDMFプラグイン：ビルド時にAvatarDynamicsConfigの参照を解決する
    /// </summary>
    public class AvatarDynamicsPlugin : Plugin<AvatarDynamicsPlugin>
    {
        public override string DisplayName => "PBReplacer - Avatar Dynamics";
        public override string QualifiedName => "jp.colloid.pbreplacer";

        protected override void Configure()
        {
            // Transformingフェーズで参照を解決
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Resolve AvatarDynamics References", ctx =>
                {
                    var configs = ctx.AvatarRootObject.GetComponentsInChildren<AvatarDynamicsConfig>(true);
                    foreach (var config in configs)
                    {
                        AvatarDynamicsResolver.ResolveReferences(config, ctx.AvatarRootObject);
                    }
                });

            // Optimizingフェーズでメタデータコンポーネントを削除
            InPhase(BuildPhase.Optimizing)
                .Run("Cleanup AvatarDynamics Metadata", ctx =>
                {
                    var configs = ctx.AvatarRootObject.GetComponentsInChildren<AvatarDynamicsConfig>(true);
                    foreach (var config in configs)
                    {
                        Object.DestroyImmediate(config);
                    }
                });
        }
    }
}
