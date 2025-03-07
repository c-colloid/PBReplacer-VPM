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
			_components.Clear();

			if (CurrentAvatar?.Armature == null)
			{
				InvokeChanged();
				return;
			}

			// アーマチュア内のコンポーネントを取得
			var senderComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCContactSender>(true);
			_components.AddRange(senderComponents);
			// AvatarDynamics内のコンポーネントを取得
			_components.AddRange(GetAvatarDynamicsComponent<VRCContactSender>());
			
			var receiverComponents = CurrentAvatar.Armature.GetComponentsInChildren<ContactReceiver>(true);
			_components.AddRange(receiverComponents);
			_components.AddRange(GetAvatarDynamicsComponent<ContactReceiver>());
			
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
			if (CurrentAvatar == null || CurrentAvatar.AvatarObject == null)
			{
				NotifyStatusMessage("アバターが設定されていません");
				return false;
			}

			try
			{
				// 各コンタクト型のリストを作成
				var contactSenders = _components.OfType<VRCContactSender>().ToList();
				var contactReceivers = _components.OfType<VRCContactReceiver>().ToList();
                
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
						NotifyStatusMessage($"エラー: {result.ErrorMessage}");
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
						NotifyStatusMessage($"エラー: {result.ErrorMessage}");
						return false;
					}
                    
					processedCount += result.ProcessedComponentCount;
				}
                
				// 処理結果を通知
				NotifyStatusMessage($"処理完了! 処理コンポーネント数: {processedCount}");
                
				// データを再読み込み
				ReloadData();
                
				// 処理完了通知
				NotifyProcessingComplete();
                
				return true;
			}
				catch (Exception ex)
				{
					Debug.LogError($"コンストレイント処理中にエラーが発生しました: {ex.Message}");
					NotifyStatusMessage($"エラー: {ex.Message}");
					return false;
				}
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