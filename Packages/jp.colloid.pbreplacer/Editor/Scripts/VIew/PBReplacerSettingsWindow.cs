using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace colloid.PBReplacer
{
    /// <summary>
    /// PBReplacerの設定ウィンドウ
    /// </summary>
    public class PBReplacerSettingsWindow : EditorWindow
    {
        private PBReplacerSettings _settings;
        
	    [MenuItem("Tools/PBReplacer/Settings", false, 21)]
        public static void ShowWindow()
        {
            // ウィンドウを表示
            var window = GetWindow<PBReplacerSettingsWindow>();
            window.titleContent = new GUIContent("PBReplacer Settings");
            window.minSize = new Vector2(300, 200);
        }
        
        private void OnEnable()
        {
            // 設定を読み込み
            _settings = PBReplacerSettings.Load();
        }
        
        private void CreateGUI()
        {
            // ルート要素
            var root = rootVisualElement;
            
            // スタイルシートを適用
            var styleSheet = Resources.Load<StyleSheet>("PBReplacer");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            
            // タイトル
            var titleLabel = new Label("PBReplacer 設定");
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 16;
            titleLabel.style.marginTop = 10;
            titleLabel.style.marginBottom = 15;
            titleLabel.style.paddingLeft = 10;
            root.Add(titleLabel);
            
            // 設定パネル
            var settingsPanel = CreateSettingsPanel();
            root.Add(settingsPanel);
            
            // 保存ボタン
            var buttonContainer = new VisualElement();
            buttonContainer.style.flexDirection = FlexDirection.Row;
            buttonContainer.style.justifyContent = Justify.Center;
            buttonContainer.style.marginTop = 20;
            
            var saveButton = new Button(() => {
                _settings.Save();
                ShowNotification(new GUIContent("設定を保存しました"));
            });
            saveButton.text = "保存";
            saveButton.style.width = 100;
            
            var resetButton = new Button(() => {
                if (EditorUtility.DisplayDialog("設定のリセット", 
                    "設定を初期状態に戻しますか？", "OK", "キャンセル"))
                {
                    _settings = new PBReplacerSettings();
                    _settings.Save();
                    Repaint();
                    ShowNotification(new GUIContent("設定をリセットしました"));
                }
            });
            resetButton.text = "リセット";
            resetButton.style.width = 100;
            resetButton.style.marginLeft = 10;
            
            buttonContainer.Add(saveButton);
            buttonContainer.Add(resetButton);
            root.Add(buttonContainer);
            
            // バージョン情報
            var versionInfo = new Label($"PBReplacer Version {GetVersionString()}");
            versionInfo.style.position = Position.Absolute;
            versionInfo.style.bottom = 5;
            versionInfo.style.right = 10;
            versionInfo.style.fontSize = 10;
            versionInfo.style.color = new Color(0.5f, 0.5f, 0.5f);
            root.Add(versionInfo);
        }
        
        /// <summary>
        /// 設定パネルを作成
        /// </summary>
        private VisualElement CreateSettingsPanel()
        {
            var panel = new ScrollView();
            panel.style.paddingLeft = 15;
            panel.style.paddingRight = 15;
            
            // 基本設定セクション
            var basicSettingsLabel = new Label("基本設定");
            basicSettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            basicSettingsLabel.style.marginBottom = 5;
            panel.Add(basicSettingsLabel);
            
            // 自動読み込み設定
            var autoLoadToggle = new Toggle("前回のアバターを自動的に読み込む");
            autoLoadToggle.value = _settings.AutoLoadLastAvatar;
            autoLoadToggle.RegisterValueChangedCallback(evt => {
                _settings.AutoLoadLastAvatar = evt.newValue;
            });
            panel.Add(autoLoadToggle);
            
            // 確認ダイアログ設定
            var confirmDialogToggle = new Toggle("処理前に確認ダイアログを表示する");
            confirmDialogToggle.value = _settings.ShowConfirmDialog;
            confirmDialogToggle.RegisterValueChangedCallback(evt => {
                _settings.ShowConfirmDialog = evt.newValue;
            });
            panel.Add(confirmDialogToggle);
            
            // 進捗バー設定
            var progressBarToggle = new Toggle("処理中に進捗バーを表示する");
            progressBarToggle.value = _settings.ShowProgressBar;
            progressBarToggle.RegisterValueChangedCallback(evt => {
                _settings.ShowProgressBar = evt.newValue;
            });
            panel.Add(progressBarToggle);
            
            // 区切り線
            var separator1 = new VisualElement();
            separator1.style.height = 1;
            separator1.style.marginTop = 10;
            separator1.style.marginBottom = 10;
            separator1.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            panel.Add(separator1);
            
            // 表示設定セクション
            var displaySettingsLabel = new Label("表示設定");
            displaySettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            displaySettingsLabel.style.marginBottom = 5;
            panel.Add(displaySettingsLabel);
            
            // テーマ設定
            var themeToggle = new Toggle("エディターテーマに合わせる");
            themeToggle.value = _settings.FollowEditorTheme;
            themeToggle.RegisterValueChangedCallback(evt => {
                _settings.FollowEditorTheme = evt.newValue;
            });
            panel.Add(themeToggle);
            
            // PhysBone設定表示
            var showPBSettingsToggle = new Toggle("PhysBoneの設定内容を表示");
            showPBSettingsToggle.tooltip = "リスト項目をクリックした時に詳細設定を表示します";
            showPBSettingsToggle.value = _settings.ShowPhysBoneSettings;
            showPBSettingsToggle.RegisterValueChangedCallback(evt => {
                _settings.ShowPhysBoneSettings = evt.newValue;
            });
            panel.Add(showPBSettingsToggle);
            
            // 区切り線
            var separator2 = new VisualElement();
            separator2.style.height = 1;
            separator2.style.marginTop = 10;
            separator2.style.marginBottom = 10;
            separator2.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            panel.Add(separator2);
            
            // 高度な設定セクション
            var advancedSettingsLabel = new Label("高度な設定");
            advancedSettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            advancedSettingsLabel.style.marginBottom = 5;
            panel.Add(advancedSettingsLabel);
            
            // 親階層の維持
            var preserveHierarchyToggle = new Toggle("親子階層構造を維持");
            preserveHierarchyToggle.tooltip = "オリジナルのオブジェクト階層構造を維持します";
            preserveHierarchyToggle.value = _settings.PreserveHierarchy;
            preserveHierarchyToggle.RegisterValueChangedCallback(evt => {
                _settings.PreserveHierarchy = evt.newValue;
            });
            panel.Add(preserveHierarchyToggle);
            
            // AnimatorがないオブジェクトでもArmatureを検出
            var detectNonAnimatorArmatureToggle = new Toggle("Animatorがないオブジェクトでもアーマチュアを検出");
            detectNonAnimatorArmatureToggle.tooltip = "Animator非依存でアーマチュアを検出します";
            detectNonAnimatorArmatureToggle.value = _settings.DetectNonAnimatorArmature;
            detectNonAnimatorArmatureToggle.RegisterValueChangedCallback(evt => {
                _settings.DetectNonAnimatorArmature = evt.newValue;
            });
            panel.Add(detectNonAnimatorArmatureToggle);
            
            return panel;
        }
        
        /// <summary>
        /// バージョン文字列を取得
        /// </summary>
        private string GetVersionString()
        {
            try
            {
                var packageJsonTextAsset = Resources.Load<TextAsset>("package");
                if (packageJsonTextAsset != null)
                {
                    // 簡易的なJSON解析
                    string text = packageJsonTextAsset.text;
                    int versionIndex = text.IndexOf("\"version\":");
                    if (versionIndex >= 0)
                    {
                        int startQuote = text.IndexOf("\"", versionIndex + 10);
                        int endQuote = text.IndexOf("\"", startQuote + 1);
                        if (startQuote >= 0 && endQuote >= 0)
                        {
                            return text.Substring(startQuote + 1, endQuote - startQuote - 1);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // バージョン取得に失敗しても処理を続行
            }
            
            // パッケージから取得できない場合は埋め込みバージョンを返す
            return "1.6.2";
        }
    }
}
