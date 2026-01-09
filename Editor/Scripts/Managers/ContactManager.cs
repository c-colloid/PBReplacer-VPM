using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
	public class ContactDataManager : ComponentManagerBase<Component>
	{
		#region Events
		// データの変更を通知するイベント
		public event Action<List<Component>> OnContactsChanged;
        #endregion
        
        #region Properties
		public List<Component> VRCContacts => _components;
		
		public override string FolderName => _settings.ContactsFolder;
        #endregion
        
        #region Singleton Implementation
		private static ContactDataManager _instance;
		public static ContactDataManager Instance => _instance ??= new ContactDataManager();

		// プライベートコンストラクタ（シングルトンパターン）
		private ContactDataManager() 
		{
		}
		
		// デストラクタ
		~ContactDataManager()
		{
		}
        #endregion
        
        #region Public Methods		
		/// <summary>
		/// コンストレイントコンポーネントを読み込む
		/// </summary>
		public override void LoadComponents()
		{
			base.LoadComponents();
			_components = _components.Where(c => c is VRCContactSender || c is VRCContactReceiver).ToList();

			InvokeChanged();
		}
		
		public override bool ProcessComponents()
		{
			return ProcessContacts();
		}
		
		/// <summary>
		/// コンタクトコンポーネントを処理する
		/// </summary>
		public bool ProcessContacts()
		{
			return ExecuteWithErrorHandling(() => ProcessContactsInternal(), "コンタクト処理");
		}

		/// <summary>
		/// コンタクト処理の内部実装
		/// </summary>
		private bool ProcessContactsInternal()
		{
			// ルートオブジェクトを準備
			var avatarDynamics = _processor.PrepareRootObject(CurrentAvatar.AvatarObject);

			// フォルダ階層を準備（Prefab復元とクリーンアップを一括処理）
			_processor.PrepareFolderHierarchy(
				avatarDynamics,
				_settings.ContactsFolder,
				_settings.SenderFolder,
				_settings.ReceiverFolder);

			// 各コンタクト型のリストを作成
			var contactSenders = _components.OfType<VRCContactSender>().Where(c => !GetAvatarDynamicsComponent<VRCContactSender>().Contains(c)).ToList();
			var contactReceivers = _components.OfType<VRCContactReceiver>().Where(c => !GetAvatarDynamicsComponent<VRCContactReceiver>().Contains(c)).ToList();

			int processedCount = 0;

			// 各コンタクト型を処理
			if (contactSenders.Count > 0)
			{
				var result = _processor.ProcessContacts(
					CurrentAvatar.AvatarObject,
					contactSenders,
					_processor.Settings.SenderFolder);

				if (!result.Success)
				{
					NotifyStatusError(result.ErrorMessage);
					return false;
				}

				processedCount += result.ProcessedComponentCount;
			}

			if (contactReceivers.Count > 0)
			{
				var result = _processor.ProcessContacts(
					CurrentAvatar.AvatarObject,
					contactReceivers,
					_processor.Settings.ReceiverFolder);

				if (!result.Success)
				{
					NotifyStatusError(result.ErrorMessage);
					return false;
				}

				processedCount += result.ProcessedComponentCount;
			}

			// 処理結果を通知
			NotifyStatusSuccess($"処理完了! 処理コンポーネント数: {processedCount}");

			return true;
		}
		
		protected override void NotifyComponentsChanged()
		{
			base.NotifyComponentsChanged();
			OnContactsChanged?.Invoke(_components);
		}
		#endregion
		
		#region Private Methods
        #endregion
	}
}