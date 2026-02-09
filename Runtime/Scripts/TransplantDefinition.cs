using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// AvatarDynamicsコンポーネントの移植設定を定義するMonoBehaviour。
    /// 独立したGameObjectに配置し、Inspector上でソース/デスティネーションアバターを指定する。
    /// </summary>
    [AddComponentMenu("PBReplacer/Transplant Definition")]
    [DisallowMultipleComponent]
    public class TransplantDefinition : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("移植元のアバター")]
        private GameObject sourceAvatar;

        [SerializeField]
        [Tooltip("移植先のアバター")]
        private GameObject destinationAvatar;

        [SerializeField]
        [Tooltip("スケールファクターを自動計算するかどうか")]
        private bool autoCalculateScale = true;

        [SerializeField]
        [Tooltip("スケールファクター（手動設定時に使用）")]
        private float scaleFactor = 1.0f;

        [SerializeField]
        [Tooltip("ボーンパスのリマップルール")]
        private List<PathRemapRule> pathRemapRules = new();

        /// <summary>移植元のアバター</summary>
        public GameObject SourceAvatar => sourceAvatar;

        /// <summary>移植先のアバター</summary>
        public GameObject DestinationAvatar => destinationAvatar;

        /// <summary>スケールファクターを自動計算するかどうか</summary>
        public bool AutoCalculateScale => autoCalculateScale;

        /// <summary>スケールファクター</summary>
        public float ScaleFactor => scaleFactor;

        /// <summary>ボーンパスのリマップルール</summary>
        public IReadOnlyList<PathRemapRule> PathRemapRules => pathRemapRules;
    }
}
