using System;
using System.Collections.Generic;
using UnityEngine;
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
		private readonly PhysBoneDataManager _manager;

		public string Description => "PhysBone処理";
		public bool CanUndo => false; // Unity Undoを使用するため独自Undoは不要

		public ProcessPhysBoneCommand(PhysBoneDataManager manager = null)
		{
			_manager = manager ?? PhysBoneDataManager.Instance;
		}

		/// <summary>
		/// PhysBone処理を実行
		/// </summary>
		public Result<CommandResult, ProcessingError> Execute()
		{
			// マネージャーのResult型メソッドを呼び出し
			return _manager.ProcessPhysBonesWithResult();
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
