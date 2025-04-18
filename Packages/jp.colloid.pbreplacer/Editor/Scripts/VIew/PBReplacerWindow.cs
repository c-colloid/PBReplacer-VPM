﻿using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;

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
		
		// ListView群
		private ListView 
		// PB
			_pbListView, _pbcListView,
		// Constraint
			_positionListView, _rotationListView, _sizeListView, _parentListView, _LookAtListView, _AimListView,
		// Contact
			_contactSenderListView, _contactReciverListView;
			
		private List<ListView> _constraintListViewList;
        
		// リストドラッグ処理用
		private ListViewDragHandler
			_pbListDragHandler, _pbcListDragHandler,
			_constraintDragHandler,
			_contactSenderDragHandler, _contactReciverDragHandler;
			
		private List<ListViewDragHandler> _constraintDragHandlerList;
		
		private List<Component> _processed;
        #endregion

        #region Data References
		// データマネージャーへの参照
		private PhysBoneDataManager _pbDataManager => PhysBoneDataManager.Instance;
		private ConstraintDataManager _constraintDataManager => ConstraintDataManager.Instance;
		private ContactDataManager _contactDataManager => ContactDataManager.Instance;
        
		// 設定への参照
		private PBReplacerSettings _settings;
        #endregion

        #region Constants
		private const string AVATAR_FIELD_LABEL_MA = "None (VRC_Avatar Descriptor or MA Merge Armature)";
		private const string AVATAR_FIELD_LABEL_DEFAULT = "None (VRC_Avatar Descriptor)";
		private const string LIST_ITEM_CLASS_NAME = "listitem";
		private const string WINDOW_TITLE = "PBReplacer";
		private const string APPLY_DIALOG_TITLE = "コンポーネントを処理します";
		private const string APPLY_DIALOG_MESSAGE = 
		@"コンポーネントを処理します
この操作はUndo可能です";
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
			UnregisterEvents();
			UnregisterDataManagerEvents();
			
			SaveAvatarData();
            
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
			
			RegisterEvents();
            
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
			
			_constraintListViewList = _constraintBox.Query<ListView>().ToList();
			
			_contactSenderListView = _contactBox.Q<ListView>(nameof(_contactSenderListView).Replace("_contact",""));
			_contactReciverListView = _contactBox.Q<ListView>(nameof(_contactReciverListView).Replace("_contact",""));
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
            
			InitializeAvatarFieldLabel();
			var fieldLabel = _avatarField.Q<Label>("", "unity-object-field-display__label");
			fieldLabel.RegisterValueChangedCallback(OnAvatarFieldLabelChanged);
            
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
			
			// Constraint群リストビューの初期化
			_constraintListViewList.ForEach(listview => InitializeListView(listview,"constraint"));
			
			// Contactリストビューの初期化
			InitializeListView(_contactSenderListView, "ContactSender");
			InitializeListView(_contactReciverListView, "ContactReciver");
            
			// ドラッグ&ドロップハンドラーの作成
			_pbListDragHandler = new ListViewDragHandler(_pbListView, typeof(VRCPhysBone));
			_pbcListDragHandler = new ListViewDragHandler(_pbcListView, typeof(VRCPhysBoneCollider));
			_constraintDragHandlerList = new List<ListViewDragHandler>();
			_constraintListViewList.Select((list,index)=>(list,index)).ToList().ForEach(item => 
			{
				var type = 
					item.index == 0 ? typeof(VRCPositionConstraint) : 
					item.index == 1 ? typeof(VRCRotationConstraint) : 
					item.index == 2 ? typeof(VRCScaleConstraint) :
					item.index == 3 ? typeof(VRCParentConstraint) :
					item.index == 4 ? typeof(VRCLookAtConstraint) :
					typeof(VRCAimConstraint);
				_constraintDragHandlerList.Add(new ListViewDragHandler(item.list, type));
			});
			_contactSenderDragHandler = new ListViewDragHandler(_contactSenderListView, typeof(VRCContactSender));
			_contactReciverDragHandler = new ListViewDragHandler(_contactReciverListView, typeof(VRCContactReceiver));
			
			// ドラッグ&ドロップハンドラーのイベント登録
			//_pbListDragHandler.OnDrop += OnPhysBoneListDrop;
			//_pbcListDragHandler.OnDrop += OnPhysBoneColliderListDrop;
		}
        
		/// <summary>
		/// PhysBoneリストにオブジェクトがドロップされた時の処理
		/// </summary>
		private void OnPhysBoneListDrop(List<GameObject> objects)
		{
			// データが変更されたためリスト更新をリクエスト
			_pbDataManager.ReloadData();
		}
        
		/// <summary>
		/// PhysBoneColliderリストにオブジェクトがドロップされた時の処理
		/// </summary>
		private void OnPhysBoneColliderListDrop(List<GameObject> objects)
		{
			// データが変更されたためリスト更新をリクエスト
			_pbDataManager.ReloadData();
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
				label.AddManipulator(new ContextualMenuManipulator(evt => {
					var target = label.userData as Component;
					evt.menu.AppendAction("Delete", action => {
						Undo.DestroyObjectImmediate(target);
						DataManagerHelper.NotifyComponentsRemoved(target);
					});
				}));
				return label;
			};

			GetProcessedComponents();
			// 要素バインドコールバック
			listView.bindItem = (element, index) => {
				if (listView.itemsSource == null || index >= listView.itemsSource.Count) return;
                
				var component = listView.itemsSource[index] as Component;
				if (component != null)
				{
					(element as Label).text = component.name;
					element.SetEnabled(!_processed.Contains(listView.itemsSource[index]));
				}
				element.userData = component;
			};
            
			// 選択タイプを複数選択に設定
			listView.selectionType = SelectionType.Multiple;
            
			// 選択変更イベントの登録
			listView.onSelectionChange += (selectedItems) => {
				SelectGameObject(selectedItems);
			};
			// 選択変更イベント(ダブルクリック)の登録
			listView.onItemsChosen += (selectedItems) => {
				SelectGameObject(selectedItems);
			};
			
			void SelectGameObject(IEnumerable<object> selectedItems)
			{
				if (selectedItems == null || selectedItems.ToList().Count == 0) return;
                
				// 選択したアイテムのGameObjectをUnityの選択に反映
				Selection.objects = selectedItems
					.OfType<Component>()
					.Select(c => c.gameObject)
					.Cast<UnityEngine.Object>()
					.ToArray();
			}
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
		private void RegisterEvents()
		{
			PBReplacerSettings.OnSettingsChanged += OnSettingsChanged;
		}
		
		private void UnregisterEvents()
		{
			PBReplacerSettings.OnSettingsChanged -= OnSettingsChanged;
		}
        
		private void OnSettingsChanged()
		{
			_settings = PBReplacerSettings.GetLatestSettings();
			Repaint();
		}
        
		/// <summary>
		/// アバターがドロップされた時の処理
		/// </summary>
		private void OnAvatarDrop(Component avatar)
		{
			// 既に_avatarFieldの値変更イベントで処理されるため、
			// ここでは追加処理が必要な場合のみ実装
		}
        
		/// <summary>
		/// アバターフィールドの値変更時の処理
		/// </summary>
		private void OnAvatarFieldValueChanged(ChangeEvent<UnityEngine.Object> evt)
		{
			var avatarObject = evt.newValue as Component;
            
			AvatarFieldHelper.SetAvatar(avatarObject?.gameObject);
                
			// 設定に保存
			_settings.SaveLastAvatarGUID(avatarObject?.gameObject);
			 //アバターの設定を実行
			if (avatarObject != null)
			{
				SetCompoentCountStatus();
			}
			else
			{
				_statusLabel.text = STATUS_SET_AVATAR;
			}
			
			if (avatarObject != null) return;
			InitializeAvatarFieldLabel();
		}
		
		private string ComponentCountStatus()
		{
			bool isValid = true;
			switch (_tabContainer.value)
			{
			case 0: // PhysBone
				isValid = !_pbDataManager.Components.All(_processed.Contains);
				break;
			case 1: // Constraint
				isValid = !_constraintDataManager.Components.All(_processed.Contains);
				break;
			case 2: // Contact
				isValid = !_contactDataManager.Components.All(_processed.Contains);
				break;
			}
			return isValid ? "Applyを押してください" : "Armature内にコンポーネントが見つかりません";
		}
		
		private void OnAvatarFieldLabelChanged(ChangeEvent<string> evt)
		{
			if (_avatarField.value != null) return;
			InitializeAvatarFieldLabel();
		}
		
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
			
			if (_avatarField.value == null) return;
			SetCompoentCountStatus();
		}
        
		/// <summary>
		/// Applyボタンクリック時の処理
		/// </summary>
		private void OnApplyButtonClicked()
		{
			// アバターが設定されているか確認
			if (AvatarFieldHelper.CurrentAvatar == null)
			{
				EditorUtility.DisplayDialog("エラー", "アバターが設定されていません", "OK");
				return;
			}
            
			// 確認ダイアログを表示（設定で無効化可能）
			if (_settings.ShowConfirmDialog)
			{
				string componentname = null;
				switch (_tabContainer.value)
				{
				case 0: // PhysBone
					componentname = "PhysBone";
					break;
				case 1: // Constraint
					componentname = "Constraint";
					break;
				case 2: // Contact
					componentname = "Contact";
					break;
				}
				
				bool proceed = EditorUtility.DisplayDialog(
					componentname + APPLY_DIALOG_TITLE,
					componentname + APPLY_DIALOG_MESSAGE,
					APPLY_DIALOG_OK, 
					APPLY_DIALOG_CANCEL);
                
				if (!proceed) return;
			}
            
			// 処理中のプログレスバーを表示（設定で無効化可能）
			if (_settings.ShowProgressBar)
			{
				EditorUtility.DisplayProgressBar("処理中", "コンポーネントを処理しています...", 0.5f);
			}
            
			try
			{
				// データマネージャーに処理を依頼
				switch (_tabContainer.value)
				{
				case 0: // PhysBone
					_pbDataManager.ProcessReplacement();
					break;
				case 1: // Constraint
					_constraintDataManager.ProcessConstraints();
					break;
				case 2: // Contact
					_contactDataManager.ProcessComponents();
					break;
				}
			}
				catch (Exception ex)
				{
					// エラーハンドリング
					Debug.LogError($"コンポーネント処理中にエラーが発生しました: {ex.Message}");
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
			DataManagerHelper.ReloadData();
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
			_pbDataManager.ReloadData();
		}
        #endregion

        #region Data Event Handlers
		/// <summary>
		/// データマネージャーのイベントを登録
		/// </summary>
		private void RegisterDataManagerEvents()
		{
			if (_pbDataManager == null) return;
            
			UnregisterDataManagerEvents(); // 重複登録を防止
            
			AvatarFieldHelper.OnAvatarChanged += OnAvatarDataChanged;
			_pbDataManager.OnPhysBonesChanged += OnPhysBonesDataChanged;
			_pbDataManager.OnPhysBoneCollidersChanged += OnPhysBoneCollidersDataChanged;
			_pbDataManager.OnComponentsChanged += SetPBTabNotification;
			_pbDataManager.OnComponentsChanged += SetCompoentCountStatus;
			AvatarFieldHelper.OnStatusMessageChanged += OnStatusMessageChanged;
			
			_pbDataManager.OnProcessingComplete += OnProcessingComplete;
			_constraintDataManager.OnProcessingComplete += OnProcessingComplete;
			_contactDataManager.OnProcessingComplete += OnProcessingComplete;
			
			_constraintDataManager.OnConstraintsChanged += OnVRCConstraintsDataChanged;
			_constraintDataManager.OnComponentsChanged += SetConstraintTabNotification;
			_constraintDataManager.OnComponentsChanged += SetCompoentCountStatus;
			
			_contactDataManager.OnContactsChanged += OnVRCContactsDataChanged;
			_contactDataManager.OnContactsChanged += SetContactTabNotification;
			_contactDataManager.OnComponentsChanged += SetCompoentCountStatus;
		}
        
		/// <summary>
		/// データマネージャーのイベント登録を解除
		/// </summary>
		private void UnregisterDataManagerEvents()
		{
			if (_pbDataManager == null) return;
            
			AvatarFieldHelper.OnAvatarChanged -= OnAvatarDataChanged;
			_pbDataManager.OnPhysBonesChanged -= OnPhysBonesDataChanged;
			_pbDataManager.OnPhysBoneCollidersChanged -= OnPhysBoneCollidersDataChanged;
			_pbDataManager.OnComponentsChanged -= SetPBTabNotification;
			_pbDataManager.OnComponentsChanged -= SetCompoentCountStatus;
			AvatarFieldHelper.OnStatusMessageChanged -= OnStatusMessageChanged;
			
			_pbDataManager.OnProcessingComplete -= OnProcessingComplete;
			_constraintDataManager.OnProcessingComplete -= OnProcessingComplete;
			_contactDataManager.OnProcessingComplete -= OnProcessingComplete;
			
			_constraintDataManager.OnConstraintsChanged -= OnVRCConstraintsDataChanged;
			_constraintDataManager.OnComponentsChanged -= SetConstraintTabNotification;
			_constraintDataManager.OnComponentsChanged -= SetCompoentCountStatus;
			
			_contactDataManager.OnContactsChanged -= OnVRCContactsDataChanged;
			_contactDataManager.OnContactsChanged -= SetContactTabNotification;
			_contactDataManager.OnComponentsChanged -= SetCompoentCountStatus;
		}
        
		/// <summary>
		/// アバターデータ変更時の処理
		/// </summary>
		private void OnAvatarDataChanged(AvatarData avatarData)
		{
			if (_avatarField == null) return;
            
			if (avatarData != null)
			{
				SetCompoentCountStatus();
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
		
		private void SetCompoentCountStatus()
		{
			OnStatusMessageChanged(ComponentCountStatus());
		}
		
		private void SetCompoentCountStatus(List<Component> list)
		{
			SetCompoentCountStatus();
		}
		
		private void SetCompoentCountStatus(List<VRCConstraintBase> list)
		{
			SetCompoentCountStatus();
		}
		
		private void GetProcessedComponents()
		{
			_processed = DataManagerHelper.GetAvatarDynamicsComponent<Component>();
		}
		
		private void SetTabNotification(Toggle target, List<Component> list)
		{
			GetProcessedComponents();
			EditorApplication.delayCall += () =>
				target.value = !list.All(_processed.Contains);
		}
		
		private void SetPBTabNotification(List<Component> list)
		{
			var notification = _tabContainer.Query<Toggle>().AtIndex(0);
			var components = list.Select(c => c as Component).ToList();
			SetTabNotification(notification, components);
		}
		
		private void SetConstraintTabNotification(List<VRCConstraintBase> list)
		{
			var notification = _tabContainer.Query<Toggle>().AtIndex(1);
			var components = list.Select(c => c as Component).ToList();
			SetTabNotification(notification, components);
		}
		
		private void SetContactTabNotification(List<Component> list)
		{
			var notification = _tabContainer.Query<Toggle>().AtIndex(2);
			var components = list.Select(c => c as Component).ToList();
			SetTabNotification(notification, components);
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
				//SetComponentListViewBindItem<VRCPhysBone>(_pbListView, _pbDataManager);
                
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
				//SetComponentListViewBindItem<VRCPhysBone, VRCPhysBoneCollider>(_pbcListView, _pbDataManager);
                
				// リストビューを再描画
				RepaintListView(_pbcListView);
			};
		}
		
		/// <summary>
		/// Constraintデータ変更時の処理
		/// </summary>
		private void OnVRCConstraintsDataChanged(List<VRCConstraintBase> constraints)
		{
			if (_constraintListViewList.Any(list => list == null)) return;
            
			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// リストビューのアイテムソースを更新
				_constraintListViewList.ForEach(list =>
				{
					switch (_constraintListViewList.IndexOf(list))
					{
					case 0: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCPositionConstraint));
						break;
					case 1: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCRotationConstraint));
						break;
					case 2: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCScaleConstraint));
						break;
					case 3: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCParentConstraint));
						break;
					case 4: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCLookAtConstraint));
						break;
					case 5: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCAimConstraint));
						break;
					}
					//SetComponentListViewBindItem<VRCConstraintBase>(list, _constraintDataManager);
                
					// リストビューを再描画
					RepaintListView(list);
				});
			};
		}
		
		/// <summary>
		/// Contactデータ変更時の処理
		/// </summary>
		private void OnVRCContactsDataChanged(List<Component> contacts)
		{
			if (_contactSenderListView == null || _contactReciverListView == null) return;
            
			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// リストビューのアイテムソースを更新
				_contactSenderListView.itemsSource = new List<Component>(contacts.Where(component => component is ContactSender));
				_contactReciverListView.itemsSource = new List<Component>(contacts.Where(component => component is ContactReceiver));
				//SetComponentListViewBindItem<Component,ContactSender>(_contactSenderListView, _contactDataManager);
				//SetComponentListViewBindItem<Component,ContactReceiver>(_contactReciverListView, _contactDataManager);
				
				// リストビューを再描画
				RepaintListView(_contactSenderListView);
				RepaintListView(_contactReciverListView);
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
			GetProcessedComponents();
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
			_constraintListViewList.ForEach(list => RepaintListView(list));
			RepaintListView(_contactSenderListView);
			RepaintListView(_contactReciverListView);
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