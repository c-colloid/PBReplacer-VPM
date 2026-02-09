using System;
using System.Text.RegularExpressions;

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

        /// <summary>
        /// 入力文字列にこのルールを適用する。
        /// </summary>
        /// <param name="input">変換対象の文字列</param>
        /// <returns>変換後の文字列。ルールが無効の場合は入力をそのまま返す。</returns>
        public string Apply(string input)
        {
            if (!enabled || string.IsNullOrEmpty(input))
                return input;

            switch (mode)
            {
                case RemapMode.PrefixReplace:
                    if (input.StartsWith(sourcePattern, StringComparison.Ordinal))
                        return destinationPattern + input.Substring(sourcePattern.Length);
                    return input;

                case RemapMode.CharacterSubstitution:
                    if (string.IsNullOrEmpty(sourcePattern))
                        return input;
                    return input.Replace(sourcePattern, destinationPattern);

                case RemapMode.RegexReplace:
                    if (string.IsNullOrEmpty(sourcePattern))
                        return input;
                    return Regex.Replace(input, sourcePattern, destinationPattern ?? "");

                default:
                    return input;
            }
        }
    }
}
