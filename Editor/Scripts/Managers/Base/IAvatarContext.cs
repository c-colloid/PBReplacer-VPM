namespace colloid.PBReplacer
{
    /// <summary>
    /// アバター関連のコンテキスト情報を提供するインターフェース
    /// 静的クラス（AvatarFieldHelper）への依存を削減し、テスタビリティを向上させる
    /// </summary>
    public interface IAvatarContext
    {
        /// <summary>
        /// 現在選択されているアバターデータ
        /// </summary>
        AvatarData CurrentAvatar { get; }

        /// <summary>
        /// PBReplacer設定
        /// </summary>
        PBReplacerSettings Settings { get; }

        /// <summary>
        /// コンポーネント処理機能
        /// </summary>
        ComponentProcessor Processor { get; }
    }
}
