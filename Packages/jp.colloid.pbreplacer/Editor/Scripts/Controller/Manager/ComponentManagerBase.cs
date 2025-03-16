﻿using System;
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
		
		public virtual string FolderName => "AvatarDinamics";
    
		// コンストラクタ
		protected ComponentManagerBase()
		{
			_settings = PBReplacerSettings.Load();
			_processor = new ComponentProcessor(_settings);
			PBReplacerSettings.OnSettingsChanged += OnSettingsChanged;
			AvatarFieldHelper.OnAvatarChanged += OnAvatarDataChanged;
		}
		
		// デコンストラクタ
		~ComponentManagerBase()
		{
			Cleanup();
		}
		
		protected void OnSettingsChanged()
		{
			_settings = PBReplacerSettings.GetLatestSettings();
			_processor = new ComponentProcessor(_settings);
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
		
		public virtual void LoadComponents()
		{
			_components.Clear();

			if (CurrentAvatar?.Armature == null)
			{
				InvokeChanged();
				return;
			}

			// アーマチュア内のコンポーネントを取得
			var targetComponents = CurrentAvatar.Armature.GetComponentsInChildren<T>(true);

			_components.AddRange(targetComponents);
            
			_components.AddRange(GetAvatarDynamicsComponent<T>());
			
			InvokeChanged();
		}
		
		// 抽象メソッド
		public abstract bool ProcessComponents();
    
		// ヘルパーメソッド
		protected List<TComponent> GetAvatarDynamicsComponent<TComponent>() where TComponent : Component
		{
			var result = new List<TComponent>();
			
			// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
			if (CurrentAvatar?.AvatarObject == null) return result;
    
			var avatarDynamicsTransform = CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics");
			if (avatarDynamicsTransform == null) return result;
    
			var avatarDynamics = avatarDynamicsTransform.gameObject;
    
			// コンポーネントを検索
			var componentsParentFolder = avatarDynamics.transform.Find(FolderName);
			if (componentsParentFolder == null) return result;
    
			// 該当するコンポーネントをすべて収集
			result.AddRange(componentsParentFolder.GetComponentsInChildren<TComponent>(true));
    
			return result;
		}
		
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
			PBReplacerSettings.OnSettingsChanged -= OnSettingsChanged;
        
			// データをクリア
			_components.Clear();
			OnComponentsChanged = null;
			OnProcessingComplete = null;
			OnStatusMessageChanged = null;
		}
	}
}