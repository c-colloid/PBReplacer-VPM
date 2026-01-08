using UnityEngine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// コンポーネント参照の解決を行うインターフェース
	/// 古いコンポーネントから新しいコンポーネントへのマッピングを提供する
	/// </summary>
	/// <typeparam name="T">解決対象のコンポーネント型</typeparam>
	public interface IReferenceResolver<T> where T : Component
	{
		/// <summary>
		/// 古いコンポーネントに対応する新しいコンポーネントを取得する
		/// </summary>
		/// <param name="oldComponent">旧コンポーネント</param>
		/// <returns>新しいコンポーネント（見つからない場合はnull）</returns>
		T Resolve(T oldComponent);

		/// <summary>
		/// コンポーネントマッピングを登録する
		/// </summary>
		/// <param name="oldComponent">旧コンポーネント</param>
		/// <param name="newComponent">新コンポーネント</param>
		void Register(T oldComponent, T newComponent);

		/// <summary>
		/// マッピングをクリアする
		/// </summary>
		void Clear();

		/// <summary>
		/// マッピングが存在するか確認する
		/// </summary>
		bool HasMappings { get; }
	}
}
