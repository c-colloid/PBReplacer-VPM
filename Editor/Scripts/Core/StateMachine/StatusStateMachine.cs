using System;
using System.Collections.Generic;

namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// ステータスステートマシンの実装
	/// UI状態と処理フローを統合的に管理する
	/// </summary>
	public class StatusStateMachine : IStatusStateMachine
	{
		private readonly Dictionary<StatusStateType, IState> _states;
		private IState _currentState;
		private StatusStateContext _context;
		private bool _hasAvatar;

		#region IStatusStateMachine プロパティ

		public IState CurrentState => _currentState;
		public StatusStateType CurrentStateType => _currentState?.StateType ?? StatusStateType.None;
		public StatusStateContext Context => _context;

		public event Action<StatusStateContext> OnStateChanged;

		#endregion

		/// <summary>
		/// コンストラクタ
		/// </summary>
		public StatusStateMachine()
		{
			// すべての状態を登録
			_states = new Dictionary<StatusStateType, IState>
			{
				{ StatusStateType.None, new NoneState() },
				{ StatusStateType.Loading, new LoadingState() },
				{ StatusStateType.Idle, new IdleState() },
				{ StatusStateType.Processing, new ProcessingState() },
				{ StatusStateType.Complete, new CompleteState() },
				{ StatusStateType.Warning, new WarningState() },
				{ StatusStateType.Error, new ErrorState() }
			};

			// 初期状態
			_currentState = _states[StatusStateType.None];
			_context = new StatusStateContext(_currentState);
		}

		#region 状態遷移

		public void TransitionTo(StatusStateType stateType)
		{
			if (!_states.TryGetValue(stateType, out var newState)) return;
			if (_currentState == newState) return;

			// 現在の状態から退出
			_currentState?.Exit(this);

			// 新しい状態に遷移
			_currentState = newState;
			_context = new StatusStateContext(_currentState);

			// 新しい状態に入る
			_currentState.Enter(this);

			// イベント発火
			OnStateChanged?.Invoke(_context);
			EventBus.Publish(new StatusStateChangedEvent(_context));
		}

		#endregion

		#region 外部トリガー

		public void SetAvatar(bool hasAvatar)
		{
			_hasAvatar = hasAvatar;
			TransitionTo(hasAvatar ? StatusStateType.Loading : StatusStateType.None);
		}

		public void OnDataLoaded()
		{
			if (CurrentStateType == StatusStateType.Loading)
			{
				TransitionTo(StatusStateType.Idle);
			}
		}

		public void StartProcessing()
		{
			if (CurrentStateType == StatusStateType.Idle)
			{
				TransitionTo(StatusStateType.Processing);
			}
		}

		public void Complete(int processedCount)
		{
			var state = GetState<CompleteState>(StatusStateType.Complete);
			state?.SetProcessedCount(processedCount);
			TransitionTo(StatusStateType.Complete);
		}

		public void Warn(string message)
		{
			var state = GetState<WarningState>(StatusStateType.Warning);
			state?.SetWarningMessage(message);
			TransitionTo(StatusStateType.Warning);
		}

		public void Fail(string errorMessage)
		{
			var state = GetState<ErrorState>(StatusStateType.Error);
			state?.SetErrorMessage(errorMessage);
			TransitionTo(StatusStateType.Error);
		}

		public void UpdateIdleState(IdleStateKind kind)
		{
			var idleState = GetState<IdleState>(StatusStateType.Idle);
			idleState?.SetKind(kind);

			// 現在Idle状態なら、コンテキストを更新してイベント発火
			if (CurrentStateType == StatusStateType.Idle)
			{
				_context = new StatusStateContext(_currentState);
				OnStateChanged?.Invoke(_context);
				EventBus.Publish(new StatusStateChangedEvent(_context));
			}
		}

		public void OnTabChanged(IdleStateKind kind)
		{
			var idleState = GetState<IdleState>(StatusStateType.Idle);
			idleState?.SetKind(kind);

			// Complete/Warning/Error状態からはタイムアウトを待たずに即座にIdleに遷移
			if (CurrentStateType == StatusStateType.Complete ||
				CurrentStateType == StatusStateType.Warning ||
				CurrentStateType == StatusStateType.Error)
			{
				TransitionTo(StatusStateType.Idle);
				return;
			}

			// 現在Idle状態なら、コンテキストを更新してイベント発火
			if (CurrentStateType == StatusStateType.Idle)
			{
				_context = new StatusStateContext(_currentState);
				OnStateChanged?.Invoke(_context);
				EventBus.Publish(new StatusStateChangedEvent(_context));
			}
		}

		public void OnTimeout()
		{
			// タイムアウト時: アバター維持 → Idle（データは既にロード済み）、クリア → None
			if (_hasAvatar)
			{
				TransitionTo(StatusStateType.Idle);
			}
			else
			{
				TransitionTo(StatusStateType.None);
			}
		}

		#endregion

		#region ヘルパーメソッド

		/// <summary>
		/// 指定した型の状態を取得
		/// </summary>
		private T GetState<T>(StatusStateType stateType) where T : class, IState
		{
			if (_states.TryGetValue(stateType, out var state))
			{
				return state as T;
			}
			return null;
		}

		#endregion
	}
}
