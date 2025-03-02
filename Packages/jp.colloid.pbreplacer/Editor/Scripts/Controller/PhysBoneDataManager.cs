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
	public class PhysBoneDataManager
	{
        #region Events
		// データの変更を通知するイベント
		public event Action<AvatarData> OnAvatarChanged;
		public event Action<List<VRCPhysBone>> OnPhysBonesChanged;
		public event Action<List<VRCPhysBoneCollider>> OnPhysBoneCollidersChanged;
		public event Action OnProcessingComplete;
		public event Action<string> OnStatusMessageChanged;
        #endregion

        #region Properties
		// 現在のアバターデータ
		private AvatarData _currentAvatar;
		public AvatarData CurrentAvatar
		{
			get => _currentAvatar;
			set => _currentAvatar = value;
		}

		// PhysBoneとPhysBoneColliderのコンポーネントリスト
		private List<VRCPhysBone> _physBones = new List<VRCPhysBone>();
		private List<VRCPhysBoneCollider> _physBoneColliders = new List<VRCPhysBoneCollider>();

		public List<VRCPhysBone> PhysBones => _physBones;
		public List<VRCPhysBoneCollider> PhysBoneColliders => _physBoneColliders;

		// PhysBone処理クラスへの参照
		private PhysBoneProcessor _processor;
        
		// 設定への参照
		private PBReplacerSettings _settings;
        #endregion

        #region Singleton Implementation
		private static PhysBoneDataManager _instance;
		public static PhysBoneDataManager Instance => _instance ??= new PhysBoneDataManager();

		// プライベートコンストラクタ（シングルトンパターン）
		private PhysBoneDataManager() 
		{
			_settings = PBReplacerSettings.Load();
			_processor = new PhysBoneProcessor(_settings);
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
				_currentAvatar = new AvatarData(avatarObject);
                
				// PhysBoneとPhysBoneColliderを取得
				LoadPhysBoneComponents();
                
				// 変更通知
				OnAvatarChanged?.Invoke(_currentAvatar);
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

		/// <summary>
		/// PhysBoneコンポーネントを再配置する処理を実行
		/// </summary>
		/// <returns>成功した場合はtrue</returns>
		public bool ProcessReplacement()
		{
			if (_currentAvatar == null || _currentAvatar.AvatarObject == null)
			{
				NotifyStatusMessage("アバターが設定されていません");
				return false;
			}

			try
			{
				// プロセッサがなければ作成
				if (_processor == null)
				{
					_processor = new PhysBoneProcessor(_settings);
				}
                
				// PhysBoneの処理を実行
				var result = _processor.ProcessPhysBones(
					_currentAvatar.AvatarObject, 
					_physBones, 
					_physBoneColliders);
                
				if (!result.Success)
				{
					NotifyStatusMessage($"エラー: {result.ErrorMessage}");
					return false;
				}
                
				// 処理結果を通知
				string message = $"処理完了! PhysBone: {result.ProcessedPhysBoneCount}, Collider: {result.ProcessedColliderCount}";
				NotifyStatusMessage(message);
                
				// データを再読み込み
				ReloadData();
                
				// 処理完了通知
				OnProcessingComplete?.Invoke();
                
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
		public void AddPhysBone(VRCPhysBone physBone)
		{
			if (physBone != null && !_physBones.Contains(physBone))
			{
				_physBones.Add(physBone);
				OnPhysBonesChanged?.Invoke(_physBones);
			}
		}

		/// <summary>
		/// 特定のPhysBoneColliderを追加
		/// </summary>
		public void AddPhysBoneCollider(VRCPhysBoneCollider collider)
		{
			if (collider != null && !_physBoneColliders.Contains(collider))
			{
				_physBoneColliders.Add(collider);
				OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
			}
		}

		/// <summary>
		/// 特定のPhysBoneを削除
		/// </summary>
		public void RemovePhysBone(VRCPhysBone physBone)
		{
			if (_physBones.Contains(physBone))
			{
				_physBones.Remove(physBone);
				OnPhysBonesChanged?.Invoke(_physBones);
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
				OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
			}
		}

		/// <summary>
		/// データをリロードする
		/// </summary>
		public void ReloadData()
		{
			if (_currentAvatar?.AvatarObject != null)
			{
				// 現在のアバターを保持したまま再ロード
				GameObject currentAvatar = _currentAvatar.AvatarObject;
				AvatarFieldHelper.SetAvatar(currentAvatar);
			}
		}

		/// <summary>
		/// データをクリアする
		/// </summary>
		public void ClearData()
		{
			_currentAvatar = null;
			_physBones.Clear();
			_physBoneColliders.Clear();
            
			OnAvatarChanged?.Invoke(null);
			OnPhysBonesChanged?.Invoke(_physBones);
			OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
		}
		
		/// <summary>
		/// PhysBoneとPhysBoneColliderのコンポーネントを読み込む
		/// </summary>
		public void LoadPhysBoneComponents()
		{
			_physBones.Clear();
			_physBoneColliders.Clear();

			if (_currentAvatar?.Armature == null) return;

			// アーマチュア内のコンポーネントを取得
			var pbComponents = _currentAvatar.Armature.GetComponentsInChildren<VRCPhysBone>(true);
			var pbcComponents = _currentAvatar.Armature.GetComponentsInChildren<VRCPhysBoneCollider>(true);

			_physBones.AddRange(pbComponents);
			_physBoneColliders.AddRange(pbcComponents);
            
			// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
			if (_currentAvatar.AvatarObject.transform.Find("AvatarDynamics") != null)
			{
				var avatarDynamics = _currentAvatar.AvatarObject.transform.Find("AvatarDynamics").gameObject;
                
				// PhysBoneを検索して追加
				if (avatarDynamics.transform.Find("PhysBones") != null)
				{
					var physBonesParent = avatarDynamics.transform.Find("PhysBones");
					var additionalPBs = physBonesParent.GetComponentsInChildren<VRCPhysBone>(true);
					foreach (var pb in additionalPBs)
					{
						if (!_physBones.Contains(pb))
						{
							_physBones.Add(pb);
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
		}
		
		public void InvokeChanged()
		{
			OnPhysBonesChanged?.Invoke(_physBones);
			OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
		}
        #endregion

        #region Private Methods

		/// <summary>
		/// ステータスメッセージの通知
		/// </summary>
		private void NotifyStatusMessage(string message)
		{
			OnStatusMessageChanged?.Invoke(message);
		}
        #endregion
	}
}