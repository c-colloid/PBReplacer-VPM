using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

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

	        var visualTree = Resources.Load<VisualTreeAsset>("UXML/PBReplacerSettingsWindow");
	        if (visualTree == null)
	        {
	            Debug.LogError("PBReplacerSettingsWindow UIレイアウトが見つかりません。");
	            return;
	        }
	        visualTree.CloneTree(root);

            // スタイルシートを適用
            var styleSheet = Resources.Load<StyleSheet>("PBReplacer");
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // タイトル
	        var titleLabel = root.Q<Label>("Title");
	        if (titleLabel != null)
	        {
	            titleLabel.text = "PBReplacer 設定";
	            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
	            titleLabel.style.fontSize = 16;
	            titleLabel.style.marginTop = 10;
	            titleLabel.style.marginBottom = 15;
	            titleLabel.style.paddingLeft = 10;
	        }

            // 設定パネル
            CreateSettingsPanel(root);

            // 保存ボタン
	        var saveButton = root.Q<Button>("Save");
	        if (saveButton != null)
	        {
	            saveButton.clicked += () => {
	                _settings.Save();
	                ShowNotification(new GUIContent("設定を保存しました"));
	            };
	            saveButton.text = "保存";
	            saveButton.style.width = 100;
	        }

	        var resetButton = root.Q<Button>("Reset");
	        if (resetButton != null)
	        {
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
	        }

            // バージョン情報
	        var versionInfo = root.Q<Label>("Version");
	        if (versionInfo != null)
	        {
	            versionInfo.text = $"PBReplacer Version {GetVersionString()}";
	        }
        }

        /// <summary>
        /// 設定パネルを作成
        /// </summary>
        private void CreateSettingsPanel(VisualElement root)
        {
	        var panel = root.Q<ScrollView>("MainPanel");
	        if (panel == null) return;
            panel.style.paddingLeft = 15;
            panel.style.paddingRight = 15;

            // 基本設定セクション
	        var basicSettingsLabel = panel.Q<Label>("Basic");
	        if (basicSettingsLabel != null)
	        {
	            basicSettingsLabel.text = "基本設定";
	            basicSettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
	            basicSettingsLabel.style.marginBottom = 5;
	        }

            // 自動読み込み設定
	        var autoLoadToggle = panel.Q<Toggle>("AutoLoadLastAvatar");
	        if (autoLoadToggle != null)
	        {
	            autoLoadToggle.label = "前回のアバターを自動的に読み込む";
	            autoLoadToggle.value = _settings.AutoLoadLastAvatar;
	            autoLoadToggle.RegisterValueChangedCallback(evt => {
	                _settings.AutoLoadLastAvatar = evt.newValue;
	            });
	        }

            // 確認ダイアログ設定
	        var confirmDialogToggle = panel.Q<Toggle>("ShowConfirmDialog");
	        if (confirmDialogToggle != null)
	        {
	            confirmDialogToggle.label = "処理前に確認ダイアログを表示する";
	            confirmDialogToggle.value = _settings.ShowConfirmDialog;
	            confirmDialogToggle.RegisterValueChangedCallback(evt => {
	                _settings.ShowConfirmDialog = evt.newValue;
	            });
	        }

            // 進捗バー設定
	        var progressBarToggle = panel.Q<Toggle>("ShowProgressBar");
	        if (progressBarToggle != null)
	        {
	            progressBarToggle.label = "処理中に進捗バーを表示する";
	            progressBarToggle.value = _settings.ShowProgressBar;
	            progressBarToggle.RegisterValueChangedCallback(evt => {
	                _settings.ShowProgressBar = evt.newValue;
	            });
	        }

            // 区切り線
	        var separator2 = panel.Q<VisualElement>("Separator2");
	        if (separator2 != null)
	        {
	            separator2.style.height = 1;
	            separator2.style.marginTop = 10;
	            separator2.style.marginBottom = 10;
	            separator2.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
	        }

            // 高度な設定セクション
	        var advancedSettingsLabel = panel.Q<Label>("Advanced");
	        if (advancedSettingsLabel != null)
	        {
	            advancedSettingsLabel.text = "高度な設定";
	            advancedSettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
	            advancedSettingsLabel.style.marginBottom = 5;
	        }

	        var unpackPrefabToggle = panel.Q<Toggle>("UnpackPrefab");
	        if (unpackPrefabToggle != null)
	        {
	        	unpackPrefabToggle.label = "Prefabの継承を破棄";
	        	unpackPrefabToggle.tooltip = "AvatarDynamicsのPrefabをUnpackした状態で生成します";
	        	unpackPrefabToggle.value = _settings.UnpackPrefab;
	        	unpackPrefabToggle.RegisterValueChangedCallback(evt => {
	        		_settings.UnpackPrefab = evt.newValue;
	        	});
	        }

	        var destroyUnusedObject = panel.Q<Toggle>("DestroyUnusedObject");
	        if (destroyUnusedObject != null)
	        {
	        	destroyUnusedObject.label = "未使用フォルダを削除";
	        	destroyUnusedObject.tooltip = "AvatarDynamics内の使用しないフォルダを削除します（新規作成時のみ）";
	        	destroyUnusedObject.value = _settings.DestroyUnusedObject;
	        	destroyUnusedObject.RegisterValueChangedCallback(evt => {
	        		_settings.DestroyUnusedObject = evt.newValue;
	        	});
	        }

	        var findComponent = panel.Q<EnumField>("FindComponent");
	        if (findComponent != null)
	        {
	        	findComponent.value = _settings.FindComponent;
	        	findComponent.RegisterValueChangedCallback(evt => {
	        		_settings.FindComponent = (FindComponent)evt.newValue;
	        	});
	        }
        }

        /// <summary>
        /// バージョン文字列を取得
        /// </summary>
        private string GetVersionString()
        {
            try
            {
	            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PBReplacerSettingsWindow).Assembly);
	            if (packageInfo != null)
	            {
	                return packageInfo.version;
	            }
            }
            catch (Exception)
            {
                // バージョン取得に失敗しても処理を続行
            }

            return "unknown";
        }
    }
}
