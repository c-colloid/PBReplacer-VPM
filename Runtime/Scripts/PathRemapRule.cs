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
        /// 入力文字列にこのルールを順方向（source→destination）で適用する。
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
                    try
                    {
                        return Regex.Replace(input, sourcePattern, destinationPattern ?? "");
                    }
                    catch (ArgumentException)
                    {
                        return input;
                    }

                default:
                    return input;
            }
        }

        /// <summary>
        /// 入力文字列にこのルールを逆方向（destination→source）で適用する。
        /// 双方向リマップにより、ルール1つで両方向の移植に対応できる。
        /// </summary>
        /// <param name="input">変換対象の文字列</param>
        /// <returns>変換後の文字列。逆適用不可の場合は入力をそのまま返す。</returns>
        public string ApplyReverse(string input)
        {
            if (!enabled || string.IsNullOrEmpty(input))
                return input;

            switch (mode)
            {
                case RemapMode.PrefixReplace:
                    if (string.IsNullOrEmpty(destinationPattern))
                        return sourcePattern + input;
                    if (input.StartsWith(destinationPattern, StringComparison.Ordinal))
                        return sourcePattern + input.Substring(destinationPattern.Length);
                    return input;

                case RemapMode.CharacterSubstitution:
                    if (string.IsNullOrEmpty(destinationPattern))
                        return input;
                    return input.Replace(destinationPattern, sourcePattern);

                case RemapMode.RegexReplace:
                    if (string.IsNullOrEmpty(destinationPattern))
                        return input;
                    try
                    {
                        return Regex.Replace(input, destinationPattern, sourcePattern ?? "");
                    }
                    catch (ArgumentException)
                    {
                        return input;
                    }

                default:
                    return input;
            }
        }
    }
}
