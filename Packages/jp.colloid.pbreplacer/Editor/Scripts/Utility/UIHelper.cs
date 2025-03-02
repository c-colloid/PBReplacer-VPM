using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace colloid.PBReplacer
{
    /// <summary>
    /// UI要素作成と操作のユーティリティクラス
    /// </summary>
    public static class UIHelper
    {
        /// <summary>
        /// PBReplacerのスタイルを適用したラベルを作成
        /// </summary>
        public static Label CreateStyledLabel(string text, bool isBold = false)
        {
            var label = new Label(text);
            
            if (isBold)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            }
            
            return label;
        }
        
        /// <summary>
        /// リスト表示用のラベルを作成
        /// </summary>
        public static Label CreateListItemLabel(string text, string className = "listitem")
        {
            var label = new Label(text);
            label.AddToClassList(className);
            label.focusable = true;
            return label;
        }
        
        /// <summary>
        /// PBReplacerのスタイルを適用したボタンを作成
        /// </summary>
        public static Button CreateStyledButton(string text, Action clickAction = null)
        {
            var button = new Button();
            button.text = text;
            
            if (clickAction != null)
            {
                button.clicked += clickAction;
            }
            
            return button;
        }
        
        /// <summary>
        /// セパレーターを作成
        /// </summary>
        public static VisualElement CreateSeparator(bool isHorizontal = true)
        {
            var separator = new VisualElement();
            
            if (isHorizontal)
            {
                separator.style.height = 1;
                separator.style.borderBottomWidth = 1;
                separator.style.borderBottomColor = new Color(0, 0, 0, 0.3f);
                separator.style.marginTop = 5;
                separator.style.marginBottom = 5;
            }
            else
            {
                separator.style.width = 1;
                separator.style.borderRightWidth = 1;
                separator.style.borderRightColor = new Color(0, 0, 0, 0.3f);
                separator.style.marginLeft = 5;
                separator.style.marginRight = 5;
            }
            
            return separator;
        }
        
        /// <summary>
        /// フィールドコンテナを作成（ラベルとフィールドを横に並べる）
        /// </summary>
        public static VisualElement CreateFieldContainer(string labelText, VisualElement field, float labelWidth = 120)
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginBottom = 5;
            
            var label = new Label(labelText);
            label.style.width = labelWidth;
            label.style.marginRight = 5;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            
            container.Add(label);
            container.Add(field);
            
            return container;
        }
        
        /// <summary>
        /// メッセージボックスを作成
        /// </summary>
        public static Box CreateMessageBox(string message, MessageType type = MessageType.Info)
        {
            var box = new Box();
            box.style.marginTop = 5;
            box.style.marginBottom = 5;
            box.style.paddingTop = 5;
            box.style.paddingRight = 10;
            box.style.paddingBottom = 5;
            box.style.paddingLeft = 10;
            box.style.borderLeftWidth = 4;
            
            // タイプに応じた色設定
            Color borderColor;
            Color backgroundColor;
            
            switch (type)
            {
                case MessageType.Info:
                    borderColor = new Color(0.2f, 0.6f, 0.9f);
                    backgroundColor = new Color(0.85f, 0.95f, 1.0f, 0.7f);
                    break;
                case MessageType.Warning:
                    borderColor = new Color(0.9f, 0.7f, 0.1f);
                    backgroundColor = new Color(1.0f, 0.95f, 0.8f, 0.7f);
                    break;
                case MessageType.Error:
                    borderColor = new Color(0.9f, 0.3f, 0.2f);
                    backgroundColor = new Color(1.0f, 0.9f, 0.9f, 0.7f);
                    break;
                case MessageType.Success:
                    borderColor = new Color(0.2f, 0.8f, 0.2f);
                    backgroundColor = new Color(0.9f, 1.0f, 0.9f, 0.7f);
                    break;
                default:
                    borderColor = new Color(0.5f, 0.5f, 0.5f);
                    backgroundColor = new Color(0.95f, 0.95f, 0.95f, 0.7f);
                    break;
            }
            
            box.style.borderLeftColor = borderColor;
            box.style.backgroundColor = backgroundColor;
            
            var label = new Label(message);
            box.Add(label);
            
            return box;
        }
        
        /// <summary>
        /// 設定パネルを作成
        /// </summary>
        public static VisualElement CreateSettingsPanel(PBReplacerSettings settings, Action onSettingsChanged = null)
        {
            var panel = new ScrollView();
            
            // タイトル
            var titleLabel = new Label("PBReplacer 設定");
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.fontSize = 14;
            titleLabel.style.marginBottom = 10;
            panel.Add(titleLabel);
            
            // 自動読み込み設定
            var autoLoadToggle = new Toggle("前回のアバターを自動的に読み込む");
            autoLoadToggle.value = settings.AutoLoadLastAvatar;
            autoLoadToggle.RegisterValueChangedCallback(evt => {
                settings.AutoLoadLastAvatar = evt.newValue;
                settings.Save();
                onSettingsChanged?.Invoke();
            });
            panel.Add(autoLoadToggle);
            
            // 確認ダイアログ設定
            var confirmDialogToggle = new Toggle("処理前に確認ダイアログを表示する");
            confirmDialogToggle.value = settings.ShowConfirmDialog;
            confirmDialogToggle.RegisterValueChangedCallback(evt => {
                settings.ShowConfirmDialog = evt.newValue;
                settings.Save();
                onSettingsChanged?.Invoke();
            });
            panel.Add(confirmDialogToggle);
            
            // 進捗バー設定
            var progressBarToggle = new Toggle("処理中に進捗バーを表示する");
            progressBarToggle.value = settings.ShowProgressBar;
            progressBarToggle.RegisterValueChangedCallback(evt => {
                settings.ShowProgressBar = evt.newValue;
                settings.Save();
                onSettingsChanged?.Invoke();
            });
            panel.Add(progressBarToggle);
            
            // テーマ設定
            var themeToggle = new Toggle("エディターテーマに合わせる");
            themeToggle.value = settings.FollowEditorTheme;
            themeToggle.RegisterValueChangedCallback(evt => {
                settings.FollowEditorTheme = evt.newValue;
                settings.Save();
                onSettingsChanged?.Invoke();
            });
            panel.Add(themeToggle);
            
            // 区切り線
            panel.Add(CreateSeparator());
            
            // バージョン情報
            var versionInfo = new Label("Version: " + GetVersionString());
            versionInfo.style.fontSize = 10;
            versionInfo.style.color = new Color(0.5f, 0.5f, 0.5f);
            panel.Add(versionInfo);
            
            return panel;
        }
        
        /// <summary>
        /// バージョン文字列を取得
        /// </summary>
        private static string GetVersionString()
        {
            try
            {
                var packageJsonTextAsset = Resources.Load<TextAsset>("package");
                if (packageJsonTextAsset != null)
                {
                    var json = JsonUtility.FromJson<PackageInfo>(packageJsonTextAsset.text);
                    return json.version;
                }
            }
            catch (Exception)
            {
                // バージョン取得に失敗しても処理を続行
            }
            
            // パッケージから取得できない場合は埋め込みバージョンを返す
            return "1.6.2";
        }
        
        /// <summary>
        /// メッセージの種類
        /// </summary>
        public enum MessageType
        {
            Info,
            Warning,
            Error,
            Success
        }
        
        /// <summary>
        /// パッケージ情報を扱うためのクラス
        /// </summary>
        [Serializable]
        private class PackageInfo
        {
            public string version;
        }
    }
}
