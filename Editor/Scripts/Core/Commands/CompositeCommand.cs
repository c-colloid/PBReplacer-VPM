using System.Collections.Generic;

namespace colloid.PBReplacer
{
	/// <summary>
	/// 複数のコマンドをまとめて実行するコマンド（マクロ）
	///
	/// 【使用例】
	/// var macro = new CompositeCommand("PhysBone一括処理");
	/// macro.Add(new ProcessPhysBoneCommand(...));
	/// macro.Add(new ProcessColliderCommand(...));
	/// macro.Execute();
	/// </summary>
	public class CompositeCommand : ICommand
	{
		private readonly List<ICommand> _commands = new List<ICommand>();
		private readonly Stack<ICommand> _executedCommands = new Stack<ICommand>();

		public string Description { get; }
		public bool CanUndo => _executedCommands.Count > 0;

		public CompositeCommand(string description)
		{
			Description = description;
		}

		/// <summary>
		/// コマンドを追加
		/// </summary>
		public void Add(ICommand command)
		{
			_commands.Add(command);
		}

		/// <summary>
		/// すべてのコマンドを実行
		/// </summary>
		public Result<CommandResult, ProcessingError> Execute()
		{
			var aggregatedResult = new CommandResult();

			foreach (var command in _commands)
			{
				var result = command.Execute();

				if (result.IsFailure)
				{
					// 失敗したら、実行済みのコマンドをUndoしてロールバック
					Undo();
					return result;
				}

				_executedCommands.Push(command);

				// 結果を集約
				result.OnSuccess(r =>
				{
					aggregatedResult.AffectedCount += r.AffectedCount;
					aggregatedResult.CreatedObjects.AddRange(r.CreatedObjects);
					foreach (var kvp in r.ComponentMap)
					{
						aggregatedResult.ComponentMap[kvp.Key] = kvp.Value;
					}
				});
			}

			return Result<CommandResult, ProcessingError>.Success(aggregatedResult);
		}

		/// <summary>
		/// すべての実行済みコマンドを取り消し
		/// </summary>
		public Result<Unit, ProcessingError> Undo()
		{
			while (_executedCommands.Count > 0)
			{
				var command = _executedCommands.Pop();
				var result = command.Undo();
				if (result.IsFailure)
				{
					return result;
				}
			}
			return Result<Unit, ProcessingError>.Success(Unit.Value);
		}

		/// <summary>
		/// 追加されたコマンドをクリア
		/// </summary>
		public void Clear()
		{
			_commands.Clear();
			_executedCommands.Clear();
		}
	}
}
