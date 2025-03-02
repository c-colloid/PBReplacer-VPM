using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerのメインUI部分を担当するEditorWindowクラス
	/// </summary>
	public class PBReplacerWindow : EditorWindow
	{
        #region UI Variables
		[SerializeField] private VisualTreeAsset _windowLayout;
		private TemplateContainer _root;
		private Label _statusLabel;
		private ObjectField _avatarField;
		private Button _applyButton;
		private Button _reloadButton;
		private Button _settingsButton;
		private VerticalTabContainer _tabContainer;
		private Box _physBoneBox;
		private Box _constraintBox;
		private Box _contactBox;
		private ListView _pbListView;
		private ListView _pbcListView;
        
		// リストドラッグ処理用
		private ListViewDragHandler _pbListDragHandler;
		private ListViewDragHandler _pbcListDragHandler;
        #endregion

        #region Data References
		// データマネージャーへの参照
		private PhysBoneDataManager _dataManager => PhysBoneDataManager.Instance;
		private ConstraintDataManager _constraintDataManager => ConstraintDataManager.Instance;
        
		// 設定への参照
		private PBReplacerSettings _settings;
        #endregion

        #region Constants
		private const string AVATAR_FIELD_LABEL_MA = "None (VRC_Avatar Descriptor or MA Merge Armature)";
		private const string AVATAR_FIELD_LABEL_DEFAULT = "None (VRC_Avatar Descriptor)";
		private const string LIST_ITEM_CLASS_NAME = "listitem";
		private const string WINDOW_TITLE = "PBReplacer";
		private const string APPLY_DIALOG_TITLE = "PhysBoneを処理します";
		private const string APPLY_DIALOG_MESSAGE = "PhysBoneとPhysBoneColliderを処理します。この操作はUndo可能です。";
		private const string APPLY_DIALOG_OK = "続行";
		private const string APPLY_DIALOG_CANCEL = "キャンセル";
		private const string STATUS_SET_AVATAR = "アバターをセットしてください";
		private const string STATUS_READY = "Applyを押してください";
        #endregion

        #region Unity Methods
		[MenuItem("Tools/PBReplacer/MainWindow")]
		[MenuItem("GameObject/PBReplacer", false, 25)]
		public static void ShowWindow()
		{
			// EditorWindowを表示
			PBReplacerWindow window = GetWindow<PBReplacerWindow>();
			window.titleContent = new GUIContent(WINDOW_TITLE);
			window.minSize = new Vector2(450, 300);
		}

		[MenuItem("GameObject/PBReplacer Selected", false, 26)]
		public static void ShowWindowWithSelection()
		{
			PBReplacerWindow window = GetWindow<PBReplacerWindow>();
			window.titleContent = new GUIContent(WINDOW_TITLE);
            
			if (Selection.activeGameObject != null)
			{
				window._avatarField.value = Selection.activeGameObject;
			}
		}

		private void OnEnable()
		{
			// 設定のロード
			_settings = PBReplacerSettings.Load();
            
			// 前回のアバターを自動読み込み（UIが準備できている場合）
			if (_settings.AutoLoadLastAvatar && _avatarField != null)
			{
				TryLoadLastAvatar();
			}
		}

		private void OnDisable()
		{
			// イベント登録解除
			UnregisterDataManagerEvents();
            
			// ドラッグハンドラーのクリーンアップ
			CleanupDragHandlers();
		}

		private void OnDestroy()
		{
			// リソースのクリーンアップ
			CleanupDragHandlers();
		}

		private void CreateGUI()
		{
			// UXMLからレイアウトを読み込み
			LoadUXMLLayout();

			// UI要素の取得
			GetUIReferences();

			// UI初期化
			InitializeUI();
            
			// データマネージャのイベント登録
			RegisterDataManagerEvents();
            
			// 前回のアバターを自動読み込み
			if (_settings.AutoLoadLastAvatar)
			{
				EditorApplication.delayCall += TryLoadLastAvatar;
			}
		}
        #endregion

        #region Initialization Methods
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
		}

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
			Undo.undoRedoPerformed += OnUndoRedo;
		}
        
		/// <summary>
		/// アバターフィールドの初期化
		/// </summary>
		private void InitializeAvatarField()
		{
			// オブジェクトの型を設定
			//_avatarField.objectType = typeof(GameObject);
            
			// アバターフィールドへのドラッグ&ドロップ処理を追加
			var fieldDisplay = _avatarField.Q<VisualElement>("", "unity-object-field-display");
			fieldDisplay.AddManipulator(new AvatarFieldDropManipulator(OnAvatarDrop));
            
			// デフォルトラベル設定
			var fieldLabel = _avatarField.Q<Label>("", "unity-object-field-display__label");
            #if MODULAR_AVATAR
			fieldLabel.text = AVATAR_FIELD_LABEL_MA;
            #else
			fieldLabel.text = AVATAR_FIELD_LABEL_DEFAULT;
            #endif
            
			Debug.Log(fieldLabel.text);
            
			// 値変更イベントの登録
			_avatarField.RegisterValueChangedCallback(OnAvatarFieldValueChanged);
		}
        
		/// <summary>
		/// リストビューの初期化
		/// </summary>
		private void InitializeListViews()
		{
			// PhysBoneリストビューの初期化
			InitializeListView(_pbListView, "PhysBone");
            
			// PhysBoneColliderリストビューの初期化
			InitializeListView(_pbcListView, "PhysBoneCollider");
            
			// ドラッグ&ドロップハンドラーの作成
			_pbListDragHandler = new ListViewDragHandler(_pbListView, typeof(VRCPhysBone));
			_pbcListDragHandler = new ListViewDragHandler(_pbcListView, typeof(VRCPhysBoneCollider));
            
			// ドラッグ&ドロップハンドラーのイベント登録
			_pbListDragHandler.OnDrop += OnPhysBoneListDrop;
			_pbcListDragHandler.OnDrop += OnPhysBoneColliderListDrop;
		}
        
		/// <summary>
		/// PhysBoneリストにオブジェクトがドロップされた時の処理
		/// </summary>
		private void OnPhysBoneListDrop(List<GameObject> objects)
		{
			// データが変更されたためリスト更新をリクエスト
			_dataManager.ReloadData();
		}
        
		/// <summary>
		/// PhysBoneColliderリストにオブジェクトがドロップされた時の処理
		/// </summary>
		private void OnPhysBoneColliderListDrop(List<GameObject> objects)
		{
			// データが変更されたためリスト更新をリクエスト
			_dataManager.ReloadData();
		}
        
		/// <summary>
		/// 単一のリストビューの初期化
		/// </summary>
		private void InitializeListView(ListView listView, string itemType)
		{
			listView.itemsSource = new List<Component>();
			
			// 要素作成コールバック
			listView.makeItem = () => {
				var label = new Label();
				label.AddToClassList(LIST_ITEM_CLASS_NAME);
				label.focusable = true;
				return label;
			};
            
			// 要素バインドコールバック
			listView.bindItem = (element, index) => {
				if (listView.itemsSource == null || index >= listView.itemsSource.Count) return;
                
				var component = listView.itemsSource[index] as Component;
				if (component != null)
				{
					(element as Label).text = component.name;
				}
			};
            
			// 選択タイプを複数選択に設定
			listView.selectionType = SelectionType.Multiple;
            
			// 選択変更イベントの登録
			listView.onItemsChosen += (selectedItems) => {
				if (selectedItems == null || selectedItems.ToList().Count == 0) return;
                
				// 選択したアイテムのGameObjectをUnityの選択に反映
				Selection.objects = selectedItems
					.OfType<Component>()
					.Select(c => c.gameObject)
					.Cast<UnityEngine.Object>()
					.ToArray();
			};
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
        
		/// <summary>
		/// ドラッグハンドラーのクリーンアップ
		/// </summary>
		private void CleanupDragHandlers()
		{
			if (_pbListDragHandler != null)
			{
				_pbListDragHandler.OnDrop -= OnPhysBoneListDrop;
				_pbListDragHandler.Cleanup();
				_pbListDragHandler = null;
			}
            
			if (_pbcListDragHandler != null)
			{
				_pbcListDragHandler.OnDrop -= OnPhysBoneColliderListDrop;
				_pbcListDragHandler.Cleanup();
				_pbcListDragHandler = null;
			}
		}
        #endregion

        #region Event Handlers
		/// <summary>
		/// アバターがドロップされた時の処理
		/// </summary>
		private void OnAvatarDrop(GameObject avatar)
		{
			// 既に_avatarFieldの値変更イベントで処理されるため、
			// ここでは追加処理が必要な場合のみ実装
			
			//_avatarField.objectType = avatar.GetType();
		}
        
		/// <summary>
		/// アバターフィールドの値変更時の処理
		/// </summary>
		private void OnAvatarFieldValueChanged(ChangeEvent<UnityEngine.Object> evt)
		{
			var avatarObject = evt.newValue as Component;
            
			// アバターの設定を実行
			if (avatarObject != null)
			{
				AvatarFieldHelper.SetAvatar(avatarObject.gameObject);
                
				// 設定に保存
				_settings.SaveLastAvatarGUID(avatarObject.gameObject);
			}
			else
			{
				_dataManager.ClearData();
				_statusLabel.text = STATUS_SET_AVATAR;
			}
		}
        
		/// <summary>
		/// タブ変更時の処理
		/// </summary>
		private void OnTabChanged(ChangeEvent<int> evt)
		{
			// すべてのBoxを非表示に
			_physBoneBox.style.display = DisplayStyle.None;
			_constraintBox.style.display = DisplayStyle.None;
			_contactBox.style.display = DisplayStyle.None;
            
			// 選択されたタブに応じてBoxを表示
			switch (evt.newValue)
			{
			case 0: // PhysBone
				_physBoneBox.style.display = DisplayStyle.Flex;
				break;
			case 1: // Constraint
				_constraintBox.style.display = DisplayStyle.Flex;
				break;
			case 2: // Contact
				_contactBox.style.display = DisplayStyle.Flex;
				break;
			}
		}
        
		/// <summary>
		/// Applyボタンクリック時の処理
		/// </summary>
		private void OnApplyButtonClicked()
		{
			// アバターが設定されているか確認
			if (_dataManager.CurrentAvatar == null)
			{
				EditorUtility.DisplayDialog("エラー", "アバターが設定されていません", "OK");
				return;
			}
            
			// 確認ダイアログを表示（設定で無効化可能）
			if (_settings.ShowConfirmDialog)
			{
				bool proceed = EditorUtility.DisplayDialog(
					APPLY_DIALOG_TITLE,
					APPLY_DIALOG_MESSAGE,
					APPLY_DIALOG_OK, 
					APPLY_DIALOG_CANCEL);
                
				if (!proceed) return;
			}
            
			// 処理中のプログレスバーを表示（設定で無効化可能）
			if (_settings.ShowProgressBar)
			{
				EditorUtility.DisplayProgressBar("処理中", "PhysBoneを処理しています...", 0.5f);
			}
            
			try
			{
				// データマネージャーに処理を依頼
				_dataManager.ProcessReplacement();
			}
				catch (Exception ex)
				{
					// エラーハンドリング
					Debug.LogError($"PhysBone処理中にエラーが発生しました: {ex.Message}");
					EditorUtility.DisplayDialog("エラー", $"処理中にエラーが発生しました: {ex.Message}", "OK");
				}
				finally
			{
				// プログレスバーをクリア
				if (_settings.ShowProgressBar)
				{
					EditorUtility.ClearProgressBar();
				}
			}
		}
        
		/// <summary>
		/// Reloadボタンクリック時の処理
		/// </summary>
		private void OnReloadButtonClicked()
		{
			_dataManager.ReloadData();
		}
        
		/// <summary>
		/// 設定ボタンクリック時の処理
		/// </summary>
		private void OnSettingsButtonClicked()
		{
			// 設定ウィンドウを表示
			PBReplacerSettingsWindow.ShowWindow();
		}
        
		/// <summary>
		/// Undo/Redo時の処理
		/// </summary>
		private void OnUndoRedo()
		{
			// データの再読み込み
			_dataManager.ReloadData();
		}
        #endregion

        #region Data Event Handlers
		/// <summary>
		/// データマネージャーのイベントを登録
		/// </summary>
		private void RegisterDataManagerEvents()
		{
			if (_dataManager == null) return;
            
			UnregisterDataManagerEvents(); // 重複登録を防止
            
			AvatarFieldHelper.OnAvatarChanged += OnAvatarDataChanged;
			_dataManager.OnPhysBonesChanged += OnPhysBonesDataChanged;
			_dataManager.OnPhysBoneCollidersChanged += OnPhysBoneCollidersDataChanged;
			AvatarFieldHelper.OnStatusMessageChanged += OnStatusMessageChanged;
			_dataManager.OnProcessingComplete += OnProcessingComplete;
		}
        
		/// <summary>
		/// データマネージャーのイベント登録を解除
		/// </summary>
		private void UnregisterDataManagerEvents()
		{
			if (_dataManager == null) return;
            
			AvatarFieldHelper.OnAvatarChanged -= OnAvatarDataChanged;
			_dataManager.OnPhysBonesChanged -= OnPhysBonesDataChanged;
			_dataManager.OnPhysBoneCollidersChanged -= OnPhysBoneCollidersDataChanged;
			AvatarFieldHelper.OnStatusMessageChanged -= OnStatusMessageChanged;
			_dataManager.OnProcessingComplete -= OnProcessingComplete;
		}
        
		/// <summary>
		/// アバターデータ変更時の処理
		/// </summary>
		private void OnAvatarDataChanged(AvatarData avatarData)
		{
			if (_avatarField == null) return;
            
			if (avatarData != null)
			{
				// アバターフィールドの値を更新（UIイベント発火なし）
				if (_avatarField.value != avatarData.AvatarObject)
				{
					_avatarField.SetValueWithoutNotify(avatarData.AvatarObject);
				}
			}
			else
			{
				_avatarField.SetValueWithoutNotify(null);
			}
		}
        
		/// <summary>
		/// PhysBoneデータ変更時の処理
		/// </summary>
		private void OnPhysBonesDataChanged(List<VRCPhysBone> physBones)
		{
			if (_pbListView == null) return;
            
			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// リストビューのアイテムソースを更新
				_pbListView.itemsSource = new List<Component>(physBones.Cast<Component>());
                
				// リストビューを再描画
				RepaintListView(_pbListView);
			};
		}
        
		/// <summary>
		/// PhysBoneColliderデータ変更時の処理
		/// </summary>
		private void OnPhysBoneCollidersDataChanged(List<VRCPhysBoneCollider> colliders)
		{
			if (_pbcListView == null) return;
            
			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// リストビューのアイテムソースを更新
				_pbcListView.itemsSource = new List<Component>(colliders.Cast<Component>());
                
				// リストビューを再描画
				RepaintListView(_pbcListView);
			};
		}
        
		/// <summary>
		/// ステータスメッセージ変更時の処理
		/// </summary>
		private void OnStatusMessageChanged(string message)
		{
			if (_statusLabel == null) return;
            
			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				_statusLabel.text = message;
			};
		}
        
		/// <summary>
		/// 処理完了時の処理
		/// </summary>
		private void OnProcessingComplete()
		{
			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// 処理完了後のUIの更新
				RepaintAllListViews();
			};
		}
        #endregion

        #region UI Helper Methods
		/// <summary>
		/// すべてのリストビューを再描画
		/// </summary>
		private void RepaintAllListViews()
		{
			RepaintListView(_pbListView);
			RepaintListView(_pbcListView);
		}
        
		/// <summary>
		/// 単一のリストビューを再描画
		/// </summary>
		private void RepaintListView(ListView listView)
		{
			if (listView == null) return;
            
            #if UNITY_2019
			listView.Refresh();
            #elif UNITY_2021_3_OR_NEWER
			listView.Rebuild();
            #else
			listView.Refresh();
            #endif
		}
        #endregion
	}
}