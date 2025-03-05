using System;
using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// VRCコンポーネント管理の抽象基底クラス
	/// </summary>
	public abstract class ComponentManagerBase<T> : IComponentManager<T> where T : Component
	{
		// イベント
		public event Action<List<T>> OnComponentsChanged;
		public event Action OnProcessingComplete;
		public event Action<string> OnStatusMessageChanged;
    
		// コンポーネントリスト
		protected List<T> _components = new List<T>();
    
		// 設定
		protected PBReplacerSettings _settings;
    
		// 処理機能への参照
		protected ComponentProcessor _processor;
		
		// プロパティ実装
		public List<T> Components => _components;
    
		public AvatarData CurrentAvatar => AvatarFieldHelper.CurrentAvatar;
    
		// コンストラクタ
		protected ComponentManagerBase()
		{
			_settings = PBReplacerSettings.Load();
			_processor = new ComponentProcessor(_settings);
			AvatarFieldHelper.OnAvatarChanged += OnAvatarDataChanged;
		}
		
		// デコンストラクタ
		~ComponentManagerBase()
		{
			Cleanup();
		}
		
		protected virtual void OnAvatarDataChanged(AvatarData avatarData)
		{
			LoadComponents();
		}
    
		// 共通実装メソッド
		public virtual void AddComponent(T component)
		{
			if (component != null && !_components.Contains(component))
			{
				_components.Add(component);
				NotifyComponentsChanged();
			}
		}
    
		public virtual void RemoveComponent(T component)
		{
			if (_components.Contains(component))
			{
				_components.Remove(component);
				NotifyComponentsChanged();
			}
		}
    
		public virtual void ClearData()
		{
			_components.Clear();
        
			NotifyComponentsChanged();
		}
    
		public virtual void ReloadData()
		{
			if (CurrentAvatar?.AvatarObject != null)
			{
				GameObject currentAvatar = CurrentAvatar.AvatarObject;
				AvatarFieldHelper.SetAvatar(currentAvatar);
			}
		}
		
		public virtual void InvokeChanged()
		{
			NotifyComponentsChanged();
		}
		
		// 抽象メソッド
		public abstract void LoadComponents();
		public abstract bool ProcessComponents();
    
		// ヘルパーメソッド
		protected virtual void NotifyComponentsChanged()
		{
			OnComponentsChanged?.Invoke(_components);
		}
    
		protected void NotifyStatusMessage(string message)
		{
			OnStatusMessageChanged?.Invoke(message);
		}
    
		protected void NotifyProcessingComplete()
		{
			OnProcessingComplete?.Invoke();
		}
		
		// クリーンアップ（シャットダウン時）
		public virtual void Cleanup()
		{
			// イベント購読解除
			AvatarFieldHelper.OnAvatarChanged -= OnAvatarDataChanged;
        
			// データをクリア
			_components.Clear();
			OnComponentsChanged = null;
			OnProcessingComplete = null;
			OnStatusMessageChanged = null;
		}
	}
}