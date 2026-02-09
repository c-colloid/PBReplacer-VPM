using System;

namespace colloid.PBReplacer
{
    /// <summary>
    /// ボーンパスのリマップルールを定義するデータクラス。
    /// ソースアバターとデスティネーションアバター間のボーン命名規則の違いを吸収する。
    /// </summary>
    [Serializable]
    public class PathRemapRule
    {
        /// <summary>パスリマップの方式</summary>
        public enum RemapMode
        {
            /// <summary>接頭辞の置換（例: "J_Bip_C_" → ""）</summary>
            PrefixReplace,
            /// <summary>文字列の置換（例: "_L" → ".L"）</summary>
            CharacterSubstitution,
            /// <summary>正規表現による置換</summary>
            RegexReplace
        }

        /// <summary>リマップの方式</summary>
        public RemapMode mode = RemapMode.CharacterSubstitution;

        /// <summary>ソース側のパターン</summary>
        public string sourcePattern = "";

        /// <summary>デスティネーション側のパターン</summary>
        public string destinationPattern = "";

        /// <summary>このルールが有効かどうか</summary>
        public bool enabled = true;
    }
}
