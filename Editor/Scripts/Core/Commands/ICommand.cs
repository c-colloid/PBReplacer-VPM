using System;

namespace colloid.PBReplacer
{
	/// <summary>
	/// コマンドパターンのインターフェース
	///
	/// 【なぜこのパターンを使うか】
	/// - 処理を「オブジェクト」として扱える
	/// - Undo/Redo が自然に実装できる
	/// - 処理の履歴を記録できる
	/// - 処理の組み合わせ（マクロ）が作れる
	/// </summary>
	public interface ICommand
	{
		/// <summary>コマンドの説明（Undoメニューに表示）</summary>
		string Description { get; }

		/// <summary>コマンドを実行</summary>
		Result<CommandResult, ProcessingError> Execute();

		/// <summary>コマンドを取り消し</summary>
		Result<Unit, ProcessingError> Undo();

		/// <summary>取り消し可能か</summary>
		bool CanUndo { get; }
	}

	/// <summary>
	/// 何も返さないことを表す型（void の代わり）
	/// </summary>
	public readonly struct Unit
	{
		public static readonly Unit Value = new Unit();
	}
}
