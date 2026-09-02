using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using colloid.PBReplacer.StateMachine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacer のメインウィンドウ。
	/// 語彙は PBRemap Inspector と共通（PBReplacerCommon.uss）:
	///   ツール行  … ↻ 再読み込み / ↶ 元に戻す / ⚙ 詳細設定 / ⋮ その他（アイコンのみ、説明はツールチップ）
	///   流れ      … [アバター] ──(再配置 n →)── [AvatarDynamics]。真ん中のピルがそのまま主操作
	///   レール    … カテゴリのアイコンチップ（枠色=状態、右下に未処理件数）。クリックで列の表示切替、Alt+クリックで単独表示
	///   列        … カテゴリごとの一覧。→ 未処理 / ✔ 配置済み（列末尾に畳む）。列へのドロップで追加
	/// partial クラスとして以下のファイルに分割:
	/// - PBReplacerWindow.cs         (フィールド / メニュー / Unity メソッド)
	/// - PBReplacerWindow.UI.cs      (UI 構築: ツール行・流れ・詳細設定・レール)
	/// - PBReplacerWindow.Columns.cs (列と行: データ反映・選択・ドロップ)
	/// - PBReplacerWindow.Events.cs  (アバター設定・再配置・Undo・状態→流れ)
	/// </summary>
	public partial class PBReplacerWindow : EditorWindow
	{
		#region UI Variables
		[SerializeField] private VisualTreeAsset _windowLayout;
		private TemplateContainer _root;

		// ツール行
		private VisualElement _tools;
		private Button _reloadButton;
		private Button _undoButton;
		private Button _gearButton;
		private Button _menuButton;

		// 流れ
		private VisualElement _strip;
		private VisualElement _nodeAvatar;
		private VisualElement _nodeDynamics;
		private VisualElement _lineLeft;
		private VisualElement _lineRight;
		private Image _nodeAvatarIcon;
		private Image _nodeAvatarBadge;
		private Image _nodeDynamicsIcon;
		private Image _connectorState;
		private Image _applyIcon;
		private Label _nodeAvatarName;
		private Label _nodeAvatarSub;
		private Label _nodeDynamicsName;
		private Label _nodeDynamicsSub;
		private Label _applyLabel;
		private Button _applyButton;

		// 詳細設定
		private VisualElement _advanced;

		// レール + 列
		private VisualElement _rail;
		private VisualElement _columns;
		private readonly Dictionary<ComponentCategory, RailChip> _chips = new Dictionary<ComponentCategory, RailChip>();
		private readonly Dictionary<ComponentCategory, CategoryColumn> _columnViews = new Dictionary<ComponentCategory, CategoryColumn>();
		private readonly List<ColumnDropHandler> _dropHandlers = new List<ColumnDropHandler>();

		// AvatarDynamics 配下に既にあるコンポーネント（= 配置済み）
		private HashSet<Component> _processed = new HashSet<Component>();

		// 表示中のカテゴリ（ビットマスク）
		private int _visibleMask = AllCategoriesMask;

		// 直前の再配置を ↶ で戻せるか
		private bool _undoAvailable;
		private double _ignoreHierarchyChangesUntil;

		// オブジェクトピッカー
		private int _avatarPickerControlId = -1;
		#endregion

		#region Data References
		private PhysBoneDataManager _pbDataManager => Managers.PhysBone;
		private PhysBoneColliderManager _pbcDataManager => Managers.PhysBoneCollider;
		private ConstraintDataManager _constraintDataManager => Managers.Constraint;
		private ContactDataManager _contactDataManager => Managers.Contact;

		private PBReplacerSettings _settings;
		private StatusStateMachine _stateMachine;
		#endregion

		#region Constants
		private const string WINDOW_TITLE = "PBReplacer";
		private const string MENU_ITEM_PBREMAP = "他のアバターへ移植 (PBRemap)...";
		private const string PBREMAP_CONTAINER_NAME = "PBRemap";
		private const string PrefVisibleCategories = "PBReplacer.VisibleCategories";
		private const string PrefAdvanced = "PBReplacer.AdvancedOpen";
		private const int AllCategoriesMask = (1 << 4) - 1;
		private const int AvatarPickerControlId = 0x5042; // ObjectSelectorClosed を自分宛てと判定するための固定 ID
		private const string AVATAR_EMPTY_NAME = "アバターをドロップ";
		private const string AVATAR_EMPTY_SUB = "VRC Avatar Descriptor / MA Merge Armature";
		#endregion

		#region Unity Methods
		[MenuItem("Tools/PBReplacer/MainWindow")]
		[MenuItem("GameObject/PBReplacer", false, 25)]
		public static void ShowWindow()
		{
			PBReplacerWindow window = GetWindow<PBReplacerWindow>();
			window.titleContent = new GUIContent(WINDOW_TITLE);
			window.minSize = new Vector2(600, 400);
		}

		[MenuItem("GameObject/PBReplacer Selected", false, 26)]
		public static void ShowWindowWithSelection()
		{
			ShowWindow();
			PBReplacerWindow window = GetWindow<PBReplacerWindow>();

			if (Selection.activeGameObject != null)
			{
				window.AcceptAvatarObject(Selection.activeGameObject);
			}
		}

		/// <summary>
		/// 設定は別ウィンドウではなくメインウィンドウの ⚙ パネル（旧 Tools/PBReplacer/Settings の導線を維持）
		/// </summary>
		[MenuItem("Tools/PBReplacer/Settings", false, 21)]
		public static void ShowSettings()
		{
			ShowWindow();
			PBReplacerWindow window = GetWindow<PBReplacerWindow>();
			window.SetAdvancedVisible(true);
		}

		/// <summary>
		/// 選択中のGameObjectにPBRemapコンポーネントを追加する
		/// （"GameObject/PBReplacer"は既に単体のコマンド項目のため、
		/// サブメニュー化による衝突を避けてフラットな項目として登録）
		/// </summary>
		[MenuItem("GameObject/PBRemapを追加", false, 27)]
		public static void AddPBRemapToSelection()
		{
			GameObject selected = Selection.activeGameObject;
			if (selected == null) return;

			PBRemap remap = selected.GetComponent<PBRemap>();
			if (remap == null)
			{
				remap = Undo.AddComponent<PBRemap>(selected);
			}

			EditorGUIUtility.PingObject(selected);
			Selection.activeObject = selected;
		}

		[MenuItem("GameObject/PBRemapを追加", true)]
		public static bool ValidateAddPBRemapToSelection()
		{
			return Selection.activeGameObject != null;
		}

		private void OnEnable()
		{
			_settings = PBReplacerSettings.Load();
			_visibleMask = EditorPrefs.GetInt(PrefVisibleCategories, AllCategoriesMask);
			if ((_visibleMask & AllCategoriesMask) == 0) _visibleMask = AllCategoriesMask;
		}

		private void OnDisable()
		{
			UnregisterEvents();
			UnregisterDataManagerEvents();
			CleanupDropHandlers();
		}

		private void OnDestroy()
		{
			CleanupDropHandlers();
		}

		private void CreateGUI()
		{
			LoadUXMLLayout();
			if (_root == null) return;

			GetUIReferences();
			InitializeUI();
			InitializeStateMachine();
			RegisterEvents();
			RegisterDataManagerEvents();

			// 既にアバターが設定済み（ドメインリロード後など）なら反映
			if (AvatarFieldHelper.CurrentAvatar?.AvatarObject != null)
			{
				_stateMachine.SetAvatar(true);
				_stateMachine.OnDataLoaded();
			}
			else if (_settings.AutoLoadLastAvatar)
			{
				EditorApplication.delayCall += TryLoadLastAvatar;
			}

			RefreshAll();
		}

		/// <summary>
		/// オブジェクトピッカー（アバターノードのクリック）の結果を受け取る。
		/// UI Toolkit のウィンドウでも ObjectSelectorClosed は OnGUI に届く。
		/// </summary>
		private void OnGUI()
		{
			var e = Event.current;
			if (e == null || _avatarPickerControlId < 0) return;
			if (e.type != EventType.ExecuteCommand) return;
			if (e.commandName != "ObjectSelectorClosed") return;
			if (EditorGUIUtility.GetObjectPickerControlID() != _avatarPickerControlId) return;

			var picked = EditorGUIUtility.GetObjectPickerObject() as GameObject;
			_avatarPickerControlId = -1;
			e.Use();

			if (picked != null)
			{
				EditorApplication.delayCall += () => AcceptAvatarObject(picked);
			}
		}

		private void InitializeStateMachine()
		{
			_stateMachine = new StatusStateMachine();
			_stateMachine.OnStateChanged += OnStateMachineStateChanged;
		}
		#endregion
	}
}
