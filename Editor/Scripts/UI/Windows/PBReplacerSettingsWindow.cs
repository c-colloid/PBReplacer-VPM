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
        
	    [SerializeField]
	    private VisualTreeAsset _UXML;
	    
	    [SerializeField]
	    private TextAsset _package;
        
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
            
	        _UXML.CloneTree(root);
            
            // スタイルシートを適用
            var styleSheet = Resources.Load<StyleSheet>("PBReplacer");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            
            // タイトル
	        var titleLabel = root.Q<Label>("Title");
	        titleLabel.text = "PBReplacer 設定";
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 16;
            titleLabel.style.marginTop = 10;
            titleLabel.style.marginBottom = 15;
            titleLabel.style.paddingLeft = 10;
            
            // 設定パネル
            var settingsPanel = CreateSettingsPanel();
            
            // 保存ボタン     
	        var saveButton = root.Q<Button>("Save");
	        saveButton.clicked += () => {
                _settings.Save();
                ShowNotification(new GUIContent("設定を保存しました"));
            };
            saveButton.text = "保存";
            saveButton.style.width = 100;
            
	        var resetButton = root.Q<Button>("Reset");
	        resetButton.clicked += () => {
                if (EditorUtility.DisplayDialog("設定のリセット", 
                    "設定を初期状態に戻しますか？", "OK", "キャンセル"))
                {
                    _settings = new PBReplacerSettings();
                    _settings.Save();
                    Repaint();
                    ShowNotification(new GUIContent("設定をリセットしました"));
                }
            };
            resetButton.text = "リセット";
            resetButton.style.width = 100;
            resetButton.style.marginLeft = 10;
            
            // バージョン情報
	        var versionInfo = root.Q<Label>("Version");
	        versionInfo.text = $"PBReplacer Version {GetVersionString()}";
        }
        
        /// <summary>
        /// 設定パネルを作成
        /// </summary>
        private VisualElement CreateSettingsPanel()
        {
	        var panel = rootVisualElement.Q<ScrollView>("MainPanel");
            panel.style.paddingLeft = 15;
            panel.style.paddingRight = 15;
            
            // 基本設定セクション
	        var basicSettingsLabel = panel.Q<Label>("Basic");
	        basicSettingsLabel.text = "基本設定";
            basicSettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            basicSettingsLabel.style.marginBottom = 5;
            
            // 自動読み込み設定
	        var autoLoadToggle = panel.Query<Toggle>().AtIndex(0);
	        autoLoadToggle.label = "前回のアバターを自動的に読み込む";
            autoLoadToggle.value = _settings.AutoLoadLastAvatar;
            autoLoadToggle.RegisterValueChangedCallback(evt => {
                _settings.AutoLoadLastAvatar = evt.newValue;
            });
            
            // 確認ダイアログ設定
	        var confirmDialogToggle = panel.Query<Toggle>().AtIndex(1);
	        confirmDialogToggle.label = "処理前に確認ダイアログを表示する";
            confirmDialogToggle.value = _settings.ShowConfirmDialog;
            confirmDialogToggle.RegisterValueChangedCallback(evt => {
                _settings.ShowConfirmDialog = evt.newValue;
            });
            
            // 進捗バー設定
	        var progressBarToggle = panel.Query<Toggle>().AtIndex(2);
	        progressBarToggle.label = "処理中に進捗バーを表示する";
            progressBarToggle.value = _settings.ShowProgressBar;
            progressBarToggle.RegisterValueChangedCallback(evt => {
                _settings.ShowProgressBar = evt.newValue;
            });
            
            // 区切り線
	        var separator1 = panel.Q<VisualElement>("Separetor1");
            separator1.style.height = 1;
            separator1.style.marginTop = 10;
            separator1.style.marginBottom = 10;
            separator1.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            
            // 表示設定セクション
	        var displaySettingsLabel = panel.Q<Label>("Visibility");
	        displaySettingsLabel.text = "表示設定";
            displaySettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            displaySettingsLabel.style.marginBottom = 5;
            
            // テーマ設定
	        var themeToggle = panel.Query<Toggle>().AtIndex(3);
	        themeToggle.label = "エディターテーマに合わせる";
            themeToggle.value = _settings.FollowEditorTheme;
            themeToggle.RegisterValueChangedCallback(evt => {
                _settings.FollowEditorTheme = evt.newValue;
            });
            
            // PhysBone設定表示
	        var showPBSettingsToggle = panel.Query<Toggle>().AtIndex(4);
	        showPBSettingsToggle.label = "PhysBoneの設定内容を表示";
            showPBSettingsToggle.tooltip = "リスト項目をクリックした時に詳細設定を表示します";
            showPBSettingsToggle.value = _settings.ShowPhysBoneSettings;
            showPBSettingsToggle.RegisterValueChangedCallback(evt => {
                _settings.ShowPhysBoneSettings = evt.newValue;
            });
            
            // 区切り線
	        var separator2 = panel.Q<VisualElement>("Separator2");
            separator2.style.height = 1;
            separator2.style.marginTop = 10;
            separator2.style.marginBottom = 10;
            separator2.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            
            // 高度な設定セクション
	        var advancedSettingsLabel = panel.Q<Label>("Advanced");
	        advancedSettingsLabel.text = "高度な設定";
            advancedSettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            advancedSettingsLabel.style.marginBottom = 5;
            
            // 親階層の維持
	        var preserveHierarchyToggle = panel.Query<Toggle>().AtIndex(5);
	        preserveHierarchyToggle.label = "親子階層構造を維持";
            preserveHierarchyToggle.tooltip = "オリジナルのオブジェクト階層構造を維持します";
            preserveHierarchyToggle.value = _settings.PreserveHierarchy;
            preserveHierarchyToggle.RegisterValueChangedCallback(evt => {
                _settings.PreserveHierarchy = evt.newValue;
            });
            
            // AnimatorがないオブジェクトでもArmatureを検出
	        var detectNonAnimatorArmatureToggle = panel.Query<Toggle>().AtIndex(6);
	        detectNonAnimatorArmatureToggle.label = "Animatorがないオブジェクトでもアーマチュアを検出";
            detectNonAnimatorArmatureToggle.tooltip = "Animator非依存でアーマチュアを検出します";
            detectNonAnimatorArmatureToggle.value = _settings.DetectNonAnimatorArmature;
            detectNonAnimatorArmatureToggle.RegisterValueChangedCallback(evt => {
                _settings.DetectNonAnimatorArmature = evt.newValue;
            });
            
	        var unpackPrefabToggle = panel.Query<Toggle>().AtIndex(7);
	        unpackPrefabToggle.label = "Prefabの継承を破棄";
	        unpackPrefabToggle.tooltip = "AvatarDynamicsのPrefabをUnpackした状態で生成します";
	        unpackPrefabToggle.value = _settings.UnpackPrefab;
	        unpackPrefabToggle.RegisterValueChangedCallback(evt => {
	        	_settings.UnpackPrefab = evt.newValue;
	        });
	        
	        // DestroyUnusedObject設定は削除されました（Prefabのフォルダ構造を保持するため）
	        var destroyUnusedObject = panel.Query<Toggle>().AtIndex(8);
	        if (destroyUnusedObject != null)
	        {
	        	destroyUnusedObject.style.display = DisplayStyle.None;
	        }

	        var findComponent = panel.Query<EnumField>().AtIndex(0);
	        findComponent.value = _settings.FindComponent;
	        findComponent.RegisterValueChangedCallback(evt => {
	        	_settings.FindComponent = (FindComponent)evt.newValue;
	        });
            
            return panel;
        }
        
        /// <summary>
        /// バージョン文字列を取得
        /// </summary>
        private string GetVersionString()
        {
            try
            {
	            var packageJsonTextAsset = _package;
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
