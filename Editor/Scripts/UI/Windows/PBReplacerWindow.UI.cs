using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerWindow - UI初期化部分
	/// </summary>
	public partial class PBReplacerWindow
	{
		#region UXML/USS Loading
		/// <summary>
		/// UXMLレイアウトを読み込み
		/// </summary>
		private void LoadUXMLLayout()
		{
			if (_windowLayout == null)
			{
				_windowLayout = Resources.Load<VisualTreeAsset>("PBReplacer");
			}

			if (_windowLayout == null)
			{
				Debug.LogError("PBReplacer UIレイアウトが見つかりません。");
				return;
			}

			// ルート要素を作成
			_root = _windowLayout.CloneTree();
			_root.StretchToParentSize();
			rootVisualElement.Add(_root);
		}

		/// <summary>
		/// UI要素への参照を取得
		/// </summary>
		private void GetUIReferences()
		{
			_statusLabel = _root.Query<Label>("ToolBarLabel").First();
			_avatarField = _root.Query<ObjectField>("AvatarFiled").First();
			_applyButton = _root.Query<Button>("ApplyButton").First();
			_reloadButton = _root.Query<Button>("ReloadButton").First();
			_tabContainer = _root.Query<VerticalTabContainer>().First();
			_physBoneBox = _root.Query<Box>("PhysBoneBox").First();
			_constraintBox = _root.Query<Box>("ConstraintBox").First();
			_contactBox = _root.Query<Box>("ContactBox").First();
			_pbListView = _root.Query<ListView>("PBListField").First();
			_pbcListView = _root.Query<ListView>("PBCListField").First();

			_constraintListViewList = _constraintBox.Query<ListView>().ToList();

			_contactSenderListView = _contactBox.Q<ListView>(nameof(_contactSenderListView).Replace("_contact", ""));
			_contactReciverListView = _contactBox.Q<ListView>(nameof(_contactReciverListView).Replace("_contact", ""));
		}
		#endregion

		#region UI Initialization
		/// <summary>
		/// UI全体の初期化
		/// </summary>
		private void InitializeUI()
		{
			// オブジェクトフィールドの初期化
			InitializeAvatarField();

			// リストビューの初期化
			InitializeListViews();

			// タブの初期化
			InitializeTabs();

			// ボタンの初期化
			InitializeButtons();

			// ステータスラベルの初期化
			_statusLabel.text = STATUS_SET_AVATAR;

			// Undoイベントハンドラの登録
			UnityEditor.Undo.undoRedoPerformed += OnUndoRedo;
		}

		/// <summary>
		/// アバターフィールドの初期化
		/// </summary>
		private void InitializeAvatarField()
		{
			// アバターフィールドへのドラッグ&ドロップ処理を追加
			var fieldDisplay = _avatarField.Q<VisualElement>("", "unity-object-field-display");
			fieldDisplay.AddManipulator(new AvatarFieldDropManipulator(OnAvatarDrop));

			InitializeAvatarFieldLabel();
			var fieldLabel = _avatarField.Q<Label>("", "unity-object-field-display__label");
			fieldLabel.RegisterValueChangedCallback(OnAvatarFieldLabelChanged);

			// 値変更イベントの登録
			_avatarField.RegisterValueChangedCallback(OnAvatarFieldValueChanged);
		}

		/// <summary>
		/// アバターフィールドのラベルを初期化
		/// </summary>
		private void InitializeAvatarFieldLabel()
		{
			// デフォルトラベル設定
			var fieldLabel = _avatarField.Q<Label>("", "unity-object-field-display__label");
#if MODULAR_AVATAR
			fieldLabel.text = AVATAR_FIELD_LABEL_MA;
#else
			fieldLabel.text = AVATAR_FIELD_LABEL_DEFAULT;
#endif
		}

		/// <summary>
		/// タブの初期化
		/// </summary>
		private void InitializeTabs()
		{
			// 初期値を設定
			_tabContainer.value = 0;

			// タブ切り替えイベントの登録
			_tabContainer.RegisterValueChangedCallback(OnTabChanged);
		}

		/// <summary>
		/// ボタンの初期化
		/// </summary>
		private void InitializeButtons()
		{
			// Applyボタンのイベント登録
			_applyButton.clicked += OnApplyButtonClicked;

			// Reloadボタンのイベント登録
			_reloadButton.clicked += OnReloadButtonClicked;

			// 設定ボタンの作成と登録（存在する場合）
			var settingsButton = _root.Query<Button>("SettingsButton").First();
			if (settingsButton != null)
			{
				_settingsButton = settingsButton;
				_settingsButton.clicked += OnSettingsButtonClicked;
			}
		}

		/// <summary>
		/// 前回のアバターを読み込み
		/// </summary>
		private void TryLoadLastAvatar()
		{
			GameObject lastAvatar = _settings.LoadLastAvatar();
			if (lastAvatar != null && _avatarField != null)
			{
				_avatarField.value = lastAvatar;
			}
		}

		private void SaveAvatarData()
		{
			// 設定に保存(最新の設定を読み込む必要があるので一旦保留)
			//_settings.SaveLastAvatarGUID((_avatarField?.value as Component)?.gameObject);
		}
		#endregion
	}
}
