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
			return ExecuteWithErrorHandling(() => ProcessConstraintsInternal(), "コンストレイント処理");
		}

		/// <summary>
		/// コンストレイント処理の内部実装
		/// </summary>
		private bool ProcessConstraintsInternal()
		{
			// ルートオブジェクトを準備
			var avatarDynamics = _processor.PrepareRootObject(CurrentAvatar.AvatarObject);

			// フォルダ階層を準備（Prefab復元とクリーンアップを一括処理）
			_processor.PrepareFolderHierarchy(
				avatarDynamics,
				_settings.ConstraintsFolder,
				_settings.PositionConstraintsFolder,
				_settings.RotationConstraintsFolder,
				_settings.ScaleConstraintsFolder,
				_settings.ParentConstraintsFolder,
				_settings.LookAtConstraintsFolder,
				_settings.AimConstraintsFolder);

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
					NotifyStatusError(result.ErrorMessage);
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
					NotifyStatusError(result.ErrorMessage);
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
					NotifyStatusError(result.ErrorMessage);
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
					NotifyStatusError(result.ErrorMessage);
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
					NotifyStatusError(result.ErrorMessage);
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
			OnConstraintsChanged?.Invoke(_components);
		}
		#endregion
		
		#region Private Methods
        #endregion
	}
}
