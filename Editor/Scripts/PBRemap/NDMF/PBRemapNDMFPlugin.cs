#if NDMF
using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(colloid.PBReplacer.PBRemapNDMFPlugin))]

namespace colloid.PBReplacer
{
    /// <summary>
    /// NDMF プラグイン定義。
    /// PBRemap コンポーネントをビルド時に処理する。
    /// </summary>
    public class PBRemapNDMFPlugin : Plugin<PBRemapNDMFPlugin>
    {
        public override string DisplayName => "PBReplacer PBRemap";
        public override string QualifiedName => "jp.colloid.pbreplacer.pbremap";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .Run<PBRemapNDMFPass>();
        }
    }
}
#endif
