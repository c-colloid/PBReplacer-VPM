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
        #endregion
        
        #region Properties

		// コンポーネントリスト
		public List<VRCConstraintBase> VRCConstraints => _components;
		
		public override string FolderName => "Constraints";
        #endregion
        
        #region Singleton Implementation
		private static ConstraintDataManager _instance;
		public static ConstraintDataManager Instance => _instance ??= new ConstraintDataManager();

		// プライベートコンストラクタ（シングルトンパターン）
		private ConstraintDataManager() 
		{
			
		}
		
		// デストラクタ
		~ConstraintDataManager()
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
				var positionConstraints = _components.OfType<VRCPositionConstraint>().Where(c => !GetAvatarDynamicsComponent<VRCPositionConstraint>().Contains(c)).ToList();
				var rotationConstraints = _components.OfType<VRCRotationConstraint>().Where(c => !GetAvatarDynamicsComponent<VRCRotationConstraint>().Contains(c)).ToList();
				var scaleConstraints = _components.OfType<VRCScaleConstraint>().Where(c => !GetAvatarDynamicsComponent<VRCScaleConstraint>().Contains(c)).ToList();
				var parentConstraints = _components.OfType<VRCParentConstraint>().Where(c => !GetAvatarDynamicsComponent<VRCParentConstraint>().Contains(c)).ToList();
				var lookAtConstraints = _components.OfType<VRCLookAtConstraint>().Where(c => !GetAvatarDynamicsComponent<VRCLookAtConstraint>().Contains(c)).ToList();
				var aimConstraints = _components.OfType<VRCAimConstraint>().Where(c => !GetAvatarDynamicsComponent<VRCAimConstraint>().Contains(c)).ToList();
                
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
		#endregion
		
		#region Private Methods
        #endregion
	}
}
