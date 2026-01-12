using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;
using colloid.PBReplacer.StateMachine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerWindow - イベントハンドラ部分
	/// </summary>
	public partial class PBReplacerWindow
	{
		#region Event Registration
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
		#endregion

		#region Avatar Field Events
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

			// 先にステートマシンに通知（Loading状態に遷移）
			// これにより、データ読み込みイベント発火時にはLoading状態になっている
			_stateMachine?.SetAvatar(avatarObject != null);

			// その後でデータ読み込み（イベント発火時にはLoading状態）
			AvatarFieldHelper.SetAvatar(avatarObject?.gameObject);

			// 設定に保存
			_settings.SaveLastAvatarGUID(avatarObject?.gameObject);

			if (avatarObject != null) return;
			InitializeAvatarFieldLabel();
		}

		private void OnAvatarFieldLabelChanged(ChangeEvent<string> evt)
		{
			if (_avatarField.value != null) return;
			InitializeAvatarFieldLabel();
		}
		#endregion

		#region Tab Events
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
			SetComponentCountStatus();
		}
		#endregion

		#region Button Events
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

			// Commandパターンを使用して処理を実行
			ExecuteCommand(_tabContainer.value);
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

		#region Command Execution
		/// <summary>
		/// Commandパターンを使用してコンポーネント処理を実行
		/// </summary>
		/// <param name="tabIndex">タブインデックス（0: PhysBone, 1: Constraint, 2: Contact）</param>
		private void ExecuteCommand(int tabIndex)
		{
			// タブに応じたコマンドを作成
			ICommand command = CreateCommand(tabIndex);
			if (command == null) return;

			// 処理開始をステートマシンに通知
			_stateMachine?.StartProcessing();

			try
			{
				// コマンドを実行してResult型で結果を受け取る
				var result = command.Execute();

				// Result型のMatchで成功/失敗を処理
				result.Match(
					onSuccess: data =>
					{
						// 成功時の処理
						if (data.AffectedCount > 0)
						{
							Debug.Log($"{command.Description}完了: {data.AffectedCount}件処理");
						}

						// ステートマシンに処理完了を通知
						_stateMachine?.Complete(data.AffectedCount);

						// データを再読み込みしてUIを更新
						ReloadDataForTab(tabIndex);

						return data;
					},
					onFailure: error =>
					{
						// 失敗時の処理
						Debug.LogError($"{command.Description}エラー: {error.Message}");
						_stateMachine?.Fail(error.Message);
						EditorUtility.DisplayDialog("エラー", $"処理中にエラーが発生しました: {error.Message}", "OK");
						return null;
					});
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
		/// タブに応じたデータマネージャーを再読み込み
		/// </summary>
		private void ReloadDataForTab(int tabIndex)
		{
			Managers.ReloadForTab(tabIndex);

			// 処理済みコンポーネントリストを更新してUIを再描画
			GetProcessedComponents();
			RepaintAllListViews();
		}

		/// <summary>
		/// タブインデックスに応じたコマンドを作成
		/// CompositeCommandを使用してPB+PBCを一括処理
		/// FinalizeCommandで参照解決と旧コンポーネント削除を実行
		/// </summary>
		private ICommand CreateCommand(int tabIndex)
		{
			// 処理開始時にコンテキストをリセット
			ProcessingContext.Instance.BeginProcessing();

			switch (tabIndex)
			{
			case 0: // PhysBone
				// CompositeCommandでPBCとPBを順番に処理し、最後にFinalizeで削除
				var pbComposite = new CompositeCommand("PhysBone一括処理");
				pbComposite.Add(new ProcessPhysBoneColliderCommand());
				pbComposite.Add(new ProcessPhysBoneCommand());
				pbComposite.Add(new FinalizeCommand());
				return pbComposite;

			case 1: // Constraint
				var constraintComposite = new CompositeCommand("Constraint処理");
				constraintComposite.Add(new ProcessConstraintCommand());
				constraintComposite.Add(new FinalizeCommand());
				return constraintComposite;

			case 2: // Contact
				var contactComposite = new CompositeCommand("Contact処理");
				contactComposite.Add(new ProcessContactCommand());
				contactComposite.Add(new FinalizeCommand());
				return contactComposite;

			default:
				return null;
			}
		}
		#endregion

		#region Data Manager Events
		/// <summary>
		/// データマネージャーのイベントを登録
		/// </summary>
		private void RegisterDataManagerEvents()
		{
			if (_pbDataManager == null) return;

			UnregisterDataManagerEvents(); // 重複登録を防止

			AvatarFieldHelper.OnAvatarChanged += OnAvatarDataChanged;
			_pbDataManager.OnPhysBonesChanged += OnPhysBonesDataChanged;
			_pbcDataManager.OnCollidersChanged += OnPhysBoneCollidersDataChanged;
			_pbDataManager.OnPhysBonesChanged += SetPBTabNotification;
			_pbcDataManager.OnCollidersChanged += SetPBCTabNotification;
			_pbDataManager.OnPhysBonesChanged += ScheduleComponentCountStatusUpdate;
			_pbcDataManager.OnCollidersChanged += ScheduleComponentCountStatusUpdate;

			// データ読み込み完了時にステートマシンに通知
			_pbDataManager.OnPhysBonesChanged += OnDataLoadedFromManager;
			_pbcDataManager.OnCollidersChanged += OnDataLoadedFromManager;

			_pbDataManager.OnProcessingComplete += OnProcessingComplete;
			_pbcDataManager.OnProcessingComplete += OnProcessingComplete;
			_constraintDataManager.OnProcessingComplete += OnProcessingComplete;
			_contactDataManager.OnProcessingComplete += OnProcessingComplete;

			_constraintDataManager.OnConstraintsChanged += OnVRCConstraintsDataChanged;
			_constraintDataManager.OnComponentsChanged += SetConstraintTabNotification;
			_constraintDataManager.OnComponentsChanged += ScheduleComponentCountStatusUpdate;

			// データ読み込み完了時にステートマシンに通知
			_constraintDataManager.OnConstraintsChanged += OnDataLoadedFromManager;

			_contactDataManager.OnContactsChanged += OnVRCContactsDataChanged;
			_contactDataManager.OnContactsChanged += SetContactTabNotification;
			_contactDataManager.OnComponentsChanged += ScheduleComponentCountStatusUpdate;

			// データ読み込み完了時にステートマシンに通知
			_contactDataManager.OnContactsChanged += OnDataLoadedFromManager;
		}

		/// <summary>
		/// データマネージャーのイベント登録を解除
		/// </summary>
		private void UnregisterDataManagerEvents()
		{
			if (_pbDataManager == null) return;

			AvatarFieldHelper.OnAvatarChanged -= OnAvatarDataChanged;
			_pbDataManager.OnPhysBonesChanged -= OnPhysBonesDataChanged;
			_pbcDataManager.OnCollidersChanged -= OnPhysBoneCollidersDataChanged;
			_pbDataManager.OnPhysBonesChanged -= SetPBTabNotification;
			_pbcDataManager.OnCollidersChanged -= SetPBCTabNotification;
			_pbDataManager.OnPhysBonesChanged -= ScheduleComponentCountStatusUpdate;
			_pbcDataManager.OnCollidersChanged -= ScheduleComponentCountStatusUpdate;

			// データ読み込み完了通知の購読解除
			_pbDataManager.OnPhysBonesChanged -= OnDataLoadedFromManager;
			_pbcDataManager.OnCollidersChanged -= OnDataLoadedFromManager;

			_pbDataManager.OnProcessingComplete -= OnProcessingComplete;
			_pbcDataManager.OnProcessingComplete -= OnProcessingComplete;
			_constraintDataManager.OnProcessingComplete -= OnProcessingComplete;
			_contactDataManager.OnProcessingComplete -= OnProcessingComplete;

			_constraintDataManager.OnConstraintsChanged -= OnVRCConstraintsDataChanged;
			_constraintDataManager.OnComponentsChanged -= SetConstraintTabNotification;
			_constraintDataManager.OnComponentsChanged -= ScheduleComponentCountStatusUpdate;

			// データ読み込み完了通知の購読解除
			_constraintDataManager.OnConstraintsChanged -= OnDataLoadedFromManager;

			_contactDataManager.OnContactsChanged -= OnVRCContactsDataChanged;
			_contactDataManager.OnContactsChanged -= SetContactTabNotification;
			_contactDataManager.OnComponentsChanged -= ScheduleComponentCountStatusUpdate;

			// データ読み込み完了通知の購読解除
			_contactDataManager.OnContactsChanged -= OnDataLoadedFromManager;
		}

		/// <summary>
		/// アバターデータ変更時の処理
		/// </summary>
		private void OnAvatarDataChanged(AvatarData avatarData)
		{
			if (_avatarField == null) return;

			if (avatarData != null)
			{
				UpdateIdleStateFromComponents();
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
		/// データマネージャーからのデータ読み込み完了通知
		/// </summary>
		private void OnDataLoadedFromManager<T>(List<T> _)
		{
			_stateMachine?.OnDataLoaded();
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

		#region Status Helpers
		private bool _componentCountStatusScheduled = false;

		private void ScheduleComponentCountStatusUpdate<T>(List<T> _)
		{
			ScheduleComponentCountStatusUpdate();
		}

		private void ScheduleComponentCountStatusUpdate()
		{
			if (_componentCountStatusScheduled) return;
			_componentCountStatusScheduled = true;
			EditorApplication.delayCall += () =>
			{
				_componentCountStatusScheduled = false;
				UpdateIdleStateFromComponents();
			};
		}

		/// <summary>
		/// 現在のタブの未処理コンポーネントの有無を確認
		/// </summary>
		private bool HasUnprocessedComponents()
		{
			switch (_tabContainer.value)
			{
			case 0: // PhysBone (PB + PBC両方をチェック)
				var pbUnprocessed = _pbDataManager.Components.Any(c => !_processed.Contains(c));
				var pbcUnprocessed = _pbcDataManager.Components.Any(c => !_processed.Contains(c));
				return pbUnprocessed || pbcUnprocessed;
			case 1: // Constraint
				return _constraintDataManager.Components.Any(c => !_processed.Contains(c));
			case 2: // Contact
				return _contactDataManager.Components.Any(c => !_processed.Contains(c));
			default:
				return false;
			}
		}

		/// <summary>
		/// 現在のタブにコンポーネントが存在するかを確認
		/// </summary>
		private bool HasAnyComponents()
		{
			switch (_tabContainer.value)
			{
			case 0: // PhysBone (PB + PBC両方をチェック)
				return _pbDataManager.Components.Count > 0 || _pbcDataManager.Components.Count > 0;
			case 1: // Constraint
				return _constraintDataManager.Components.Count > 0;
			case 2: // Contact
				return _contactDataManager.Components.Count > 0;
			default:
				return false;
			}
		}

		/// <summary>
		/// Idle状態の種類を判定
		/// </summary>
		private IdleStateKind GetIdleStateKind()
		{
			if (HasUnprocessedComponents()) return IdleStateKind.HasUnprocessed;
			return HasAnyComponents() ? IdleStateKind.AllProcessed : IdleStateKind.NoComponents;
		}

		/// <summary>
		/// コンポーネント状態に基づいてステートマシンのIdle状態を更新
		/// </summary>
		private void UpdateIdleStateFromComponents()
		{
			// ステートマシンのIdle状態を更新
			_stateMachine?.UpdateIdleState(GetIdleStateKind());
		}

		/// <summary>
		/// タブ変更時にステータスを更新
		/// Complete/Warning/Error状態からはタイムアウトを待たずに即座にIdleに遷移
		/// </summary>
		private void SetComponentCountStatus()
		{
			_stateMachine?.OnTabChanged(GetIdleStateKind());
		}

		private void GetProcessedComponents()
		{
			_processed = DataManagerHelper.GetAvatarDynamicsComponent<Component>();
		}
		#endregion

		#region Tab Notification Helpers
		private void SetTabNotification(Toggle target, List<Component> list)
		{
			GetProcessedComponents();
			EditorApplication.delayCall += () =>
				target.value = !list.All(_processed.Contains);
		}

		private void SetPBTabNotification(List<VRCPhysBone> list)
		{
			UpdatePBTabNotification();
		}

		private void SetPBCTabNotification(List<VRCPhysBoneCollider> list)
		{
			UpdatePBTabNotification();
		}

		private void UpdatePBTabNotification()
		{
			var notification = _tabContainer.Query<Toggle>().AtIndex(0);
			// PhysBoneとPhysBoneCollider両方を結合してチェック
			var pbComponents = _pbDataManager.Components.Select(c => c as Component);
			var pbcComponents = _pbcDataManager.Components.Select(c => c as Component);
			var allComponents = pbComponents.Concat(pbcComponents).ToList();
			SetTabNotification(notification, allComponents);
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
		#endregion
	}
}
