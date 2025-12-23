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
	public class PhysBoneDataManager : ComponentManagerBase<Component>
	{
        #region Events
		// データの変更を通知するイベント
		public event Action<List<VRCPhysBone>> OnPhysBonesChanged;
		public event Action<List<VRCPhysBoneCollider>> OnPhysBoneCollidersChanged;
        #endregion

        #region Properties
		private List<VRCPhysBone> _physBones = new List<VRCPhysBone>();
		private List<VRCPhysBoneCollider> _physBoneColliders = new List<VRCPhysBoneCollider>();

		public List<VRCPhysBone> PhysBones => _physBones;
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
        
        #region Validation
		// バリデーション結果を保持するフィールド
		private ValidationResult _lastValidationResult = new ValidationResult();
		public ValidationResult LastValidationResult => _lastValidationResult;

		// バリデーション完了イベント
		public event Action<ValidationResult> OnValidationComplete;

		/// <summary>
		/// PhysBoneとPhysBoneColliderのコンポーネントを検証する
		/// </summary>
		/// <returns>検証結果</returns>
		public ValidationResult ValidateComponents()
		{
			if (CurrentAvatar == null) return new ValidationResult();
    
			var targetPB = _physBones.Where(c => !GetAvatarDynamicsComponent<VRCPhysBone>().Contains(c)).ToList();
			var targetPBC = _physBoneColliders.Where(c => !GetAvatarDynamicsComponent<VRCPhysBoneCollider>().Contains(c)).ToList();
    
			_lastValidationResult = ComponentProcessingHelper.ValidatePhysBones(targetPB, targetPBC);
			OnValidationComplete?.Invoke(_lastValidationResult);
    
			return _lastValidationResult;
		}
		#endregion

        #region Public Methods	
		public override void LoadComponents()
		{
			base.LoadComponents();
			_components = _components.Where(c => c is VRCPhysBone || c is VRCPhysBoneCollider).ToList();

			_physBones.Clear();
			_physBoneColliders.Clear();

			_physBones = _components.Where(c => c is VRCPhysBone)
				.Select(c => c as VRCPhysBone).ToList();
			_physBoneColliders = _components.Where(c => c is VRCPhysBoneCollider)
				.Select(c => c as VRCPhysBoneCollider).ToList();
			
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
				var targetPB = _physBones.Where(c => !GetAvatarDynamicsComponent<VRCPhysBone>().Contains(c)).ToList();
				var targetPBC = _physBoneColliders.Where(c => !GetAvatarDynamicsComponent<VRCPhysBoneCollider>().Contains(c)).ToList();
				
				// PhysBoneの検証を実行
				_lastValidationResult = ComponentProcessingHelper.ValidatePhysBones(targetPB, targetPBC);
				OnValidationComplete?.Invoke(_lastValidationResult);
        
				// 問題がある場合は確認ダイアログを表示
				if (!_lastValidationResult.IsValid)
				{
					bool proceed = EditorUtility.DisplayDialog(
						"PhysBone設定に問題があります",
						_lastValidationResult.GetFormattedMessage(),
						"続行",
						"キャンセル");
            
					if (!proceed)
					{
						NotifyStatusMessage("処理がキャンセルされました");
						return false;
					}
					else
					{
						foreach (var pb in targetPB)
						{
							pb.colliders.Where(c => c == null).ToList()
								.ForEach(c => pb.colliders.Remove(c));
						}
					}
				}
				
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
		//public void LoadPhysBoneComponents()
		//{
		//	_components.Clear();
		//	_physBoneColliders.Clear();

		//	if (CurrentAvatar?.Armature == null) return;

		//	// アーマチュア内のコンポーネントを取得
		//	var pbComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCPhysBone>(true);
		//	var pbcComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCPhysBoneCollider>(true);

		//	_components.AddRange(pbComponents);
		//	_physBoneColliders.AddRange(pbcComponents);
            
		//	// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
		//	if (CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics") != null)
		//	{
		//		var avatarDynamics = CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics").gameObject;
                
		//		// PhysBoneを検索して追加
		//		if (avatarDynamics.transform.Find("PhysBones") != null)
		//		{
		//			var physBonesParent = avatarDynamics.transform.Find("PhysBones");
		//			var additionalPBs = physBonesParent.GetComponentsInChildren<VRCPhysBone>(true);
		//			foreach (var pb in additionalPBs)
		//			{
		//				if (!_components.Contains(pb))
		//				{
		//					_components.Add(pb);
		//				}
		//			}
		//		}
                
		//		// PhysBoneColliderを検索して追加
		//		if (avatarDynamics.transform.Find("PhysBoneColliders") != null)
		//		{
		//			var collidersParent = avatarDynamics.transform.Find("PhysBoneColliders");
		//			var additionalColliders = collidersParent.GetComponentsInChildren<VRCPhysBoneCollider>(true);
		//			foreach (var collider in additionalColliders)
		//			{
		//				if (!_physBoneColliders.Contains(collider))
		//				{
		//					_physBoneColliders.Add(collider);
		//				}
		//			}
		//		}
		//	}
			
		//	InvokeChanged();
		//}
		
		//public override void InvokeChanged()
		//{
		//	base.InvokeChanged();
		//}
		
		protected override void NotifyComponentsChanged()
		{
			base.NotifyComponentsChanged();
        
			// 従来のイベントも発火
			OnPhysBonesChanged?.Invoke(_physBones);
			OnPhysBoneCollidersChanged?.Invoke(_physBoneColliders);
		}
		
		public override void Cleanup()
		{
			base.Cleanup();
			OnPhysBonesChanged = null;
			OnPhysBoneCollidersChanged = null;
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