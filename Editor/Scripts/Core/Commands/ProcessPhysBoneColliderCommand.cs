using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PhysBoneCollider処理コマンド
	/// </summary>
	public class ProcessPhysBoneColliderCommand : ICommand
	{
		private readonly PhysBoneColliderManager _manager;

		public string Description => "PhysBoneCollider処理";
		public bool CanUndo => false; // Unity Undoを使用

		public ProcessPhysBoneColliderCommand(PhysBoneColliderManager manager = null)
		{
			_manager = manager ?? PhysBoneColliderManager.Instance;
		}

		/// <summary>
		/// PhysBoneCollider処理を実行
		/// </summary>
		public Result<CommandResult, ProcessingError> Execute()
		{
			return _manager.ProcessCollidersWithResult();
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
