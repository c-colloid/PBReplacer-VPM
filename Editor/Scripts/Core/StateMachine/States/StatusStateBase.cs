using UnityEditor;

namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// 状態の基底クラス
	/// 共通のタイムアウト処理などを提供する
	/// </summary>
	public abstract class StatusStateBase : IState
	{
		/// <summary>
		/// 状態タイプ
		/// </summary>
		public abstract StatusStateType StateType { get; }

		/// <summary>
		/// 表示メッセージ
		/// </summary>
		public abstract string Message { get; }

		/// <summary>
		/// 状態に入った時の処理
		/// </summary>
		public virtual void Enter(IStatusStateMachine stateMachine) { }

		/// <summary>
		/// 状態から出る時の処理
		/// </summary>
		public virtual void Exit(IStatusStateMachine stateMachine)
		{
			CancelTimeout();
		}

		/// <summary>
		/// 状態中の更新処理
		/// </summary>
		public virtual void Update(IStatusStateMachine stateMachine) { }

		/// <summary>
		/// EditorApplication.update に登録されたタイムアウトコールバックの参照
		/// </summary>
		private EditorApplication.CallbackFunction _timeoutCallback;

		/// <summary>
		/// タイムアウトをスケジュール
		/// EditorApplication.update に登録し、指定秒数後に自己解除して OnTimeout を呼ぶ
		/// </summary>
		/// <param name="stateMachine">ステートマシン</param>
		/// <param name="seconds">タイムアウト秒数</param>
		protected void ScheduleTimeout(IStatusStateMachine stateMachine, double seconds)
		{
			CancelTimeout();

			var targetTime = EditorApplication.timeSinceStartup + seconds;

			_timeoutCallback = () =>
			{
				if (stateMachine.CurrentState != this)
				{
					CancelTimeout();
					return;
				}

				if (EditorApplication.timeSinceStartup >= targetTime)
				{
					CancelTimeout();
					stateMachine.OnTimeout();
				}
			};

			EditorApplication.update += _timeoutCallback;
		}

		/// <summary>
		/// タイムアウトをキャンセルし、EditorApplication.update から購読解除する
		/// </summary>
		private void CancelTimeout()
		{
			if (_timeoutCallback != null)
			{
				EditorApplication.update -= _timeoutCallback;
				_timeoutCallback = null;
			}
		}
	}
}
