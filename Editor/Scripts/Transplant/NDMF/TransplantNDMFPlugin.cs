#if NDMF
using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(colloid.PBReplacer.TransplantNDMFPlugin))]

namespace colloid.PBReplacer
{
    /// <summary>
    /// NDMF プラグイン定義。
    /// TransplantDefinition コンポーネントをビルド時に処理する。
    /// </summary>
    public class TransplantNDMFPlugin : Plugin<TransplantNDMFPlugin>
    {
        public override string DisplayName => "PBReplacer Transplant";
        public override string QualifiedName => "jp.colloid.pbreplacer.transplant";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .Run<TransplantNDMFPass>();
        }
    }
}
#endif
