using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
	public class ConstraintDataManager : ComponentManagerBase<VRCConstraintBase>
	{
		#region Events
		// データの変更を通知するイベント
		public event Action<List<VRCConstraintBase>> OnConstraintsChanged;
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

		// コンポーネントリスト
		//private List<VRCConstraintBase> _components = new List<VRCConstraintBase>();

		public List<VRCConstraintBase> VRCConstraints => _components;

		/**
		// PhysBone処理クラスへの参照
		private PhysBoneProcessor _processor;
		**/
        
		// 設定への参照
		//private PBReplacerSettings _settings;
		
		//private ComponentProcessor _processor;
        #endregion
        
        #region Singleton Implementation
		private static ConstraintDataManager _instance;
		public static ConstraintDataManager Instance => _instance ??= new ConstraintDataManager();

		// プライベートコンストラクタ（シングルトンパターン）
		private ConstraintDataManager() 
		{
			/**
			_settings = PBReplacerSettings.Load();
			_processor = new ComponentProcessor(_settings);
			
			AvatarFieldHelper.OnAvatarChanged += OnAvatarChanged;
			**/
		}
		
		// デストラクタ
		~ConstraintDataManager()
		{
			//AvatarFieldHelper.OnAvatarChanged -= OnAvatarChanged;
		}
        #endregion
        
        #region Public Methods
		/// <summary>
		/// コンストレイントコンポーネントを読み込む
		/// </summary>
		public override void LoadComponents()
		{
			_components.Clear();

			if (CurrentAvatar?.Armature == null) return;

			// アーマチュア内のコンポーネントを取得
			var vrcConstraintComponents = CurrentAvatar.Armature.GetComponentsInChildren<VRCConstraintBase>(true);

			_components.AddRange(vrcConstraintComponents);
            
			// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
			if (CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics") != null)
			{
				var avatarDynamics = CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics").gameObject;
                
				// VRCConstraintを検索して追加
				if (avatarDynamics.transform.Find("Constraints") != null)
				{
					var vrcConstraintParent = avatarDynamics.transform.Find("Constraints");
					var additionalVRCConstraints = vrcConstraintParent.GetComponentsInChildren<VRCConstraintBase>(true);
					foreach (var constraint in additionalVRCConstraints)
					{
						if (!_components.Contains(constraint))
						{
							_components.Add(constraint);
						}
					}
				}
			}
			
			InvokeChanged();
		}
		
		public override bool ProcessComponents()
		{
			return ProcessConstraints();
		}
		
		/// <summary>
		/// コンストレイントコンポーネントを処理する
		/// </summary>
		public bool ProcessConstraints()
		{
			if (CurrentAvatar == null || CurrentAvatar.AvatarObject == null)
			{
				NotifyStatusMessage("アバターが設定されていません");
				return false;
			}

			try
			{
				// 各コンストレイント型のリストを作成
				var positionConstraints = _components.OfType<VRCPositionConstraint>().ToList();
				var rotationConstraints = _components.OfType<VRCRotationConstraint>().ToList();
				var scaleConstraints = _components.OfType<VRCScaleConstraint>().ToList();
				var parentConstraints = _components.OfType<VRCParentConstraint>().ToList();
				var lookAtConstraints = _components.OfType<VRCLookAtConstraint>().ToList();
				var aimConstraints = _components.OfType<VRCAimConstraint>().ToList();
                
				int processedCount = 0;
                
				// 各コンストレイント型を処理
				if (positionConstraints.Count > 0)
				{
					var result = _processor.ProcessConstraints(
						CurrentAvatar.AvatarObject, 
						positionConstraints, 
						_processor.Settings.PositionConstraintsFolder);
                        
					if (!result.Success)
					{
						NotifyStatusMessage($"エラー: {result.ErrorMessage}");
						return false;
					}
                    
					processedCount += result.ProcessedComponentCount;
				}
                
				if (rotationConstraints.Count > 0)
				{
					var result = _processor.ProcessConstraints(
						CurrentAvatar.AvatarObject, 
						rotationConstraints, 
						_processor.Settings.RotationConstraintsFolder);
                        
					if (!result.Success)
					{
						NotifyStatusMessage($"エラー: {result.ErrorMessage}");
						return false;
					}
                    
					processedCount += result.ProcessedComponentCount;
				}
                
				if (scaleConstraints.Count > 0)
				{
					var result = _processor.ProcessConstraints(
						CurrentAvatar.AvatarObject, 
						scaleConstraints, 
						_processor.Settings.ScaleConstraintsFolder);
                        
					if (!result.Success)
					{
						NotifyStatusMessage($"エラー: {result.ErrorMessage}");
						return false;
					}
                    
					processedCount += result.ProcessedComponentCount;
				}
                
				if (parentConstraints.Count > 0)
				{
					var result = _processor.ProcessConstraints(
						CurrentAvatar.AvatarObject, 
						parentConstraints, 
						_processor.Settings.ParentConstraintsFolder);
                        
					if (!result.Success)
					{
						NotifyStatusMessage($"エラー: {result.ErrorMessage}");
						return false;
					}
                    
					processedCount += result.ProcessedComponentCount;
				}
                
				if (lookAtConstraints.Count > 0)
				{
					var result = _processor.ProcessConstraints(
						CurrentAvatar.AvatarObject, 
						lookAtConstraints, 
						_processor.Settings.LookAtConstraintsFolder);
                        
					if (!result.Success)
					{
						NotifyStatusMessage($"エラー: {result.ErrorMessage}");
						return false;
					}
                    
					processedCount += result.ProcessedComponentCount;
				}
                
				if (aimConstraints.Count > 0)
				{
					var result = _processor.ProcessConstraints(
						CurrentAvatar.AvatarObject, 
						aimConstraints, 
						_processor.Settings.AimConstraintsFolder);
                        
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
			OnConstraintsChanged?.Invoke(_components);
		}
        
		/// <summary>
		/// データをリロードする
		/// </summary>
		//public void ReloadData()
		//{
		//	if (CurrentAvatar?.AvatarObject != null)
		//	{
		//		// 現在のアバターを保持したまま再ロード
		//		GameObject currentAvatar = CurrentAvatar.AvatarObject;
		//		AvatarFieldHelper.SetAvatar(currentAvatar);
		//	}
		//}
        
		/// <summary>
		/// データをクリアする
		/// </summary>
		//public void ClearData()
		//{
		//	_components.Clear();
		//	OnConstraintsChanged?.Invoke(_components);
		//}
		
		//public void InvokeChanged()
		//{
		//	OnConstraintsChanged?.Invoke(_components);
		//}
		#endregion
		
		#region Private Methods
		/// <summary>
		/// アバターデータが変更された時の処理
		/// </summary>
		//private void OnAvatarChanged(AvatarData avatarData)
		//{
		//	// アバター変更時にコンポーネントを再ロード
		//	LoadComponents();
		//}
        #endregion
	}
}
