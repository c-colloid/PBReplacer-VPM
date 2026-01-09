using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace colloid.PBReplacer
{
	/// <summary>
	/// VRCコンポーネント管理の抽象基底クラス
	/// EventBusとの統合によりイベント管理を改善
	/// IAvatarContext経由で依存関係を注入
	/// </summary>
	public abstract class ComponentManagerBase<T> : IComponentManager<T> where T : Component
	{
		// 従来のイベント（後方互換性のため維持）
		public event Action<List<T>> OnComponentsChanged;
		public event Action OnProcessingComplete;

		// EventBus購読管理
		protected List<IDisposable> _subscriptions = new List<IDisposable>();

		// コンポーネントリスト
		protected List<T> _components = new List<T>();

		// アバターコンテキスト（IAvatarContext経由でアクセス）
		protected IAvatarContext _context;

		// 設定（コンテキスト経由でアクセス）
		protected PBReplacerSettings _settings => _context.Settings;

		// 処理機能への参照（コンテキスト経由でアクセス）
		protected ComponentProcessor _processor => _context.Processor;

		// プロパティ実装
		public List<T> Components => _components;

		// 現在のアバター（コンテキスト経由でアクセス）
		public AvatarData CurrentAvatar => _context.CurrentAvatar;
		
		public virtual string FolderName => "AvatarDinamics";
    
		// コンストラクタ（IAvatarContext注入）
		protected ComponentManagerBase() : this(AvatarContext.Instance)
		{
		}

		// コンストラクタ（テスト用：カスタムコンテキスト注入）
		protected ComponentManagerBase(IAvatarContext context)
		{
			_context = context ?? AvatarContext.Instance;
			AvatarFieldHelper.OnAvatarChanged += OnAvatarDataChanged;
			DataManagerHelper.OnComponentsRemoved += RemoveComponent;
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
		
		private void RemoveComponent(Component component)
		{
			// コンポーネントの型をチェックして、適切な型であれば削除
			if (component is T typedComponent)
			{
				RemoveComponent(typedComponent);
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
			
			IEnumerable<T> targetComponents = _settings.FindComponent switch
			{
				FindComponent.InArmature => CurrentAvatar.Armature.GetComponentsInChildren<T>(true),
				
				FindComponent.InPrefab => PrefabUtility.IsPartOfAnyPrefab(CurrentAvatar.AvatarObject)
				? CurrentAvatar.AvatarObject.GetComponentsInChildren<T>(true)
				.Where(c => PrefabUtility.GetNearestPrefabInstanceRoot(c) == PrefabUtility.GetNearestPrefabInstanceRoot(CurrentAvatar.AvatarObject))
				: CurrentAvatar.Armature.GetComponentsInChildren<T>(true),
				
				FindComponent.AllChilds => CurrentAvatar.AvatarObject.GetComponentsInChildren<T>(true),
				
				_ => null
			};
			
			targetComponents ??= CurrentAvatar.Armature.GetComponentsInChildren<T>(true); 

			_components.AddRange(targetComponents);
            
			_components.AddRange(GetAvatarDynamicsComponent<T>());
			
			InvokeChanged();
		}
		
		// 抽象メソッド
		public abstract bool ProcessComponents();
    
		// ヘルパーメソッド
		public List<TComponent> GetAvatarDynamicsComponent<TComponent>() where TComponent : Component
		{
			var result = new List<TComponent>();
			
			// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
			if (CurrentAvatar?.AvatarObject == null) return result;
    
			var avatarDynamicsTransform = CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics");
			if (avatarDynamicsTransform == null) return result;
    
			// 該当するコンポーネントをすべて収集
			result.AddRange(avatarDynamicsTransform.GetComponentsInChildren<TComponent>(true));

			return result;
		}
		
		protected virtual void NotifyComponentsChanged()
		{
			OnComponentsChanged?.Invoke(_components);
		}
    
		protected void NotifyStatusMessage(string message)
		{
			// StatusMessageManager経由で通知（優先度: Info）
			StatusMessageManager.Info(message);
		}

		protected void NotifyStatusError(string message)
		{
			// StatusMessageManager経由のみで通知（優先度: Error）
			StatusMessageManager.Error(message);
		}

		protected void NotifyStatusSuccess(string message)
		{
			// StatusMessageManager経由のみで通知（優先度: Success）
			StatusMessageManager.Success(message);
		}

		/// <summary>
		/// エラーハンドリングを含むテンプレートメソッド
		/// アバターnullチェック、try-catch、結果通知を共通化
		/// </summary>
		/// <param name="action">実行するアクション（成功時true、失敗時false）</param>
		/// <param name="operationName">操作名（エラーメッセージ用）</param>
		/// <returns>処理が成功した場合はtrue</returns>
		protected bool ExecuteWithErrorHandling(Func<bool> action, string operationName)
		{
			// アバターnullチェック
			if (CurrentAvatar == null || CurrentAvatar.AvatarObject == null)
			{
				NotifyStatusMessage("アバターが設定されていません");
				return false;
			}

			// 処理開始時に優先度をリセット
			StatusMessageManager.ResetPriority();

			try
			{
				var result = action();
				if (result)
				{
					ReloadData();
					NotifyProcessingComplete();
				}
				return result;
			}
			catch (Exception ex)
			{
				Debug.LogError($"{operationName}中にエラーが発生しました: {ex.Message}");
				NotifyStatusError(ex.Message);
				return false;
			}
		}

		protected void NotifyProcessingComplete()
		{
			OnProcessingComplete?.Invoke();
			// EventBus経由でも処理完了を通知
			EventBus.Publish(new ProcessingCompletedEvent(
				GetType().Name,
				_components.Count,
				true));
		}
		
		// クリーンアップ（シャットダウン時）
		public virtual void Cleanup()
		{
			// イベント購読解除
			AvatarFieldHelper.OnAvatarChanged -= OnAvatarDataChanged;
			DataManagerHelper.OnComponentsRemoved -= RemoveComponent;

			// EventBus購読解除
			foreach (var subscription in _subscriptions)
			{
				subscription?.Dispose();
			}
			_subscriptions.Clear();

			// データをクリア
			_components.Clear();
			OnComponentsChanged = null;
			OnProcessingComplete = null;
		}

		/// <summary>
		/// EventBusに購読を追加（自動解除のため）
		/// </summary>
		protected void AddSubscription(IDisposable subscription)
		{
			_subscriptions.Add(subscription);
		}
	}
}