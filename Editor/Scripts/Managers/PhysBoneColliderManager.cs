using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PhysBoneColliderの管理を行うクラス
	/// IReferenceResolverを実装し、PhysBoneからの参照解決をサポート
	/// データ保持とイベント通知に特化（処理ロジックはCommandに移動）
	/// </summary>
	public class PhysBoneColliderManager : ComponentManagerBase<VRCPhysBoneCollider>, IReferenceResolver<VRCPhysBoneCollider>
	{
		#region Events
		public event Action<List<VRCPhysBoneCollider>> OnCollidersChanged;
		#endregion

		#region Reference Resolver
		private Dictionary<int, VRCPhysBoneCollider> _colliderMap = new Dictionary<int, VRCPhysBoneCollider>();

		public VRCPhysBoneCollider Resolve(VRCPhysBoneCollider oldComponent)
		{
			if (oldComponent == null) return null;
			return _colliderMap.TryGetValue(oldComponent.GetInstanceID(), out var newComponent)
				? newComponent
				: null;
		}

		public void Register(VRCPhysBoneCollider oldComponent, VRCPhysBoneCollider newComponent)
		{
			if (oldComponent != null && newComponent != null)
			{
				_colliderMap[oldComponent.GetInstanceID()] = newComponent;
			}
		}

		public void Clear()
		{
			_colliderMap.Clear();
		}

		public bool HasMappings => _colliderMap.Count > 0;
		#endregion

		#region Singleton Implementation
		private static PhysBoneColliderManager _instance;
		public static PhysBoneColliderManager Instance => _instance ??= new PhysBoneColliderManager();

		private PhysBoneColliderManager() : base()
		{
		}
		#endregion

		#region Properties
		public override string FolderName => _settings.PhysBoneCollidersFolder;
		#endregion

		#region Public Methods
		public override void LoadComponents()
		{
			base.LoadComponents();
			InvokeChanged();
		}

		public override bool ProcessComponents()
		{
			// 処理はProcessPhysBoneColliderCommandに移動したため、ここでは何もしない
			// この呼び出しはManagersクラスからの互換性のために残す
			return true;
		}

		public override void InvokeChanged()
		{
			base.InvokeChanged();
			OnCollidersChanged?.Invoke(_components);
		}

		public override void Cleanup()
		{
			base.Cleanup();
			Clear();
			OnCollidersChanged = null;
		}
		#endregion
	}
}
