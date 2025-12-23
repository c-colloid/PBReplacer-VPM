using System;
using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
    /// <summary>
    /// AvatarDynamics設定をエクスポート/インポートするためのメタデータコンポーネント
    /// NDMFによってビルド時に処理され、参照が解決される
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PBReplacer/Avatar Dynamics Config")]
    public class AvatarDynamicsConfig : MonoBehaviour
    {
        [Header("エクスポート情報")]
        [Tooltip("この設定の名前")]
        public string configName = "AvatarDynamics";

        [Tooltip("エクスポート元のアバター名")]
        public string sourceAvatarName;

        [Tooltip("エクスポート日時")]
        public string exportDate;

        [Tooltip("エクスポートしたPBReplacerのバージョン")]
        public string exporterVersion;

        [Header("参照マッピング")]
        [Tooltip("全コンポーネントの参照情報")]
        public List<ComponentReferenceData> componentReferences = new List<ComponentReferenceData>();

        [Header("マッピング設定")]
        [Tooltip("Armature名のオーバーライド（空の場合は自動検出）")]
        public string armatureNameOverride;

        [Tooltip("パスマッチングの厳格さ")]
        public PathMatchingMode pathMatchingMode = PathMatchingMode.FlexibleWithWarning;

        [Tooltip("見つからない参照の処理方法")]
        public MissingReferenceHandling missingReferenceHandling = MissingReferenceHandling.LogWarning;

        /// <summary>
        /// この設定が有効かどうか
        /// </summary>
        public bool IsValid => componentReferences != null && componentReferences.Count > 0;

        /// <summary>
        /// コンポーネント数を取得
        /// </summary>
        public int ComponentCount => componentReferences?.Count ?? 0;

#if UNITY_EDITOR
        private void Reset()
        {
            configName = gameObject.name;
            exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(configName))
            {
                configName = gameObject.name;
            }
        }
#endif
    }

    /// <summary>
    /// パスマッチングのモード
    /// </summary>
    public enum PathMatchingMode
    {
        [Tooltip("完全一致のみ")]
        Strict,

        [Tooltip("柔軟なマッチング（名前ベース）で警告を出す")]
        FlexibleWithWarning,

        [Tooltip("柔軟なマッチング（警告なし）")]
        Flexible
    }

    /// <summary>
    /// 見つからない参照の処理方法
    /// </summary>
    public enum MissingReferenceHandling
    {
        [Tooltip("エラーとして処理を中断")]
        Error,

        [Tooltip("警告を出して続行")]
        LogWarning,

        [Tooltip("無視して続行")]
        Ignore
    }
}
