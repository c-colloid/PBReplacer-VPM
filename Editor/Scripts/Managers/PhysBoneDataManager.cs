using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
	/// <summary>
	/// PhysBoneの管理を行うクラス
	/// PhysBoneColliderManagerと連携して参照を更新する
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
			// PhysBoneColliderManagerの処理完了イベントを購読
			PhysBoneColliderManager.Instance.OnCollidersProcessed += OnCollidersProcessed;
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
			return ProcessPhysBones();
		}

		/// <summary>
		/// PhysBoneを再配置する処理を実行
		/// </summary>
		/// <returns>成功した場合はtrue</returns>
		public bool ProcessPhysBones()
		{
			if (CurrentAvatar == null || CurrentAvatar.AvatarObject == null)
			{
				NotifyStatusMessage("アバターが設定されていません");
				return false;
			}

			try
			{
				var targetPB = _components
					.Where(c => !GetAvatarDynamicsComponent<VRCPhysBone>().Contains(c))
					.ToList();

				if (targetPB.Count == 0)
				{
					NotifyStatusMessage("処理対象のPhysBoneがありません");
					return true;
				}

				// Undoグループ開始
				Undo.SetCurrentGroupName("PBReplacer - PhysBone置換");
				int undoGroup = Undo.GetCurrentGroup();

				// ルートオブジェクトを準備
				var avatarDynamics = _processor.PrepareRootObject(CurrentAvatar.AvatarObject);

				// PhysBoneを処理
				var result = _processor.ProcessComponents<VRCPhysBone>(
					CurrentAvatar.AvatarObject,
					targetPB,
					FolderName,
					(oldPB, newPB, newObj, res) =>
					{
						// rootTransformの設定
						if (oldPB.rootTransform == null)
						{
							newPB.rootTransform = oldPB.transform;
						}
						else
						{
							newObj.name = _processor.GetSafeObjectName(newPB.rootTransform.name);
						}
					});

				// Undoグループ終了
				Undo.CollapseUndoOperations(undoGroup);

				if (!result.Success)
				{
					NotifyStatusMessage($"エラー: {result.ErrorMessage}");
					return false;
				}

				// 処理結果を通知
				string message = $"PhysBone処理完了! 処理数: {result.ProcessedComponentCount}";
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
		/// PhysBoneCollider処理完了時のコールバック
		/// PhysBoneのCollider参照を更新する
		/// </summary>
		private void OnCollidersProcessed(IReferenceResolver<VRCPhysBoneCollider> resolver)
		{
			if (!resolver.HasMappings) return;

			foreach (var pb in _components)
			{
				if (pb == null || pb.colliders == null) continue;

				for (int i = 0; i < pb.colliders.Count; i++)
				{
					var oldCollider = pb.colliders[i] as VRCPhysBoneCollider;
					if (oldCollider == null) continue;

					var newCollider = resolver.Resolve(oldCollider);
					if (newCollider != null)
					{
						pb.colliders[i] = newCollider;
					}
				}
			}
		}

		public override void InvokeChanged()
		{
			base.InvokeChanged();
			OnPhysBonesChanged?.Invoke(_components);
		}

		public override void Cleanup()
		{
			base.Cleanup();
			PhysBoneColliderManager.Instance.OnCollidersProcessed -= OnCollidersProcessed;
			OnPhysBonesChanged = null;
		}
		#endregion
	}
}