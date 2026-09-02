//#if NDMF
using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(colloid.PBReplacer.PBRemapNDMFPlugin))]

namespace colloid.PBReplacer
{
    /// <summary>
    /// NDMF プラグイン定義。
    /// PBRemap コンポーネントをビルド時に処理する。
    /// Resolving フェーズ（参照解決の段階。MA の MergeArmature(Transforming) より前）で実行し、
    /// ドロップされたままの PBRemap の参照を移植先へ付け替える。
    /// </summary>
    public class PBRemapNDMFPlugin : Plugin<PBRemapNDMFPlugin>
    {
        public override string DisplayName => "PBReplacer PBRemap";
        public override string QualifiedName => "jp.colloid.pbreplacer.pbremap";

        protected override void Configure()
        {
	        InPhase(BuildPhase.Resolving)
		        .BeforePlugin("nadena.dev.modular-avatar")
		        .Run<PBRemapNDMFPass>(new PBRemapNDMFPass());
        }
    }
}
//#endif
