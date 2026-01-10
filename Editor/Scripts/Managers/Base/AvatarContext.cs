namespace colloid.PBReplacer
{
    /// <summary>
    /// IAvatarContextのデフォルト実装
    /// AvatarFieldHelperをラップし、静的クラスへの直接アクセスを集約する
    /// </summary>
    public class AvatarContext : IAvatarContext
    {
        private static AvatarContext _instance;
        private PBReplacerSettings _settings;
        private ComponentProcessor _processor;

        /// <summary>
        /// シングルトンインスタンス
        /// </summary>
        public static AvatarContext Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AvatarContext();
                }
                return _instance;
            }
        }

        /// <summary>
        /// 現在のアバターデータ（AvatarFieldHelper経由）
        /// </summary>
        public AvatarData CurrentAvatar => AvatarFieldHelper.CurrentAvatar;

        /// <summary>
        /// 設定（キャッシュ付き）
        /// </summary>
        public PBReplacerSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = PBReplacerSettings.Load();
                    PBReplacerSettings.OnSettingsChanged += OnSettingsChanged;
                }
                return _settings;
            }
        }

        /// <summary>
        /// コンポーネント処理機能（キャッシュ付き）
        /// </summary>
        public ComponentProcessor Processor
        {
            get
            {
                if (_processor == null)
                {
                    _processor = new ComponentProcessor(Settings);
                }
                return _processor;
            }
        }

        private AvatarContext()
        {
            // プライベートコンストラクタ（シングルトン）
        }

        private void OnSettingsChanged()
        {
            _settings = PBReplacerSettings.GetLatestSettings();
            _processor = new ComponentProcessor(_settings);
        }

        /// <summary>
        /// インスタンスをリセット（テスト用）
        /// </summary>
        public static void Reset()
        {
            if (_instance != null)
            {
                PBReplacerSettings.OnSettingsChanged -= _instance.OnSettingsChanged;
                _instance = null;
            }
        }
    }
}
