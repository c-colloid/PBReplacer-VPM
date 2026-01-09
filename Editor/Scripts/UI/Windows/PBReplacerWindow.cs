using System;
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
	/// partialクラスとして以下のファイルに分割:
	/// - PBReplacerWindow.cs (メイン/フィールド/Unityメソッド)
	/// - PBReplacerWindow.UI.cs (UI初期化)
	/// - PBReplacerWindow.Events.cs (イベントハンドラ)
	/// - PBReplacerWindow.ListView.cs (ListView関連)
	/// </summary>
	public partial class PBReplacerWindow : EditorWindow
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
		// データマネージャーへの参照（Managersレジストリ経由）
		private PhysBoneDataManager _pbDataManager => Managers.PhysBone;
		private PhysBoneColliderManager _pbcDataManager => Managers.PhysBoneCollider;
		private ConstraintDataManager _constraintDataManager => Managers.Constraint;
		private ContactDataManager _contactDataManager => Managers.Contact;

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
	}
}
