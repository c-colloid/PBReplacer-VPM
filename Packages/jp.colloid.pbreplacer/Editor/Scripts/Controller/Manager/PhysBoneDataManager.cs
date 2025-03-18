using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDKBase;
using VRC.SDK3.Dynamics.PhysBone.Components;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerのデータを管理するクラス
	/// PhysBoneとPhysBoneColliderの情報を保持し、それらの処理を行う
	/// </summary>
	public class PhysBoneDataManager : ComponentManagerBase<VRCPhysBone>
	{
        #region Events
		// データの変更を通知するイベント
		public event Action<List<VRCPhysBone>> OnPhysBonesChanged;
		public event Action<List<VRCPhysBoneCollider>> OnPhysBoneCollidersChanged;
        #endregion

        #region Properties
		private List<VRCPhysBoneCollider> _physBoneColliders = new List<VRCPhysBoneCollider>();

		public List<VRCPhysBone> PhysBones => Components;
		public List<VRCPhysBoneCollider> PhysBoneColliders => _physBoneColliders;
        #endregion

        #region Singleton Implementation
		private static PhysBoneDataManager _instance;
		public static PhysBoneDataManager Instance => _instance ??= new PhysBoneDataManager();

		// プライベートコンストラクタ（シングルトンパターン）
		private PhysBoneDataManager() : base()
		{

		}
        #endregion

        #region Public Methods	
		public override void LoadComponents()
		{
			_components.Clear();
			_physBoneColliders.Clear();

			if (CurrentAvatar?.Armature == null)
			{
				InvokeChanged();
				return;
			}

			// アーマチュア内のコンポーネントを取得
			var pbComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCPhysBone>(true);
			var pbcComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCPhysBoneCollider>(true);
			
			_components.AddRange(pbComponents);
			_physBoneColliders.AddRange(pbcComponents);
        
			// AvatarDynamics内のコンポーネントを取得
			//LoadComponentsFromAvatarDynamics();
			_components.AddRange(GetAvatarDynamicsComponent<VRCPhysBone>());
			_physBoneColliders.AddRange(GetAvatarDynamicsComponent<VRCPhysBoneCollider>());
        
			// 変更を通知
			InvokeChanged();
		}
		
		/*
		private void LoadComponentsFromAvatarDynamics()
		{
			if (CurrentAvatar?.AvatarObject == null) return;
        
			var avatarDynamics = CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics");
			if (avatarDynamics == null) return;
        
			var physBonesParent = avatarDynamics.Find("PhysBones");
			if (physBonesParent != null)
			{
				var additionalPBs = physBonesParent.GetComponentsInChildren<VRCPhysBone>(true);
				foreach (var pb in additionalPBs)
				{
					if (!_components.Contains(pb))
					{
						_components.Add(pb);
					}
				}
			}
			
			var collidersParent = avatarDynamics.transform.Find("PhysBoneColliders");
			if (collidersParent != null)
			{
				var additionalColliders = collidersParent.GetComponentsInChildren<VRCPhysBoneCollider>(true);
				foreach (var collider in additionalColliders)
				{
					if (!_physBoneColliders.Contains(collider))
					{
						_physBoneColliders.Add(collider);
					}
				}
			}
		}
		*/
		
		public override bool ProcessComponents()
		{
			return ProcessReplacement();
		}

		/// <summary>
		/// PhysBoneコンポーネントを再配置する処理を実行
		/// </summary>
		/// <returns>成功した場合はtrue</returns>
		public bool ProcessReplacement()
		{
			if (CurrentAvatar == null || CurrentAvatar.AvatarObject == null)
			{
				NotifyStatusMessage("アバターが設定されていません");
				return false;
			}

			try
			{
				var targetPB = _components.Where(c => !GetAvatarDynamicsComponent<VRCPhysBone>().Contains(c)).ToList();
				var targetPBC = _physBoneColliders.Where(c => !GetAvatarDynamicsComponent<VRCPhysBoneCollider>().Contains(c)).ToList();
				
				// 単一プロセッサを使用してPhysBoneを処理
				var result = _processor.ProcessPhysBones(
					CurrentAvatar.AvatarObject, 
					targetPB, 
					targetPBC);
                
				if (!result.Success)
				{
					NotifyStatusMessage($"エラー: {result.ErrorMessage}");
					return false;
				}
                
				// 処理結果を通知
				string message = $"処理完了! 処理コンポーネント数: {result.ProcessedComponentCount}";
				NotifyStatusMessage(message);
                
				// データを再読み込み
				ReloadData();
                
				// 処理完了通知
				NotifyProcessingComplete();
                
				return true;
			}
				catch (Exception ex)
				{
					Debug.LogError($"PhysBone置換中にエラーが発生しました: {ex.Message}");
					NotifyStatusMessage($"エラー: {ex.Message}");
					return false;
				}
		}

		/// <summary>
		/// データをリロードする
		/// </summary>
		public override void ReloadData()
		{
			if (CurrentAvatar?.AvatarObject != null)
			{
				// 現在のアバターを保持したまま再ロード
				GameObject currentAvatar = CurrentAvatar.AvatarObject;
				AvatarFieldHelper.SetAvatar(currentAvatar);
			}
		}

		/// <summary>
		/// データをクリアする
		/// </summary>
		public override void ClearData()
		{
			base.ClearData();
			
			_physBoneColliders.Clear();
            
			OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
		}
		
		/// <summary>
		/// PhysBoneとPhysBoneColliderのコンポーネントを読み込む
		/// </summary>
		public void LoadPhysBoneComponents()
		{
			_components.Clear();
			_physBoneColliders.Clear();

			if (CurrentAvatar?.Armature == null) return;

			// アーマチュア内のコンポーネントを取得
			var pbComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCPhysBone>(true);
			var pbcComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCPhysBoneCollider>(true);

			_components.AddRange(pbComponents);
			_physBoneColliders.AddRange(pbcComponents);
            
			// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
			if (CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics") != null)
			{
				var avatarDynamics = CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics").gameObject;
                
				// PhysBoneを検索して追加
				if (avatarDynamics.transform.Find("PhysBones") != null)
				{
					var physBonesParent = avatarDynamics.transform.Find("PhysBones");
					var additionalPBs = physBonesParent.GetComponentsInChildren<VRCPhysBone>(true);
					foreach (var pb in additionalPBs)
					{
						if (!_components.Contains(pb))
						{
							_components.Add(pb);
						}
					}
				}
                
				// PhysBoneColliderを検索して追加
				if (avatarDynamics.transform.Find("PhysBoneColliders") != null)
				{
					var collidersParent = avatarDynamics.transform.Find("PhysBoneColliders");
					var additionalColliders = collidersParent.GetComponentsInChildren<VRCPhysBoneCollider>(true);
					foreach (var collider in additionalColliders)
					{
						if (!_physBoneColliders.Contains(collider))
						{
							_physBoneColliders.Add(collider);
						}
					}
				}
			}
			
			InvokeChanged();
		}
		
		public override void InvokeChanged()
		{
			base.InvokeChanged();
			OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
		}
		
		protected override void NotifyComponentsChanged()
		{
			base.NotifyComponentsChanged();
        
			// 従来のイベントも発火
			OnPhysBonesChanged?.Invoke(_components);
		}
		
		public override void Cleanup()
		{
			base.Cleanup();
			OnPhysBonesChanged = null;
		}
        #endregion
        
        #region PhysBoneCollider Methods
		/// <summary>
		/// 特定のPhysBoneColliderを追加
		/// </summary>
		public void AddPhysBoneCollider(VRCPhysBoneCollider collider)
		{
			if (collider != null && !_physBoneColliders.Contains(collider))
			{
				_physBoneColliders.Add(collider);
				NotifyPhysBoneCollidersChanged();
			}
		}
		
		/// <summary>
		/// 特定のPhysBoneColliderを削除
		/// </summary>
		public void RemovePhysBoneCollider(VRCPhysBoneCollider collider)
		{
			if (_physBoneColliders.Contains(collider))
			{
				_physBoneColliders.Remove(collider);
				NotifyPhysBoneCollidersChanged();
			}
		}
		
		/// <summary>
		/// PhysBoneCollider変更の通知
		/// </summary>
		private void NotifyPhysBoneCollidersChanged()
		{
			OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
		}
        #endregion

        #region Private Methods
        #endregion
	}
}