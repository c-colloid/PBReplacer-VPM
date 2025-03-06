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
		
		public virtual string FolderName => "AvatarDinamics";
    
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
		
		public virtual void LoadComponents()
		{
			_components.Clear();

			if (CurrentAvatar?.Armature == null) return;

			// アーマチュア内のコンポーネントを取得
			var vrcConstraintComponents = CurrentAvatar.Armature.GetComponentsInChildren<T>(true);

			_components.AddRange(vrcConstraintComponents);
            
			// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
			if (CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics") != null)
			{
				var avatarDynamics = CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics").gameObject;
                
				// VRCConstraintを検索して追加
				if (avatarDynamics.transform.Find(FolderName) != null)
				{
					var vrcConstraintParent = avatarDynamics.transform.Find(FolderName);
					var additionalVRCConstraints = vrcConstraintParent.GetComponentsInChildren<T>(true);
					foreach (var constraint in additionalVRCConstraints)
					{
						if (!_components.Contains(constraint))
						{
							_components.Add(constraint);
						}
					}
				}
			}
			
			InvokeChanged();
		}
		
		// 抽象メソッド
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