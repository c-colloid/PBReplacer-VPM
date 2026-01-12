using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
	/// <summary>
	/// 処理全体を通じて共有されるコンテキスト
	/// 削除待ちコンポーネントの管理を行う
	/// </summary>
	public class ProcessingContext
	{
		#region Singleton
		private static ProcessingContext _instance;
		public static ProcessingContext Instance => _instance ??= new ProcessingContext();
		private ProcessingContext() { }
		#endregion

		/// <summary>
		/// 削除待ちの旧コンポーネントリスト
		/// </summary>
		public List<Component> PendingDeletions { get; } = new List<Component>();

		/// <summary>
		/// 処理開始時に呼び出し（状態をリセット）
		/// </summary>
		public void BeginProcessing()
		{
			PendingDeletions.Clear();
		}

		/// <summary>
		/// 削除待ちコンポーネントを追加
		/// </summary>
		public void AddPendingDeletion(Component component)
		{
			if (component != null)
				PendingDeletions.Add(component);
		}

		/// <summary>
		/// 全削除待ちコンポーネントを削除
		/// </summary>
		public void DeleteAllPending()
		{
			foreach (var component in PendingDeletions)
			{
				if (component != null)
					Undo.DestroyObjectImmediate(component);
			}
			PendingDeletions.Clear();
		}
	}
}
