using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PhysBoneColliderの管理を行うクラス
	/// IReferenceResolverを実装し、PhysBoneからの参照解決をサポート
	/// </summary>
	public class PhysBoneColliderManager : ComponentManagerBase<VRCPhysBoneCollider>, IReferenceResolver<VRCPhysBoneCollider>
	{
		#region Events
		public event Action<List<VRCPhysBoneCollider>> OnCollidersChanged;

		/// <summary>
		/// コライダー処理完了時に発火（PhysBoneManagerが参照を更新するタイミング）
		/// </summary>
		public event Action<IReferenceResolver<VRCPhysBoneCollider>> OnCollidersProcessed;
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
			return ProcessColliders();
		}

		/// <summary>
		/// PhysBoneColliderを再配置する処理を実行
		/// </summary>
		/// <returns>成功した場合はtrue</returns>
		public bool ProcessColliders()
		{
			if (CurrentAvatar == null || CurrentAvatar.AvatarObject == null)
			{
				NotifyStatusMessage("アバターが設定されていません");
				return false;
			}

			try
			{
				// マッピングをクリア
				Clear();

				var targetColliders = _components
					.Where(c => !GetAvatarDynamicsComponent<VRCPhysBoneCollider>().Contains(c))
					.ToList();

				if (targetColliders.Count == 0)
				{
					NotifyStatusMessage("処理対象のPhysBoneColliderがありません");
					return true;
				}

				// Undoグループ開始
				Undo.SetCurrentGroupName("PBReplacer - PhysBoneCollider置換");
				int undoGroup = Undo.GetCurrentGroup();

				// ルートオブジェクトを準備
				var avatarDynamics = _processor.PrepareRootObject(CurrentAvatar.AvatarObject);

				// コライダーを処理
				var result = _processor.ProcessComponents<VRCPhysBoneCollider>(
					CurrentAvatar.AvatarObject,
					targetColliders,
					FolderName,
					(oldCollider, newCollider, newObj, res) =>
					{
						// rootTransformの設定
						if (oldCollider.rootTransform == null)
						{
							newCollider.rootTransform = oldCollider.transform;
						}
						else
						{
							newObj.name = _processor.GetSafeObjectName(newCollider.rootTransform.name);
						}

						// マッピングに登録
						Register(oldCollider, newCollider);
					});

				// Undoグループ終了
				Undo.CollapseUndoOperations(undoGroup);

				if (!result.Success)
				{
					NotifyStatusMessage($"エラー: {result.ErrorMessage}");
					return false;
				}

				// 処理結果を通知
				string message = $"PhysBoneCollider処理完了! 処理数: {result.ProcessedComponentCount}";
				NotifyStatusMessage(message);

				// コライダー処理完了イベントを発火（PhysBoneManagerが参照を更新）
				OnCollidersProcessed?.Invoke(this);

				// データを再読み込み
				ReloadData();

				// 処理完了通知
				NotifyProcessingComplete();

				return true;
			}
			catch (Exception ex)
			{
				Debug.LogError($"PhysBoneCollider置換中にエラーが発生しました: {ex.Message}");
				NotifyStatusMessage($"エラー: {ex.Message}");
				return false;
			}
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
			OnCollidersProcessed = null;
		}
		#endregion
	}
}
