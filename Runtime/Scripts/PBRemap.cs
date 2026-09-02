using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace colloid.PBReplacer
{
    /// <summary>スケール係数の決め方</summary>
    public enum PBRemapScaleMode
    {
        /// <summary>自動（Hips-Head距離比 → ボーン間距離比 → 1.0）</summary>
        Auto = 0,
        /// <summary>手動指定（世界寸法比）</summary>
        Manual = 1,
        /// <summary>補正しない（lossyScale差の打ち消しも行わない）</summary>
        None = 2,
    }

    /// <summary>
    /// AvatarDynamicsコンポーネント群の移植設定。
    /// PBReplacerが作るAvatarDynamics階層（1オブジェクト=1コンポーネント）のルートに付け、
    /// そのGameObjectを別のアバター/衣装/小物へD&amp;Dするだけで、配下コンポーネントのボーン参照を移植先へ付け替える。
    ///
    /// 移植元の参照情報は「移植元にいる間」にマニフェスト（<see cref="PBRemapManifest"/>）として自動保存されるため、
    /// Prefab化・別シーン・別プロジェクトへ持ち出した後でも解決できる。
    /// </summary>
    [AddComponentMenu("PBReplacer/PB Remap")]
    [DisallowMultipleComponent]
    public class PBRemap : MonoBehaviour, IEditorOnly
    {
        [SerializeField]
        [Tooltip("移植元で自動取得された参照情報")]
        private PBRemapManifest manifest = new PBRemapManifest();

        [SerializeField]
        [Tooltip("ユーザーが手動で確定したボーン対応")]
        private List<ManualMapping> mappingOverrides = new List<ManualMapping>();

        [SerializeField]
        [Tooltip("ボーンパスのリマップルール")]
        private List<PathRemapRule> pathRemapRules = new List<PathRemapRule>();

        [SerializeField]
        [Tooltip("スケール係数の決め方")]
        private PBRemapScaleMode scaleMode = PBRemapScaleMode.Auto;

        [SerializeField]
        [Tooltip("手動スケール係数（世界寸法比）")]
        private float manualScaleFactor = 1.0f;

        [SerializeField]
        [Tooltip("ドロップ時の動作")]
        private PBRemapApplyMode applyMode = PBRemapApplyMode.Confirm;

        [SerializeField]
        [Tooltip("適用済み記録（二重適用防止）")]
        private AppliedRecord applied = new AppliedRecord();

        [SerializeField]
        [Tooltip("手動指定: 移植元のルートオブジェクト（自動検出できない場合に使用）")]
        private GameObject sourceRootOverride;

        [SerializeField]
        [Tooltip("手動指定: 移植先のルートオブジェクト（自動検出できない場合に使用）")]
        private GameObject destinationRootOverride;

        // ---- 旧形式（3.0.0-beta.7以前）。読み込み時にマニフェストへ移行する ----
        [SerializeField, HideInInspector] private bool autoCalculateScale = true;
        [SerializeField, HideInInspector] private float scaleFactor = 1.0f;
        [SerializeField, HideInInspector] private List<SerializedBoneReference> serializedBoneReferences = new List<SerializedBoneReference>();
        [SerializeField, HideInInspector] private float sourceAvatarScale;

        /// <summary>移植元で取得された参照情報</summary>
        public PBRemapManifest Manifest => manifest;

        /// <summary>手動マッピング</summary>
        public List<ManualMapping> MappingOverrides => mappingOverrides;

        /// <summary>ボーンパスのリマップルール</summary>
        public IReadOnlyList<PathRemapRule> PathRemapRules => pathRemapRules;

        /// <summary>スケール係数の決め方</summary>
        public PBRemapScaleMode ScaleMode => scaleMode;

        /// <summary>手動スケール係数</summary>
        public float ManualScaleFactor => manualScaleFactor;

        /// <summary>ドロップ時の動作</summary>
        public PBRemapApplyMode ApplyMode => applyMode;

        /// <summary>適用済み記録</summary>
        public AppliedRecord Applied => applied;

        /// <summary>手動指定: 移植元のルートオブジェクト</summary>
        public GameObject SourceRootOverride => sourceRootOverride;

        /// <summary>手動指定: 移植先のルートオブジェクト</summary>
        public GameObject DestinationRootOverride => destinationRootOverride;

        // ---- 旧形式アクセサ（移行用） ----
        /// <summary>旧: スケールファクターを自動計算するかどうか</summary>
        public bool AutoCalculateScale => autoCalculateScale;
        /// <summary>旧: スケールファクター</summary>
        public float ScaleFactor => scaleFactor;
        /// <summary>旧: Prefab用シリアライズ参照</summary>
        public IReadOnlyList<SerializedBoneReference> SerializedBoneReferences => serializedBoneReferences;
        /// <summary>旧: ソースアバターのスケール基準値</summary>
        public float SourceAvatarScale => sourceAvatarScale;

#if UNITY_EDITOR
        /// <summary>マニフェストを差し替える（Editor専用）</summary>
        public void SetManifest(PBRemapManifest value) => manifest = value ?? new PBRemapManifest();

        /// <summary>適用済み記録を更新する（Editor専用）</summary>
        public void SetApplied(AppliedRecord value) => applied = value ?? new AppliedRecord();

        /// <summary>旧形式データを破棄する（移行完了後）</summary>
        public void ClearLegacyData()
        {
            serializedBoneReferences.Clear();
            sourceAvatarScale = 0f;
        }

        /// <summary>旧形式のスケール設定を新形式へ写す</summary>
        public void MigrateLegacyScaleSettings()
        {
            if (!autoCalculateScale)
            {
                scaleMode = PBRemapScaleMode.Manual;
                manualScaleFactor = scaleFactor;
                autoCalculateScale = true;
            }
        }
#endif
    }
}
