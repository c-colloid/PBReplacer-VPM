using System;

namespace colloid.PBReplacer
{
    /// <summary>
    /// ボーンマッピングのプレビュー用データクラス。
    /// ソースボーンとデスティネーションボーンの対応関係を保持する。
    /// </summary>
    [Serializable]
    public class BoneMapping
    {
        /// <summary>ソース側のボーンパス（Armatureからの相対パス）</summary>
        public string sourceBonePath;

        /// <summary>デスティネーション側のボーンパス（Armatureからの相対パス）</summary>
        public string destinationBonePath;

        /// <summary>ボーンが正常に解決されたかどうか</summary>
        public bool resolved;

        /// <summary>解決に失敗した場合のエラーメッセージ</summary>
        public string errorMessage;
    }
}
