using System;
using System.Collections.Generic;

namespace colloid.PBReplacer
{
	/// <summary>
	/// 型安全なイベントバス
	/// IDisposableパターンで購読解除を簡単に行える
	/// </summary>
	public static class EventBus
	{
		private static readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();
		private static readonly object _lock = new object();

		/// <summary>
		/// イベントを購読する
		/// </summary>
		/// <typeparam name="TEvent">イベントの型</typeparam>
		/// <param name="handler">イベントハンドラ</param>
		/// <returns>購読解除用のIDisposable</returns>
		public static IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
		{
			var eventType = typeof(TEvent);

			lock (_lock)
			{
				if (!_handlers.ContainsKey(eventType))
				{
					_handlers[eventType] = new List<Delegate>();
				}
				_handlers[eventType].Add(handler);
			}

			return new Subscription<TEvent>(handler);
		}

		/// <summary>
		/// イベントを発行する
		/// </summary>
		/// <typeparam name="TEvent">イベントの型</typeparam>
		/// <param name="eventData">イベントデータ</param>
		public static void Publish<TEvent>(TEvent eventData) where TEvent : IEvent
		{
			var eventType = typeof(TEvent);

			List<Delegate> handlersCopy;
			lock (_lock)
			{
				if (!_handlers.ContainsKey(eventType))
					return;

				handlersCopy = new List<Delegate>(_handlers[eventType]);
			}

			foreach (var handler in handlersCopy)
			{
				try
				{
					((Action<TEvent>)handler)(eventData);
				}
				catch (Exception ex)
				{
					UnityEngine.Debug.LogError($"EventBus: ハンドラ実行中にエラー: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// 購読を解除する（内部使用）
		/// </summary>
		internal static void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
		{
			var eventType = typeof(TEvent);

			lock (_lock)
			{
				if (_handlers.ContainsKey(eventType))
				{
					_handlers[eventType].Remove(handler);
				}
			}
		}

		/// <summary>
		/// すべての購読を解除する
		/// </summary>
		public static void Clear()
		{
			lock (_lock)
			{
				_handlers.Clear();
			}
		}

		/// <summary>
		/// 特定のイベント型の購読をすべて解除する
		/// </summary>
		public static void Clear<TEvent>() where TEvent : IEvent
		{
			var eventType = typeof(TEvent);

			lock (_lock)
			{
				if (_handlers.ContainsKey(eventType))
				{
					_handlers[eventType].Clear();
				}
			}
		}

		/// <summary>
		/// 購読解除用のクラス
		/// </summary>
		private class Subscription<TEvent> : IDisposable where TEvent : IEvent
		{
			private Action<TEvent> _handler;
			private bool _disposed;

			public Subscription(Action<TEvent> handler)
			{
				_handler = handler;
			}

			public void Dispose()
			{
				if (_disposed) return;
				_disposed = true;

				if (_handler != null)
				{
					Unsubscribe(_handler);
					_handler = null;
				}
			}
		}
	}

	/// <summary>
	/// イベントのマーカーインターフェース
	/// </summary>
	public interface IEvent { }

	#region 具体的なイベント定義

	/// <summary>
	/// アバター変更イベント
	/// </summary>
	public struct AvatarChangedEvent : IEvent
	{
		public AvatarData NewAvatar { get; }
		public AvatarData OldAvatar { get; }

		public AvatarChangedEvent(AvatarData newAvatar, AvatarData oldAvatar = null)
		{
			NewAvatar = newAvatar;
			OldAvatar = oldAvatar;
		}
	}

	/// <summary>
	/// コンポーネント処理完了イベント
	/// </summary>
	public struct ProcessingCompletedEvent : IEvent
	{
		public string ManagerName { get; }
		public int ProcessedCount { get; }
		public bool Success { get; }
		public string Message { get; }

		public ProcessingCompletedEvent(string managerName, int processedCount, bool success, string message = "")
		{
			ManagerName = managerName;
			ProcessedCount = processedCount;
			Success = success;
			Message = message;
		}
	}

	/// <summary>
	/// 設定変更イベント
	/// </summary>
	public struct SettingsChangedEvent : IEvent
	{
		public PBReplacerSettings Settings { get; }

		public SettingsChangedEvent(PBReplacerSettings settings)
		{
			Settings = settings;
		}
	}

	/// <summary>
	/// ステータス状態変更イベント
	/// </summary>
	public struct StatusStateChangedEvent : IEvent
	{
		public StateMachine.StatusStateContext Context { get; }

		public StatusStateChangedEvent(StateMachine.StatusStateContext context)
		{
			Context = context;
		}
	}

	#endregion
}
