using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// 処理の最終フェーズ: 参照解決と旧コンポーネント削除
	/// 全コンポーネント処理の最後に実行される
	/// </summary>
	public class FinalizeCommand : ICommand
	{
		public string Description => "参照解決と旧コンポーネント削除";
		public bool CanUndo => false;

		/// <summary>
		/// 参照解決と削除を実行
		/// </summary>
		public Result<CommandResult, ProcessingError> Execute()
		{
			try
			{
				// 1. PhysBoneのcolliders参照を解決
				ResolvePhysBoneColliderReferences();

				// 2. 全旧コンポーネントを削除
				ProcessingContext.Instance.DeleteAllPending();

				return Result<CommandResult, ProcessingError>.Success(new CommandResult());
			}
			catch (System.Exception ex)
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
			return Result<Unit, ProcessingError>.Success(Unit.Value);
		}

		/// <summary>
		/// PhysBoneのcolliders参照を解決
		/// 新しく作成されたPhysBoneのcolliders参照を、新しいPhysBoneColliderに置き換える
		/// </summary>
		private void ResolvePhysBoneColliderReferences()
		{
			var resolver = PhysBoneColliderManager.Instance;
			if (!resolver.HasMappings) return;

			// 新しく作成されたPhysBoneのcolliders参照を更新
			var newPhysBones = PhysBoneDataManager.Instance.GetAvatarDynamicsComponent<VRCPhysBone>();

			foreach (var pb in newPhysBones)
			{
				if (pb?.colliders == null) continue;

				// 変更が必要かチェック
				bool willModify = false;
				for (int i = 0; i < pb.colliders.Count; i++)
				{
					var oldCollider = pb.colliders[i] as VRCPhysBoneCollider;
					if (oldCollider != null && resolver.Resolve(oldCollider) != null)
					{
						willModify = true;
						break;
					}
				}

				if (!willModify) continue;

				// Undo登録（変更前に呼ぶ）
				Undo.RecordObject(pb, "Resolve PhysBone Collider References");

				// colliders参照を更新
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

				EditorUtility.SetDirty(pb);
			}
		}
	}
}
