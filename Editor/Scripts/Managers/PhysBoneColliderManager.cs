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
			// Result型を使った処理
			var result = ProcessCollidersWithResult();

			// 結果に応じて処理
			return result.Match(
				onSuccess: data =>
				{
					NotifyStatusMessage($"PhysBoneCollider処理完了! 処理数: {data.AffectedCount}");

					// コライダー処理完了イベントを発火（PhysBoneManagerが参照を更新）
					OnCollidersProcessed?.Invoke(this);

					ReloadData();
					NotifyProcessingComplete();
					return true;
				},
				onFailure: error =>
				{
					NotifyStatusMessage($"エラー: {error.Message}");
					if (error.Exception != null)
					{
						Debug.LogError($"PhysBoneCollider置換中にエラーが発生しました: {error.Exception.Message}");
					}
					return false;
				});
		}

		/// <summary>
		/// PhysBoneCollider処理のResult型バージョン
		/// Railway Oriented Programmingによるエラーハンドリング
		/// </summary>
		public Result<CommandResult, ProcessingError> ProcessCollidersWithResult()
		{
			// Step 1: アバター検証
			if (CurrentAvatar == null || CurrentAvatar.AvatarObject == null)
			{
				return Result<CommandResult, ProcessingError>.Failure(
					new ProcessingError("アバターが設定されていません", ErrorType.AvatarNotSet));
			}

			try
			{
				// マッピングをクリア
				Clear();

				// Step 2: 処理対象の取得（Specificationパターンを使用）
				var existingInDynamics = GetAvatarDynamicsComponent<VRCPhysBoneCollider>();
				var notInDynamicsSpec = new ComponentSpecs.NotInCollection<VRCPhysBoneCollider>(existingInDynamics);

				var targetColliders = _components
					.Where(c => notInDynamicsSpec.IsSatisfiedBy(c))
					.ToList();

				if (targetColliders.Count == 0)
				{
					return Result<CommandResult, ProcessingError>.Success(
						new CommandResult
						{
							AffectedCount = 0,
							Message = "処理対象のPhysBoneColliderがありません"
						});
				}

				// Step 3: Undoグループ開始
				Undo.SetCurrentGroupName("PBReplacer - PhysBoneCollider置換");
				int undoGroup = Undo.GetCurrentGroup();

				// Step 4: ルートオブジェクトを準備
				var avatarDynamics = _processor.PrepareRootObject(CurrentAvatar.AvatarObject);

				// Step 5: コライダーを処理
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
					return Result<CommandResult, ProcessingError>.Failure(
						new ProcessingError(result.ErrorMessage, ErrorType.ProcessingFailed));
				}

				return Result<CommandResult, ProcessingError>.Success(
					new CommandResult
					{
						AffectedCount = result.ProcessedComponentCount,
						CreatedObjects = result.CreatedObjects,
						Message = $"PhysBoneCollider処理完了! 処理数: {result.ProcessedComponentCount}"
					});
			}
			catch (Exception ex)
			{
				return Result<CommandResult, ProcessingError>.Failure(
					ProcessingError.FromException(ex, ErrorType.ProcessingFailed));
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
