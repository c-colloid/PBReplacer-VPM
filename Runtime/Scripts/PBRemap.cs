using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace colloid.PBReplacer
{
    /// <summary>
    /// AvatarDynamicsコンポーネントの移植設定を定義するMonoBehaviour。
    /// AvatarDynamicsフォルダに配置し、フォルダごとD&amp;Dで別アバターに移植する。
    /// ソース/デスティネーションアバターは階層から自動検出される。
    /// AvatarDescriptorが無い場合はAnimator/MA/Prefab等のフォールバック検出を行い、
    /// それでも検出できない場合は手動指定フィールドを使用する。
    /// </summary>
    [AddComponentMenu("PBReplacer/PB Remap")]
    [DisallowMultipleComponent]
	public class PBRemap : MonoBehaviour, IEditorOnly
    {
        [SerializeField]
        [Tooltip("スケールファクターを自動計算するかどうか")]
        private bool autoCalculateScale = true;

        [SerializeField]
        [Tooltip("スケールファクター（手動設定時に使用）")]
        private float scaleFactor = 1.0f;

        [SerializeField]
        [Tooltip("ボーンパスのリマップルール")]
        private List<PathRemapRule> pathRemapRules = new();

        [SerializeField]
        [Tooltip("Prefab用: シリアライズされたボーン参照データ")]
        private List<SerializedBoneReference> serializedBoneReferences = new();

        [SerializeField]
        [Tooltip("Prefab用: ソースアバターのスケール基準値（Hips→Head距離）")]
        private float sourceAvatarScale;

        [SerializeField]
        [Tooltip("手動指定: 移植元のルートオブジェクト（自動検出できない場合に使用）")]
        private GameObject sourceRootOverride;

        [SerializeField]
        [Tooltip("手動指定: 移植先のルートオブジェクト（自動検出できない場合に使用）")]
        private GameObject destinationRootOverride;

        /// <summary>スケールファクターを自動計算するかどうか</summary>
        public bool AutoCalculateScale => autoCalculateScale;

        /// <summary>スケールファクター</summary>
        public float ScaleFactor => scaleFactor;

        /// <summary>ボーンパスのリマップルール</summary>
        public IReadOnlyList<PathRemapRule> PathRemapRules => pathRemapRules;

        /// <summary>Prefab用: シリアライズされたボーン参照データ</summary>
        public IReadOnlyList<SerializedBoneReference> SerializedBoneReferences => serializedBoneReferences;

        /// <summary>Prefab用: ソースアバターのスケール基準値</summary>
        public float SourceAvatarScale => sourceAvatarScale;

        /// <summary>手動指定: 移植元のルートオブジェクト</summary>
        public GameObject SourceRootOverride => sourceRootOverride;

        /// <summary>手動指定: 移植先のルートオブジェクト</summary>
        public GameObject DestinationRootOverride => destinationRootOverride;
    }
}
