using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
	public class ConstraintDataManager
	{
		#region Events
		// データの変更を通知するイベント
		public event Action<List<VRCConstraintBase>> OnConstraintsChanged;
		public event Action OnProcessingComplete;
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
		private List<VRCConstraintBase> _vrcConstraints = new List<VRCConstraintBase>();

		public List<VRCConstraintBase> VRCConstraints => _vrcConstraints;

		/**
		// PhysBone処理クラスへの参照
		private PhysBoneProcessor _processor;
        
		// 設定への参照
		private PBReplacerSettings _settings;
		**/
        #endregion
        
        #region Singleton Implementation
		private static ConstraintDataManager _instance;
		public static ConstraintDataManager Instance => _instance ??= new ConstraintDataManager();

		// プライベートコンストラクタ（シングルトンパターン）
		private ConstraintDataManager() 
		{
			/**
			_settings = PBReplacerSettings.Load();
			_processor = new PhysBoneProcessor(_settings);
			**/
		}
        #endregion

        
		/// <summary>
		/// PhysBoneとPhysBoneColliderのコンポーネントを読み込む
		/// </summary>
		public void LoadPhysBoneComponents()
		{
			_vrcConstraints.Clear();

			if (_currentAvatar?.Armature == null) return;

			// アーマチュア内のコンポーネントを取得
			var vrcConstraintComponents = _currentAvatar.Armature.GetComponentsInChildren<VRCConstraintBase>(true);

			_vrcConstraints.AddRange(vrcConstraintComponents);
            
			// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
			if (_currentAvatar.AvatarObject.transform.Find("AvatarDynamics") != null)
			{
				var avatarDynamics = _currentAvatar.AvatarObject.transform.Find("AvatarDynamics").gameObject;
                
				// VRCConstraintを検索して追加
				if (avatarDynamics.transform.Find("Constraints") != null)
				{
					var vrcConstraintParent = avatarDynamics.transform.Find("Constraints");
					var additionalVRCConstraints = vrcConstraintParent.GetComponentsInChildren<VRCConstraintBase>(true);
					foreach (var constraint in additionalVRCConstraints)
					{
						if (!_vrcConstraints.Contains(constraint))
						{
							_vrcConstraints.Add(constraint);
						}
					}
				}
			}
		}
		
		public void InvokeChanged()
		{
			OnConstraintsChanged?.Invoke(_vrcConstraints);
		}
	}
}
