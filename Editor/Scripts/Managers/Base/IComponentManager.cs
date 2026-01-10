using UnityEngine;
using System.Collections.Generic;

namespace colloid.PBReplacer
{
	/// <summary>
	/// VRCコンポーネント管理の基本インターフェース
	/// </summary>
	/// <typeparam name="T">管理対象のコンポーネント型</typeparam>
	public interface IComponentManager<T> where T : Component
	{
		// コンポーネントリスト取得
		List<T> Components { get; }
    
		// 現在のアバターデータ
		AvatarData CurrentAvatar { get; }
    
		// コンポーネントロード
		void LoadComponents();
    
		// コンポーネント追加
		void AddComponent(T component);
    
		// コンポーネント削除
		void RemoveComponent(T component);
    
		// データのリロード
		void ReloadData();
    
		// データクリア
		void ClearData();
    
		// コンポーネント一括処理
		bool ProcessComponents();
	}
}