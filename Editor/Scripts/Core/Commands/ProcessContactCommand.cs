using System;
using UnityEditor;

namespace colloid.PBReplacer
{
	/// <summary>
	/// Contact処理コマンド
	/// </summary>
	public class ProcessContactCommand : ICommand
	{
		private readonly ContactDataManager _manager;

		public string Description => "Contact処理";
		public bool CanUndo => false;

		public ProcessContactCommand(ContactDataManager manager = null)
		{
			_manager = manager ?? ContactDataManager.Instance;
		}

		/// <summary>
		/// Contact処理を実行
		/// </summary>
		public Result<CommandResult, ProcessingError> Execute()
		{
			try
			{
				bool success = _manager.ProcessComponents();

				if (success)
				{
					return Result<CommandResult, ProcessingError>.Success(
						new CommandResult
						{
							AffectedCount = _manager.Components.Count,
							Message = "Contact処理完了"
						});
				}
				else
				{
					return Result<CommandResult, ProcessingError>.Failure(
						new ProcessingError("Contact処理に失敗しました", ErrorType.ProcessingFailed));
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
			UnityEditor.Undo.PerformUndo();
			return Result<Unit, ProcessingError>.Success(Unit.Value);
		}
	}
}
