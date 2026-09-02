using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.Dynamics;
using colloid.PBReplacer.StateMachine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerWindow - イベント（アバター設定 / 再配置 / Undo / マネージャー通知 / 状態 → 流れ）
	/// </summary>
	public partial class PBReplacerWindow
	{
		#region Event Registration
		private void RegisterEvents()
		{
			UnregisterEvents();
			PBReplacerSettings.OnSettingsChanged += OnSettingsChanged;
			Undo.undoRedoPerformed += OnUndoRedo;
			EditorApplication.hierarchyChanged += OnHierarchyChanged;
		}

		private void UnregisterEvents()
		{
			PBReplacerSettings.OnSettingsChanged -= OnSettingsChanged;
			Undo.undoRedoPerformed -= OnUndoRedo;
			EditorApplication.hierarchyChanged -= OnHierarchyChanged;
		}

		private void OnSettingsChanged()
		{
			_settings = PBReplacerSettings.GetLatestSettings();
			RefreshAdvancedValues();
			Repaint();
		}
		#endregion

		#region Avatar
		/// <summary>左ノードへのドロップ（AvatarFieldDropManipulator 経由。判定済みのコンポーネントが来る）</summary>
		private void OnAvatarDrop(Component avatar)
		{
			SetAvatar(avatar != null ? avatar.gameObject : null);
		}

		/// <summary>ピッカー / メニューから来た GameObject を判定して受け入れる</summary>
		private void AcceptAvatarObject(GameObject obj)
		{
			if (obj == null) return;
			var accepted = AvatarFieldDropManipulator.ResolveAvatarComponent(obj);
			if (accepted != null) SetAvatar(accepted.gameObject);
		}

		private void SetAvatar(GameObject avatarObject)
		{
			// 先に Loading へ遷移させ、データ読み込みイベントが Loading 中に届くようにする
			_stateMachine?.SetAvatar(avatarObject != null);
			AvatarFieldHelper.SetAvatar(avatarObject);
			_settings?.SaveLastAvatarGUID(avatarObject);
			_undoAvailable = false;
			UpdateStrip();
			UpdateTools();
		}

		/// <summary>AvatarFieldHelper からの通知（解除も含む）</summary>
		private void OnAvatarDataChanged(AvatarData avatarData)
		{
			ScheduleRefresh();
		}

		/// <summary>アバター直下の AvatarDynamics（設定の RootObjectName）</summary>
		private GameObject FindAvatarDynamics()
		{
			var avatar = AvatarFieldHelper.CurrentAvatar?.AvatarObject;
			if (avatar == null) return null;
			string name = _settings?.RootObjectName ?? "AvatarDynamics";
			return avatar.transform.Find(name)?.gameObject;
		}
		#endregion

		#region Buttons
		private void OnReloadButtonClicked()
		{
			ComponentIconUtility.ClearCache();
			DataManagerHelper.ReloadData();
		}

		private void OnUndoButtonClicked()
		{
			if (!_undoAvailable) return;
			Undo.PerformUndo();
		}

		private void OnGearButtonClicked()
		{
			SetAdvancedVisible(!IsAdvancedVisible);
		}

		private void OnOverflowMenuButtonClicked()
		{
			var menu = new GenericMenu();
			GameObject avatar = AvatarFieldHelper.CurrentAvatar?.AvatarObject;

			if (avatar != null)
			{
				menu.AddItem(new GUIContent(MENU_ITEM_PBREMAP), false, () => AddPBRemapToAvatar(avatar));
			}
			else
			{
				menu.AddDisabledItem(new GUIContent(MENU_ITEM_PBREMAP));
			}

			menu.DropDown(_menuButton.worldBound);
		}

		/// <summary>
		/// アバターのAvatarDynamics（PBReplacerが生成した階層）にPBRemapを追加し、Inspectorへ誘導する。
		/// AvatarDynamicsが無い場合は専用の子オブジェクト（"PBRemap"）を作成する。
		/// PBRemapは「移植したい階層のルート」に付け、そのオブジェクトごと移植先へD&amp;Dする。
		/// </summary>
		private void AddPBRemapToAvatar(GameObject avatar)
		{
			string dynamicsName = _settings?.RootObjectName ?? "AvatarDynamics";
			Transform container = avatar.transform.Find(dynamicsName) ?? avatar.transform.Find(PBREMAP_CONTAINER_NAME);
			GameObject containerObject;
			if (container != null)
			{
				containerObject = container.gameObject;
			}
			else
			{
				containerObject = new GameObject(PBREMAP_CONTAINER_NAME);
				containerObject.transform.SetParent(avatar.transform);
				containerObject.transform.localPosition = Vector3.zero;
				containerObject.transform.localRotation = Quaternion.identity;
				containerObject.transform.localScale = Vector3.one;
				Undo.RegisterCreatedObjectUndo(containerObject, $"Create {PBREMAP_CONTAINER_NAME}");
			}

			PBRemap remap = containerObject.GetComponent<PBRemap>();
			if (remap == null)
			{
				remap = Undo.AddComponent<PBRemap>(containerObject);
				// 移植元にいる今のうちに参照情報を確定する
				PBRemapper.RefreshManifestIfLive(remap);
			}

			EditorGUIUtility.PingObject(containerObject);
			Selection.activeObject = containerObject;
		}

		private void OnUndoRedo()
		{
			_undoAvailable = false;
			DataManagerHelper.ReloadData();
			ScheduleRefresh();
		}

		private void OnHierarchyChanged()
		{
			// 再配置直後の階層変更は自分のものなので無視。それ以外の変更があったら ↶ は対象外になる
			if (!_undoAvailable) return;
			if (EditorApplication.timeSinceStartup < _ignoreHierarchyChangesUntil) return;
			_undoAvailable = false;
			UpdateTools();
		}
		#endregion

		#region Apply
		/// <summary>
		/// 再配置（主操作）。対象は「表示中のカテゴリが属する処理グループ」の未処理コンポーネント。
		/// PhysBone と PhysBoneCollider は参照解決のため常に同時に処理する。
		/// </summary>
		private void OnApplyButtonClicked()
		{
			if (AvatarFieldHelper.CurrentAvatar?.AvatarObject == null) return;

			var groups = VisibleProcessGroups().Where(g => PendingCount(g) > 0).ToList();
			if (groups.Count == 0) return;

			int count = groups.Sum(PendingCount);

			if (_settings.ShowConfirmDialog)
			{
				bool skipConfirm = _settings.SkipConfirmForSmallBatches && count <= _settings.SkipConfirmThreshold;
				if (!skipConfirm)
				{
					string targets = string.Join(" / ", groups.Select(ComponentCategoryInfo.ProcessGroupName));
					bool proceed = EditorUtility.DisplayDialog(
						$"{targets} {count}件を再配置します",
						$"各コンポーネントを 1オブジェクト＝1コンポーネント に分けて {(_settings.RootObjectName ?? "AvatarDynamics")} 配下へ移動します。\n元の設定値は保持されます。Ctrl+Z で元に戻せます。",
						"再配置する",
						"やめる");
					if (!proceed) return;
				}
			}

			ExecuteApply(groups, count);
		}

		private void ExecuteApply(List<int> groups, int expectedCount)
		{
			// 処理開始時にコンテキストをリセット（グループをまたいで 1 回だけ）
			ProcessingContext.Instance.BeginProcessing();

			var composite = new CompositeCommand("再配置");
			foreach (var group in groups.OrderBy(g => g))
			{
				var command = CreateCommand(group);
				if (command != null) composite.Add(command);
			}

			_stateMachine?.StartProcessing();
			_undoAvailable = false;

			try
			{
				var result = composite.Execute();

				result.Match(
					onSuccess: data =>
					{
						if (data.AffectedCount > 0)
						{
							Debug.Log($"[PBReplacer] 再配置完了: {data.AffectedCount}件処理");
						}

						_stateMachine?.Complete(data.AffectedCount);

						// ↶ を有効化。直後に届く自分自身の階層変更通知は無視する
						_undoAvailable = data.AffectedCount > 0;
						_ignoreHierarchyChangesUntil = EditorApplication.timeSinceStartup + 1.0;

						Managers.ReloadAll();
						FlashStrip();
						return data;
					},
					onFailure: error =>
					{
						Debug.LogError($"[PBReplacer] 再配置エラー: {error.Message}");
						_stateMachine?.Fail(error.Message);
						Managers.ReloadAll();
						return null;
					});
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				ScheduleRefresh();
			}
		}

		/// <summary>
		/// 処理グループに応じたコマンドを作成
		/// CompositeCommandを使用してPB+PBCを一括処理
		/// FinalizeCommandで参照解決と旧コンポーネント削除を実行
		/// </summary>
		private ICommand CreateCommand(int group)
		{
			switch (group)
			{
			case 0: // PhysBone（PBC → PB の順に処理し、最後に Finalize で削除）
				var pbComposite = new CompositeCommand("PhysBone一括処理");
				pbComposite.Add(new ProcessPhysBoneColliderCommand());
				pbComposite.Add(new ProcessPhysBoneCommand());
				pbComposite.Add(new CleanupUnusedFoldersCommand(_settings.PhysBonesFolder, _settings.PhysBoneCollidersFolder));
				pbComposite.Add(new FinalizeCommand());
				return pbComposite;

			case 1: // Constraint
				var constraintComposite = new CompositeCommand("Constraint処理");
				constraintComposite.Add(new ProcessConstraintCommand());
				constraintComposite.Add(new CleanupUnusedFoldersCommand(_settings.ConstraintsFolder));
				constraintComposite.Add(new FinalizeCommand());
				return constraintComposite;

			case 2: // Contact
				var contactComposite = new CompositeCommand("Contact処理");
				contactComposite.Add(new ProcessContactCommand());
				contactComposite.Add(new CleanupUnusedFoldersCommand(_settings.ContactsFolder));
				contactComposite.Add(new FinalizeCommand());
				return contactComposite;

			default:
				return null;
			}
		}
		#endregion

		#region Data Manager Events
		private void RegisterDataManagerEvents()
		{
			if (_pbDataManager == null) return;
			UnregisterDataManagerEvents();

			AvatarFieldHelper.OnAvatarChanged += OnAvatarDataChanged;
			_pbDataManager.OnPhysBonesChanged += OnPhysBonesChanged;
			_pbcDataManager.OnCollidersChanged += OnCollidersChanged;
			_constraintDataManager.OnConstraintsChanged += OnConstraintsChanged;
			_contactDataManager.OnContactsChanged += OnContactsChanged;

			_pbDataManager.OnProcessingComplete += OnProcessingComplete;
			_pbcDataManager.OnProcessingComplete += OnProcessingComplete;
			_constraintDataManager.OnProcessingComplete += OnProcessingComplete;
			_contactDataManager.OnProcessingComplete += OnProcessingComplete;
		}

		private void UnregisterDataManagerEvents()
		{
			if (_pbDataManager == null) return;

			AvatarFieldHelper.OnAvatarChanged -= OnAvatarDataChanged;
			_pbDataManager.OnPhysBonesChanged -= OnPhysBonesChanged;
			_pbcDataManager.OnCollidersChanged -= OnCollidersChanged;
			_constraintDataManager.OnConstraintsChanged -= OnConstraintsChanged;
			_contactDataManager.OnContactsChanged -= OnContactsChanged;

			_pbDataManager.OnProcessingComplete -= OnProcessingComplete;
			_pbcDataManager.OnProcessingComplete -= OnProcessingComplete;
			_constraintDataManager.OnProcessingComplete -= OnProcessingComplete;
			_contactDataManager.OnProcessingComplete -= OnProcessingComplete;
		}

		private void OnPhysBonesChanged(List<VRCPhysBone> _) => OnCategoryDataChanged();
		private void OnCollidersChanged(List<VRCPhysBoneCollider> _) => OnCategoryDataChanged();
		private void OnConstraintsChanged(List<VRCConstraintBase> _) => OnCategoryDataChanged();
		private void OnContactsChanged(List<Component> _) => OnCategoryDataChanged();

		private void OnCategoryDataChanged()
		{
			_stateMachine?.OnDataLoaded();
			ScheduleRefresh();
		}

		private void OnProcessingComplete()
		{
			ScheduleRefresh();
		}
		#endregion

		#region Status → Strip
		private void OnStateMachineStateChanged(StatusStateContext context)
		{
			EditorApplication.delayCall += UpdateStrip;
		}

		private void UpdateIdleStateFromComponents()
		{
			if (_stateMachine == null) return;
			IdleStateKind kind;
			if (TotalPending() > 0) kind = IdleStateKind.HasUnprocessed;
			else if (TotalComponents() > 0 || TotalProcessed() > 0) kind = IdleStateKind.AllProcessed;
			else kind = IdleStateKind.NoComponents;
			_stateMachine.UpdateIdleState(kind);
		}

		/// <summary>
		/// 流れの見た目をデータと状態機械から決める。
		/// 未設定: 左が空枠、ピル無効 / 未処理あり: 線が琥珀、ピル緑 / すべて配置済み: 線が緑、中央 ✔ /
		/// 処理中: ピル無効 / エラー: 背景赤、中央にエラー記号（ツールチップに内容）
		/// </summary>
		private void UpdateStrip()
		{
			if (_strip == null) return;

			var avatarData = AvatarFieldHelper.CurrentAvatar;
			var avatar = avatarData?.AvatarObject;
			var state = _stateMachine?.CurrentStateType ?? StatusStateType.None;
			string rootName = _settings?.RootObjectName ?? "AvatarDynamics";

			int totalComponents = TotalComponents();
			int totalProcessed = TotalProcessed();
			var groups = VisibleProcessGroups();
			int visiblePending = groups.Sum(PendingCount);
			int allPending = TotalPending();

			// ---- 左ノード: アバター ----
			if (avatar == null)
			{
				_nodeAvatar.AddToClassList("pbr-node--empty");
				PBRemapIcons.Set(_nodeAvatarIcon, PBRemapIcons.Unlinked);
				_nodeAvatarBadge.style.display = DisplayStyle.None;
				_nodeAvatarName.text = AVATAR_EMPTY_NAME;
				_nodeAvatarSub.text = AVATAR_EMPTY_SUB;
				_nodeAvatar.tooltip = "Hierarchy からアバターをドロップ、またはクリックで選択";
			}
			else
			{
				_nodeAvatar.RemoveFromClassList("pbr-node--empty");
				var method = AvatarValidator.Validate(avatar).Method;
				PBRemapIcons.Set(_nodeAvatarIcon, IconForDetection(method));
				_nodeAvatarBadge.style.display = DisplayStyle.None;
				_nodeAvatarName.text = avatar.name;
				_nodeAvatarSub.text = state == StatusStateType.Loading
					? $"{DetectionLabel(method)} · 読み込み中…"
					: $"{DetectionLabel(method)} · Armature 内 {totalComponents} 件";
				_nodeAvatar.tooltip = "クリックで別のアバターを選択、右クリックで Hierarchy 表示 / 解除";
			}

			// ---- 右ノード: AvatarDynamics ----
			var dynamics = FindAvatarDynamics();
			_nodeDynamicsName.text = rootName;
			if (dynamics == null)
			{
				PBRemapIcons.Set(_nodeDynamicsIcon, PBRemapIcons.Empty);
				_nodeDynamicsSub.text = avatar == null ? "" : "未作成";
				_nodeDynamics.tooltip = "再配置すると作成されます";
				_nodeDynamics.AddToClassList("pbr-node--ghost");
			}
			else
			{
				PBRemapIcons.Set(_nodeDynamicsIcon, PBRemapIcons.Prefab);
				_nodeDynamicsSub.text = $"配置済み {totalProcessed} 件";
				_nodeDynamics.tooltip = "クリックで Hierarchy に表示";
				_nodeDynamics.RemoveFromClassList("pbr-node--ghost");
			}

			// ---- 背景・線・中央 ----
			_strip.RemoveFromClassList("pbr-strip--home");
			_strip.RemoveFromClassList("pbr-strip--displaced");
			_strip.RemoveFromClassList("pbr-strip--error");
			_lineLeft.RemoveFromClassList("pbr-line--active");
			_lineLeft.RemoveFromClassList("pbr-line--home");
			_lineRight.RemoveFromClassList("pbr-line--active");
			_lineRight.RemoveFromClassList("pbr-line--home");
			_applyButton.RemoveFromClassList("pbr-apply--ready");
			_applyButton.RemoveFromClassList("pbr-apply--partial");
			_applyButton.RemoveFromClassList("pbr-apply--hidden");
			_connectorState.style.display = DisplayStyle.None;

			string verb = "再配置";

			if (avatar == null)
			{
				_applyLabel.text = verb;
				_applyButton.SetEnabled(false);
				_applyButton.tooltip = "先にアバターを設定してください";
				return;
			}

			if (state == StatusStateType.Error)
			{
				_strip.AddToClassList("pbr-strip--error");
				_connectorState.style.display = DisplayStyle.Flex;
				PBRemapIcons.Set(_connectorState, PBRemapIcons.Error, _stateMachine?.Context?.Message ?? "エラー");
				_applyLabel.text = allPending > 0 ? $"{verb} {visiblePending}" : verb;
				_applyButton.SetEnabled(visiblePending > 0);
				_applyButton.tooltip = "Console にエラーの内容があります。原因を直してからもう一度再配置してください";
				return;
			}

			if (state == StatusStateType.Loading || state == StatusStateType.Processing)
			{
				_applyLabel.text = allPending > 0 ? $"{verb} {visiblePending}" : verb;
				_applyButton.SetEnabled(false);
				_applyButton.tooltip = state == StatusStateType.Loading ? "読み込み中…" : "処理中…";
				return;
			}

			if (allPending == 0)
			{
				// すべて配置済み（または対象なし）: 線が緑、中央は Linked
				bool anything = totalComponents > 0 || totalProcessed > 0;
				if (anything)
				{
					_strip.AddToClassList("pbr-strip--home");
					_lineLeft.AddToClassList("pbr-line--home");
					_lineRight.AddToClassList("pbr-line--home");
					_applyButton.AddToClassList("pbr-apply--hidden");
					_connectorState.style.display = DisplayStyle.Flex;
					PBRemapIcons.Set(_connectorState, PBRemapIcons.Linked, $"すべて {rootName} に配置済みです");
				}
				else
				{
					_applyLabel.text = verb;
					_applyButton.SetEnabled(false);
					_applyButton.tooltip = "Armature 内に対象のコンポーネントがありません。検索範囲は ⚙ 詳細設定で広げられます";
				}
				return;
			}

			// 未処理あり
			_strip.AddToClassList("pbr-strip--displaced");
			_lineLeft.AddToClassList("pbr-line--active");
			_lineRight.AddToClassList("pbr-line--active");

			if (visiblePending == 0)
			{
				_applyLabel.text = verb;
				_applyButton.SetEnabled(false);
				_applyButton.tooltip = $"表示中のカテゴリに未処理はありません（他に {allPending} 件）。左のチップで表示を切り替えてください";
				return;
			}

			bool partial = visiblePending < allPending;
			_applyButton.AddToClassList(partial ? "pbr-apply--partial" : "pbr-apply--ready");
			_applyButton.SetEnabled(true);
			_applyLabel.text = $"{verb} {visiblePending}";

			var names = groups.Where(g => PendingCount(g) > 0).Select(g => $"{ComponentCategoryInfo.ProcessGroupName(g)} {PendingCount(g)}");
			string detail = string.Join(" · ", names);
			_applyButton.tooltip = partial
				? $"{detail} を {rootName} へ再配置（表示中のカテゴリのみ。残り {allPending - visiblePending} 件）\nPhysBone と Collider は常に同時に処理されます。Ctrl+Z で元に戻せます"
				: $"{detail} を {rootName} へ再配置\nCtrl+Z で元に戻せます";
		}

		/// <summary>成功時: 背景が緑に光って戻る（PBRemap と同じ）</summary>
		private void FlashStrip()
		{
			if (_strip == null) return;
			_strip.AddToClassList("pbr-strip--flash");
			_strip.schedule.Execute(() => _strip.RemoveFromClassList("pbr-strip--flash")).StartingIn(450);
		}

		private static string IconForDetection(AvatarDetectionMethod method)
		{
			switch (method)
			{
				case AvatarDetectionMethod.VRCAvatarDescriptor: return PBRemapIcons.Avatar;
				case AvatarDetectionMethod.MergeArmature: return PBRemapIcons.Costume;
				case AvatarDetectionMethod.Animator: return PBRemapIcons.Animator;
				default: return PBRemapIcons.Prop;
			}
		}

		private static string DetectionLabel(AvatarDetectionMethod method)
		{
			switch (method)
			{
				case AvatarDetectionMethod.VRCAvatarDescriptor: return "VRC Avatar Descriptor";
				case AvatarDetectionMethod.MergeArmature: return "MA Merge Armature";
				case AvatarDetectionMethod.Animator: return "Animator";
				default: return "GameObject";
			}
		}
		#endregion
	}
}
