using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerWindow - UI 構築（ツール行 / 流れ / 詳細設定 / レール）
	/// </summary>
	public partial class PBReplacerWindow
	{
		#region UXML Loading
		private void LoadUXMLLayout()
		{
			if (_windowLayout == null)
			{
				_windowLayout = Resources.Load<VisualTreeAsset>("UXML/PBReplacer")
					?? Resources.Load<VisualTreeAsset>("PBReplacer");
			}

			if (_windowLayout == null)
			{
				Debug.LogError("PBReplacer UIレイアウトが見つかりません。");
				return;
			}

			_root = _windowLayout.CloneTree();
			_root.style.flexGrow = 1;
			rootVisualElement.Add(_root);
			PBReplacerFonts.Apply(rootVisualElement);
			PBReplacerTheme.Apply(rootVisualElement);
		}

		private void GetUIReferences()
		{
			_tools = _root.Q<VisualElement>("tools");

			_strip = _root.Q<VisualElement>("strip");
			_nodeAvatar = _root.Q<VisualElement>("node-avatar");
			_nodeDynamics = _root.Q<VisualElement>("node-dynamics");
			_lineLeft = _root.Q<VisualElement>("line-left");
			_lineRight = _root.Q<VisualElement>("line-right");
			_nodeAvatarIcon = _root.Q<Image>("node-avatar-icon");
			_nodeAvatarBadge = _root.Q<Image>("node-avatar-badge");
			_nodeDynamicsIcon = _root.Q<Image>("node-dynamics-icon");
			_connectorState = _root.Q<Image>("connector-state");
			_applyIcon = _root.Q<Image>("apply-icon");
			_nodeAvatarName = _root.Q<Label>("node-avatar-name");
			_nodeAvatarSub = _root.Q<Label>("node-avatar-sub");
			_nodeDynamicsName = _root.Q<Label>("node-dynamics-name");
			_nodeDynamicsSub = _root.Q<Label>("node-dynamics-sub");
			_applyLabel = _root.Q<Label>("apply-label");
			_applyButton = _root.Q<Button>("apply-button");

			_advanced = _root.Q<VisualElement>("advanced");

			_rail = _root.Q<VisualElement>("rail");
			_columns = _root.Q<VisualElement>("columns");
		}
		#endregion

		#region UI Initialization
		private void InitializeUI()
		{
			InitializeTools();
			InitializeStrip();
			InitializeAdvanced();
			InitializeRail();
			InitializeColumns();

			bool adv = EditorPrefs.GetBool(PrefAdvanced, false);
			SetAdvancedVisible(adv);
		}

		/// <summary>
		/// ツール行: 要素は UXML（tools 配下）。ここでは内蔵アイコンの画像とクリックだけを結び付ける
		/// </summary>
		private void InitializeTools()
		{
			_reloadButton = BindIconButton("tool-reload", PBRemapIcons.Refresh, OnReloadButtonClicked);
			_undoButton = BindIconButton("tool-undo", PBRemapIcons.Undo, OnUndoButtonClicked);
			_gearButton = BindIconButton("tool-gear", PBRemapIcons.Settings, OnGearButtonClicked);
			_menuButton = BindIconButton("tool-menu", "_Menu", OnOverflowMenuButtonClicked);

			UpdateTools();
		}

		/// <summary>UXML 上のアイコンボタンに内蔵アイコンとクリック処理を結び付ける</summary>
		private Button BindIconButton(string name, string iconName, Action onClick)
		{
			var button = _root.Q<Button>(name);
			if (button == null) return null;
			PBRemapIcons.Set(button.Q<Image>(), iconName);
			button.clicked += onClick;
			return button;
		}

		private void UpdateTools()
		{
			bool hasAvatar = AvatarFieldHelper.CurrentAvatar?.AvatarObject != null;
			_reloadButton?.SetEnabled(hasAvatar);
			_undoButton?.SetEnabled(_undoAvailable);
		}

		/// <summary>
		/// 流れ: 左ノード = アバター（ドロップ先 / クリックでピッカー）、中央 = 再配置ピル、右ノード = AvatarDynamics
		/// </summary>
		private void InitializeStrip()
		{
			// 左ノードへのドロップ = アバターの選択（ObjectField は使わず、ノードそのものが受け皿）
			_nodeAvatar.AddManipulator(new AvatarFieldDropManipulator(OnAvatarDrop));
			_nodeAvatar.RegisterCallback<ClickEvent>(OnAvatarNodeClicked);
			_nodeAvatar.AddManipulator(new ContextualMenuManipulator(evt =>
			{
				var avatar = AvatarFieldHelper.CurrentAvatar?.AvatarObject;
				evt.menu.AppendAction("Hierarchy で表示", _ => { if (avatar != null) EditorGUIUtility.PingObject(avatar); },
					avatar != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
				evt.menu.AppendAction("アバターを解除", _ => SetAvatar(null),
					avatar != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
			}));

			// 右ノードのクリック = AvatarDynamics を Hierarchy で Ping
			_nodeDynamics.RegisterCallback<ClickEvent>(_ =>
			{
				var dynamics = FindAvatarDynamics();
				if (dynamics != null) EditorGUIUtility.PingObject(dynamics);
			});

			PBRemapIcons.Set(_applyIcon, PBRemapIcons.Apply);
			_applyButton.clicked += OnApplyButtonClicked;

			UpdateStrip();
		}

		private void OnAvatarNodeClicked(ClickEvent evt)
		{
			if (evt.button != 0) return;
			var current = AvatarFieldHelper.CurrentAvatar?.AvatarObject;
			_avatarPickerControlId = AvatarPickerControlId;
			EditorGUIUtility.ShowObjectPicker<GameObject>(current, true, "", _avatarPickerControlId);
		}

		/// <summary>
		/// 詳細設定（⚙ で開閉）。値は変更と同時に保存する（Unity の設定と同じ挙動）。
		/// </summary>
		private void InitializeAdvanced()
		{
			var find = _root.Q<EnumField>("setting-find-component");
			var destroyUnused = _root.Q<Toggle>("setting-destroy-unused");
			var unpack = _root.Q<Toggle>("setting-unpack-prefab");
			var autoLoad = _root.Q<Toggle>("setting-auto-load");
			var confirm = _root.Q<Toggle>("setting-confirm");
			var skipSmall = _root.Q<Toggle>("setting-skip-small");
			var threshold = _root.Q<IntegerField>("setting-skip-threshold");
			var reset = _root.Q<Button>("setting-reset");
			var version = _root.Q<Label>("version-label");

			find?.Init(_settings.FindComponent);
			find?.RegisterValueChangedCallback(evt =>
			{
				if (evt.newValue is FindComponent fc) { _settings.FindComponent = fc; SaveSettings(); DataManagerHelper.ReloadData(); }
			});
			destroyUnused?.RegisterValueChangedCallback(evt => { _settings.DestroyUnusedObject = evt.newValue; SaveSettings(); });
			unpack?.RegisterValueChangedCallback(evt => { _settings.UnpackPrefab = evt.newValue; SaveSettings(); });
			autoLoad?.RegisterValueChangedCallback(evt => { _settings.AutoLoadLastAvatar = evt.newValue; SaveSettings(); });
			confirm?.RegisterValueChangedCallback(evt => { _settings.ShowConfirmDialog = evt.newValue; SaveSettings(); });
			skipSmall?.RegisterValueChangedCallback(evt => { _settings.SkipConfirmForSmallBatches = evt.newValue; SaveSettings(); });
			threshold?.RegisterValueChangedCallback(evt => { _settings.SkipConfirmThreshold = Mathf.Max(0, evt.newValue); SaveSettings(); });

			if (reset != null)
			{
				reset.clicked += () =>
				{
					if (!EditorUtility.DisplayDialog("設定を初期値に戻す", "PBReplacer の設定をすべて初期値に戻します。", "初期値に戻す", "やめる")) return;
					var fresh = new PBReplacerSettings { LastAvatarGUID = _settings.LastAvatarGUID };
					_settings = fresh;
					SaveSettings();
					// 検索範囲が変わっている可能性があるので一覧を取り直す
					DataManagerHelper.ReloadData();
				};
			}

			if (version != null)
			{
				version.text = $"PBReplacer {GetVersionString()}";
			}

			RefreshAdvancedValues();
		}

		/// <summary>設定値をパネルに反映（通知なし）</summary>
		private void RefreshAdvancedValues()
		{
			if (_root == null || _settings == null) return;

			_root.Q<EnumField>("setting-find-component")?.SetValueWithoutNotify(_settings.FindComponent);
			_root.Q<Toggle>("setting-destroy-unused")?.SetValueWithoutNotify(_settings.DestroyUnusedObject);
			_root.Q<Toggle>("setting-unpack-prefab")?.SetValueWithoutNotify(_settings.UnpackPrefab);
			_root.Q<Toggle>("setting-auto-load")?.SetValueWithoutNotify(_settings.AutoLoadLastAvatar);
			_root.Q<Toggle>("setting-confirm")?.SetValueWithoutNotify(_settings.ShowConfirmDialog);
			_root.Q<Toggle>("setting-skip-small")?.SetValueWithoutNotify(_settings.SkipConfirmForSmallBatches);
			_root.Q<IntegerField>("setting-skip-threshold")?.SetValueWithoutNotify(_settings.SkipConfirmThreshold);

			// 確認しない設定のときは「n件以下なら省略」は意味を持たないので薄くする
			_root.Q<VisualElement>("setting-skip-row")?.SetEnabled(_settings.ShowConfirmDialog);
		}

		private void SaveSettings()
		{
			_settings.Save();
		}

		private bool _advancedVisible;

		private void SetAdvancedVisible(bool visible)
		{
			if (_advanced == null) return;
			_advancedVisible = visible;
			_advanced.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			_gearButton?.EnableInClassList("pbremap-icon-button--on", visible);
			EditorPrefs.SetBool(PrefAdvanced, visible);
		}

		private bool IsAdvancedVisible => _advancedVisible;

		/// <summary>
		/// レール: チップの要素は UXML（rail 配下の chip-{Category}）。ここではアイコン画像とクリックだけを結び付ける
		/// </summary>
		private void InitializeRail()
		{
			_chips.Clear();

			foreach (var category in ComponentCategoryInfo.All)
			{
				var root = _rail.Q<VisualElement>($"chip-{category}");
				if (root == null) continue;

				// SDK に固有アイコンが無ければ内蔵型で代替（レイアウトは UXML/USS のまま）
				var chip = new RailChip(category, root, ComponentIconUtility.GetCategoryIcon(category, out _));
				chip.Root.RegisterCallback<ClickEvent>(evt => OnRailChipClicked(category, evt.altKey));
				_chips[category] = chip;
			}
		}

		private void OnRailChipClicked(ComponentCategory category, bool solo)
		{
			int bit = 1 << (int)category;
			if (solo)
			{
				_visibleMask = bit;
			}
			else
			{
				// 最後の 1 つは消せない（列が 0 になると何も見えなくなる）
				int next = _visibleMask ^ bit;
				if ((next & AllCategoriesMask) == 0) return;
				_visibleMask = next;
			}

			EditorPrefs.SetInt(PrefVisibleCategories, _visibleMask);
			ApplyVisibility();
			UpdateStrip();
		}

		private bool IsCategoryVisible(ComponentCategory category) => (_visibleMask & (1 << (int)category)) != 0;

		private void TryLoadLastAvatar()
		{
			if (_settings == null) return;
			GameObject lastAvatar = _settings.LoadLastAvatar();
			if (lastAvatar != null && AvatarFieldHelper.CurrentAvatar?.AvatarObject == null)
			{
				SetAvatar(lastAvatar);
			}
		}

		private static string GetVersionString()
		{
			try
			{
				var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PBReplacerWindow).Assembly);
				if (packageInfo != null) return packageInfo.version;
			}
			catch (Exception) { }
			return "";
		}
		#endregion

		#region Rail Chip
		/// <summary>
		/// レールのチップ 1 つ。要素は UXML で定義済み（Image + Label バッジ）。C# は画像・件数・状態クラスを反映するだけ
		/// </summary>
		private class RailChip
		{
			public readonly ComponentCategory Category;
			public readonly VisualElement Root;
			public readonly Image Icon;
			public readonly Label Badge;

			public RailChip(ComponentCategory category, VisualElement root, Texture2D icon)
			{
				Category = category;
				Root = root;
				Root.tooltip = ComponentCategoryInfo.DisplayName(category);

				Icon = root.Q<Image>(className: "pbr-rail-chip-icon");
				if (Icon != null) { Icon.image = icon; Icon.scaleMode = ScaleMode.ScaleToFit; }

				Badge = root.Q<Label>(className: "pbr-rail-badge");
			}

			public void Update(int pending, int total, bool visible)
			{
				string name = ComponentCategoryInfo.DisplayName(Category);
				Root.EnableInClassList("pbr-rail-chip--off", !visible);
				Root.EnableInClassList("pbr-rail-chip--pending", pending > 0);
				Root.EnableInClassList("pbr-rail-chip--done", pending == 0 && total > 0);
				Badge.EnableInClassList("pbr-rail-badge--pending", pending > 0);
				Badge.EnableInClassList("pbr-rail-badge--done", pending == 0 && total > 0);

				if (total == 0)
				{
					Badge.text = "0";
					Root.tooltip = $"{name} — 対象なし\nクリックで列の表示切替、Alt+クリックでこの列だけ表示";
				}
				else if (pending == 0)
				{
					Badge.text = "✔";
					Root.tooltip = $"{name} — すべて配置済み ({total})\nクリックで列の表示切替、Alt+クリックでこの列だけ表示";
				}
				else
				{
					Badge.text = pending.ToString();
					Root.tooltip = $"{name} — 未処理 {pending} / 全 {total}\nクリックで列の表示切替、Alt+クリックでこの列だけ表示";
				}
			}
		}
		#endregion
	}
}
