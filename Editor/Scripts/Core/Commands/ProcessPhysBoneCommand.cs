using System;
using System.Linq;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PhysBone処理コマンド
	///
	/// 【Commandパターンの利点】
	/// - 処理を「オブジェクト」として扱える
	/// - 実行前の状態を保持してUndo可能
	/// - CompositeCommandと組み合わせてマクロが作れる
	/// </summary>
	public class ProcessPhysBoneCommand : ICommand
	{
		public string Description => "PhysBone処理";
		public bool CanUndo => false; // Unity Undoを使用するため独自Undoは不要

		/// <summary>
		/// PhysBone処理を実行
		/// </summary>
		public Result<CommandResult, ProcessingError> Execute()
		{
			var manager = PhysBoneDataManager.Instance;
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
				var existingInDynamics = manager.GetAvatarDynamicsComponent<VRCPhysBone>();
				var targetPB = manager.Components
					.Where(c => !existingInDynamics.Contains(c))
					.ToList();

				if (targetPB.Count == 0)
				{
					return Result<CommandResult, ProcessingError>.Success(
						new CommandResult { AffectedCount = 0, Message = "処理対象のPhysBoneがありません" });
				}

				// Undoグループ開始
				UnityEditor.Undo.SetCurrentGroupName("PBReplacer - PhysBone置換");
				int undoGroup = UnityEditor.Undo.GetCurrentGroup();

				// フォルダ準備
				var avatarDynamics = processor.PrepareRootObject(avatar.AvatarObject);
				processor.RevertFolderFromPrefab(avatarDynamics, processor.Settings.PhysBonesFolder);

				// Helper経由で処理
				var result = ComponentProcessingHelper.ProcessPhysBones(
					processor, avatar.AvatarObject, targetPB);

				// Undoグループ終了
				UnityEditor.Undo.CollapseUndoOperations(undoGroup);

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
		/// Undo処理（Unity Undoシステムに委譲）
		/// </summary>
		public Result<Unit, ProcessingError> Undo()
		{
			// Unity標準のUndoを使用
			UnityEditor.Undo.PerformUndo();
			return Result<Unit, ProcessingError>.Success(Unit.Value);
		}
	}
}
