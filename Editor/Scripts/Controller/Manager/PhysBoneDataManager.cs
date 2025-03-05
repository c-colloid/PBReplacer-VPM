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
		//public event Action OnProcessingComplete;
		//public event Action<string> OnStatusMessageChanged;
        #endregion

        #region Properties
		// 現在のアバターデータ
		//private AvatarData CurrentAvatar;
		//public AvatarData CurrentAvatar
		//{
		//	get => AvatarFieldHelper.CurrentAvatar;
		//	//set => CurrentAvatar = value;
		//}

		// PhysBoneとPhysBoneColliderのコンポーネントリスト
		//private List<VRCPhysBone> _physBones = new List<VRCPhysBone>();
		private List<VRCPhysBoneCollider> _physBoneColliders = new List<VRCPhysBoneCollider>();

		public List<VRCPhysBone> PhysBones => Components;
		public List<VRCPhysBoneCollider> PhysBoneColliders => _physBoneColliders;

		// PhysBone処理クラスへの参照
		//private PhysBoneProcessor _processor;
		//private ComponentProcessor _processor;
        
		// 設定への参照
		//private PBReplacerSettings _settings;
        #endregion

        #region Singleton Implementation
		private static PhysBoneDataManager _instance;
		public static PhysBoneDataManager Instance => _instance ??= new PhysBoneDataManager();

		// プライベートコンストラクタ（シングルトンパターン）
		private PhysBoneDataManager() : base()
		{
			//_settings = PBReplacerSettings.Load();
			//_processor = new PhysBoneProcessor(_settings);
			//_processor = new ComponentProcessor(_settings);
		}
        #endregion

        #region Public Methods
		/// <summary>
		/// アバターを設定し、PhysBoneコンポーネントを読み込む
		/// </summary>
		/// <param name="avatarObject">アバターのGameObject</param>
		/// <returns>成功した場合はtrue</returns>
		/**
		public bool SetAvatar(GameObject avatarObject)
		{
			if (avatarObject == null)
			{
				ClearData();
				NotifyStatusMessage("アバターをセットしてください");
				return false;
			}

			try
			{
				// アバターデータを生成
				CurrentAvatar = new AvatarData(avatarObject);
                
				// PhysBoneとPhysBoneColliderを取得
				LoadPhysBoneComponents();
                
				// 変更通知
				OnAvatarChanged?.Invoke(CurrentAvatar);
				OnPhysBonesChanged?.Invoke(_physBones);
				OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
                
				NotifyStatusMessage(_physBones.Count > 0 || _physBoneColliders.Count > 0 ? 
					"Applyを押してください" : 
					"Armature内にPhysBoneが見つかりません");
                
				return true;
			}
				catch (Exception ex)
				{
					Debug.LogError($"アバターの設定中にエラーが発生しました: {ex.Message}");
					ClearData();
					NotifyStatusMessage($"エラー: {ex.Message}");
					return false;
				}
		}
		**/
		
		public override void LoadComponents()
		{
			_components.Clear();
			_physBoneColliders.Clear();

			if (CurrentAvatar?.Armature == null) return;

			// アーマチュア内のコンポーネントを取得
			var pbComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCPhysBone>(true);
			var pbcComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCPhysBoneCollider>(true);
			
			_components.AddRange(pbComponents);
			_physBoneColliders.AddRange(pbcComponents);
        
			// AvatarDynamics内のコンポーネントを取得
			LoadComponentsFromAvatarDynamics();
        
			// 変更を通知
			InvokeChanged();
		}
		
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
			
			//NotifyComponentsChanged();
			//NotifyPhysBoneCollidersChanged();
		}
		
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
				// 単一プロセッサを使用してPhysBoneを処理
				var result = _processor.ProcessPhysBones(
					CurrentAvatar.AvatarObject, 
					_components, 
					_physBoneColliders);
                
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
				InvokeChanged();
                
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
		/// 特定のPhysBoneを追加
		/// </summary>
		//public void AddPhysBone(VRCPhysBone physBone)
		//{
		//	if (physBone != null && !Components.Contains(physBone))
		//	{
		//		Components.Add(physBone);
		//		OnComponentsChanged?.Invoke(Components);
		//	}
		//}


		/// <summary>
		/// 特定のPhysBoneを削除
		/// </summary>
		//public void RemovePhysBone(VRCPhysBone physBone)
		//{
		//	if (Components.Contains(physBone))
		//	{
		//		Components.Remove(physBone);
		//		OnComponentsChanged?.Invoke(Components);
		//	}
		//}

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
		/// <summary>
		/// アバターデータが変更された時の処理
		/// </summary>
		//private void OnAvatarChanged(AvatarData avatarData)
		//{
		//	// アバター変更時にコンポーネントを再ロード
		//	LoadPhysBoneComponents();
		//}
        
		/// <summary>
		/// ステータスメッセージの通知
		/// </summary>
		//private void NotifyStatusMessage(string message)
		//{
		//	OnStatusMessageChanged?.Invoke(message);
		//}
        #endregion
	}
}