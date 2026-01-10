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
			// PhysBoneColliderManagerの処理完了イベントを購読（従来方式）
			PhysBoneColliderManager.Instance.OnCollidersProcessed += OnCollidersProcessed;

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
			return ProcessPhysBones();
		}

		/// <summary>
		/// PhysBoneを再配置する処理を実行
		/// </summary>
		/// <returns>成功した場合はtrue</returns>
		public bool ProcessPhysBones()
		{
			// Result型を使った処理
			var result = ProcessPhysBonesWithResult();

			// 結果に応じて処理
			return result.Match(
				onSuccess: data =>
				{
					ReloadData();
					NotifyProcessingComplete();
					return true;
				},
				onFailure: error =>
				{
					if (error.Exception != null)
					{
						Debug.LogError($"PhysBone置換中にエラーが発生しました: {error.Exception.Message}");
					}
					return false;
				});
		}

		/// <summary>
		/// PhysBone処理のResult型バージョン
		/// Railway Oriented Programmingによるエラーハンドリング
		/// </summary>
		public Result<CommandResult, ProcessingError> ProcessPhysBonesWithResult()
		{
			// Step 1: アバター検証
			if (CurrentAvatar == null || CurrentAvatar.AvatarObject == null)
			{
				return Result<CommandResult, ProcessingError>.Failure(
					new ProcessingError("アバターが設定されていません", ErrorType.AvatarNotSet));
			}

			try
			{
				// Step 2: 処理対象の取得（Specificationパターンを使用）
				var existingInDynamics = GetAvatarDynamicsComponent<VRCPhysBone>();
				var notInDynamicsSpec = new ComponentSpecs.NotInCollection<VRCPhysBone>(existingInDynamics);

				var targetPB = _components
					.Where(c => notInDynamicsSpec.IsSatisfiedBy(c))
					.ToList();

				if (targetPB.Count == 0)
				{
					return Result<CommandResult, ProcessingError>.Success(
						new CommandResult
						{
							AffectedCount = 0,
							Message = "処理対象のPhysBoneがありません"
						});
				}

				// Step 3: Undoグループ開始
				Undo.SetCurrentGroupName("PBReplacer - PhysBone置換");
				int undoGroup = Undo.GetCurrentGroup();

				// Step 4: ルートオブジェクトを準備
				var avatarDynamics = _processor.PrepareRootObject(CurrentAvatar.AvatarObject);

				// Step 4.5: 既存のAvatarDynamicsの場合、Prefabから削除されたフォルダを復元
				_processor.RevertFolderFromPrefab(avatarDynamics, _settings.PhysBonesFolder);

				// Step 5: PhysBoneを処理
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
					return Result<CommandResult, ProcessingError>.Failure(
						new ProcessingError(result.ErrorMessage, ErrorType.ProcessingFailed));
				}

				return Result<CommandResult, ProcessingError>.Success(
					new CommandResult
					{
						AffectedCount = result.ProcessedComponentCount,
						CreatedObjects = result.CreatedObjects,
						Message = $"PhysBone処理完了! 処理数: {result.ProcessedComponentCount}"
					});
			}
			catch (Exception ex)
			{
				return Result<CommandResult, ProcessingError>.Failure(
					ProcessingError.FromException(ex, ErrorType.ProcessingFailed));
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