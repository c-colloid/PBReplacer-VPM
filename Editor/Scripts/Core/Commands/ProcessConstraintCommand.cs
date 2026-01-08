using System;
using UnityEditor;

namespace colloid.PBReplacer
{
	/// <summary>
	/// Constraint処理コマンド
	/// </summary>
	public class ProcessConstraintCommand : ICommand
	{
		private readonly ConstraintDataManager _manager;

		public string Description => "Constraint処理";
		public bool CanUndo => false;

		public ProcessConstraintCommand(ConstraintDataManager manager = null)
		{
			_manager = manager ?? ConstraintDataManager.Instance;
		}

		/// <summary>
		/// Constraint処理を実行
		/// </summary>
		public Result<CommandResult, ProcessingError> Execute()
		{
			try
			{
				bool success = _manager.ProcessConstraints();

				if (success)
				{
					return Result<CommandResult, ProcessingError>.Success(
						new CommandResult
						{
							AffectedCount = _manager.Components.Count,
							Message = "Constraint処理完了"
						});
				}
				else
				{
					return Result<CommandResult, ProcessingError>.Failure(
						new ProcessingError("Constraint処理に失敗しました", ErrorType.ProcessingFailed));
				}
			}
			catch (Exception ex)
			{
				return Result<CommandResult, ProcessingError>.Failure(
					ProcessingError.FromException(ex, ErrorType.ProcessingFailed));
			}
		}

		public Result<Unit, ProcessingError> Undo()
		{
			Undo.PerformUndo();
			return Result<Unit, ProcessingError>.Success(Unit.Value);
		}
	}
}
