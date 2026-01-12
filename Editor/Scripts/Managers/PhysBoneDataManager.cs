using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
	/// <summary>
	/// PhysBoneの管理を行うクラス
	/// データ保持とイベント通知に特化（処理ロジックはCommandに移動）
	/// </summary>
	public class PhysBoneDataManager : ComponentManagerBase<VRCPhysBone>
	{
		#region Events
		public event Action<List<VRCPhysBone>> OnPhysBonesChanged;
		#endregion

		#region Properties
		public override string FolderName => _settings.PhysBonesFolder;
		#endregion

		#region Singleton Implementation
		private static PhysBoneDataManager _instance;
		public static PhysBoneDataManager Instance => _instance ??= new PhysBoneDataManager();

		private PhysBoneDataManager() : base()
		{
			// EventBus経由でアバター変更を購読
			AddSubscription(EventBus.Subscribe<AvatarChangedEvent>(OnAvatarChangedEvent));
		}

		/// <summary>
		/// EventBus経由のアバター変更イベントハンドラ
		/// </summary>
		private void OnAvatarChangedEvent(AvatarChangedEvent e)
		{
			// 既にOnAvatarDataChangedで処理されるため、追加処理が必要な場合のみ実装
		}
		#endregion

		#region Public Methods
		public override void LoadComponents()
		{
			base.LoadComponents();
			InvokeChanged();
		}

		public override bool ProcessComponents()
		{
			// 処理はProcessPhysBoneCommandに移動したため、ここでは何もしない
			// この呼び出しはManagersクラスからの互換性のために残す
			return true;
		}

		public override void InvokeChanged()
		{
			base.InvokeChanged();
			OnPhysBonesChanged?.Invoke(_components);
		}

		public override void Cleanup()
		{
			base.Cleanup();
			OnPhysBonesChanged = null;
		}
		#endregion
	}
}
