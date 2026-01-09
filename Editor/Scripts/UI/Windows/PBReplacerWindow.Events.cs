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

			AvatarFieldHelper.SetAvatar(avatarObject?.gameObject);

			// 設定に保存
			_settings.SaveLastAvatarGUID(avatarObject?.gameObject);
			 //アバターの設定を実行
			if (avatarObject != null)
			{
				SetComponentCountStatus();
			}
			else
			{
				_statusLabel.text = STATUS_SET_AVATAR;
			}

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

						// データを再読み込みしてUIを更新
						ReloadDataForTab(tabIndex);

						return data;
					},
					onFailure: error =>
					{
						// 失敗時の処理
						Debug.LogError($"{command.Description}エラー: {error.Message}");
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
		/// </summary>
		private ICommand CreateCommand(int tabIndex)
		{
			switch (tabIndex)
			{
			case 0: // PhysBone
				// CompositeCommandでPBCとPBを順番に処理
				var pbComposite = new CompositeCommand("PhysBone一括処理");
				pbComposite.Add(new ProcessPhysBoneColliderCommand());
				pbComposite.Add(new ProcessPhysBoneCommand());
				return pbComposite;

			case 1: // Constraint
				return new ProcessConstraintCommand();

			case 2: // Contact
				return new ProcessContactCommand();

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
			_pbDataManager.OnPhysBonesChanged += SetComponentCountStatus;
			_pbcDataManager.OnCollidersChanged += SetComponentCountStatus;
			AvatarFieldHelper.OnStatusMessageChanged += OnStatusMessageChanged;

			_pbDataManager.OnProcessingComplete += OnProcessingComplete;
			_pbcDataManager.OnProcessingComplete += OnProcessingComplete;
			_constraintDataManager.OnProcessingComplete += OnProcessingComplete;
			_contactDataManager.OnProcessingComplete += OnProcessingComplete;

			_constraintDataManager.OnConstraintsChanged += OnVRCConstraintsDataChanged;
			_constraintDataManager.OnComponentsChanged += SetConstraintTabNotification;
			_constraintDataManager.OnComponentsChanged += SetComponentCountStatus;

			_contactDataManager.OnContactsChanged += OnVRCContactsDataChanged;
			_contactDataManager.OnContactsChanged += SetContactTabNotification;
			_contactDataManager.OnComponentsChanged += SetComponentCountStatus;
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
			_pbDataManager.OnPhysBonesChanged -= SetComponentCountStatus;
			_pbcDataManager.OnCollidersChanged -= SetComponentCountStatus;
			AvatarFieldHelper.OnStatusMessageChanged -= OnStatusMessageChanged;

			_pbDataManager.OnProcessingComplete -= OnProcessingComplete;
			_pbcDataManager.OnProcessingComplete -= OnProcessingComplete;
			_constraintDataManager.OnProcessingComplete -= OnProcessingComplete;
			_contactDataManager.OnProcessingComplete -= OnProcessingComplete;

			_constraintDataManager.OnConstraintsChanged -= OnVRCConstraintsDataChanged;
			_constraintDataManager.OnComponentsChanged -= SetConstraintTabNotification;
			_constraintDataManager.OnComponentsChanged -= SetComponentCountStatus;

			_contactDataManager.OnContactsChanged -= OnVRCContactsDataChanged;
			_contactDataManager.OnContactsChanged -= SetContactTabNotification;
			_contactDataManager.OnComponentsChanged -= SetComponentCountStatus;
		}

		/// <summary>
		/// アバターデータ変更時の処理
		/// </summary>
		private void OnAvatarDataChanged(AvatarData avatarData)
		{
			if (_avatarField == null) return;

			if (avatarData != null)
			{
				SetComponentCountStatus();
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

		#region Status Helpers
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

		private void SetComponentCountStatus()
		{
			OnStatusMessageChanged(ComponentCountStatus());
		}

		private void SetComponentCountStatus(List<VRCPhysBone> list)
		{
			SetComponentCountStatus();
		}

		private void SetComponentCountStatus(List<VRCPhysBoneCollider> list)
		{
			SetComponentCountStatus();
		}

		private void SetComponentCountStatus(List<VRCConstraintBase> list)
		{
			SetComponentCountStatus();
		}

		private void SetComponentCountStatus(List<Component> list)
		{
			SetComponentCountStatus();
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
