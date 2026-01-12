using System;
using System.Linq;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PhysBoneCollider処理コマンド
	/// </summary>
	public class ProcessPhysBoneColliderCommand : ICommand
	{
		public string Description => "PhysBoneCollider処理";
		public bool CanUndo => false; // Unity Undoを使用

		/// <summary>
		/// PhysBoneCollider処理を実行
		/// </summary>
		public Result<CommandResult, ProcessingError> Execute()
		{
			var manager = PhysBoneColliderManager.Instance;
			var avatar = manager.CurrentAvatar;

			if (avatar?.AvatarObject == null)
			{
				return Result<CommandResult, ProcessingError>.Failure(
					new ProcessingError("アバターが設定されていません", ErrorType.AvatarNotSet));
			}

			try
			{
				var processor = new ComponentProcessor();

				// 処理対象を取得（Manager経由）
				var existingInDynamics = manager.GetAvatarDynamicsComponent<VRCPhysBoneCollider>();
				var targetPBC = manager.Components
					.Where(c => !existingInDynamics.Contains(c))
					.ToList();

				if (targetPBC.Count == 0)
				{
					return Result<CommandResult, ProcessingError>.Success(
						new CommandResult { AffectedCount = 0, Message = "処理対象のPhysBoneColliderがありません" });
				}

				// Undoグループ開始
				Undo.SetCurrentGroupName("PBReplacer - PhysBoneCollider置換");
				int undoGroup = Undo.GetCurrentGroup();

				// フォルダ準備
				var avatarDynamics = processor.PrepareRootObject(avatar.AvatarObject);
				processor.RevertFolderFromPrefab(avatarDynamics, processor.Settings.PhysBoneCollidersFolder);

				// 空の未使用フォルダを削除
				processor.CleanupUnusedFolders(avatarDynamics,
					processor.Settings.PhysBonesFolder,
					processor.Settings.PhysBoneCollidersFolder);

				// Helper経由で処理
				var result = ComponentProcessingHelper.ProcessPhysBoneColliders(
					processor, avatar.AvatarObject, targetPBC);

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

		/// <summary>
		/// Undo処理（Unity Undoシステムに委譲）
		/// </summary>
		public Result<Unit, ProcessingError> Undo()
		{
			UnityEditor.Undo.PerformUndo();
			return Result<Unit, ProcessingError>.Success(Unit.Value);
		}
	}
}
