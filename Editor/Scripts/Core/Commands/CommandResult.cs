using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// コマンド実行結果
	/// </summary>
	public class CommandResult
	{
		/// <summary>処理されたコンポーネント数</summary>
		public int AffectedCount { get; set; }

		/// <summary>生成されたオブジェクトのリスト</summary>
		public List<GameObject> CreatedObjects { get; set; } = new List<GameObject>();

		/// <summary>古いコンポーネント → 新しいコンポーネントのマッピング</summary>
		public Dictionary<int, Component> ComponentMap { get; set; } = new Dictionary<int, Component>();

		/// <summary>追加のメッセージ</summary>
		public string Message { get; set; } = string.Empty;
	}
}
