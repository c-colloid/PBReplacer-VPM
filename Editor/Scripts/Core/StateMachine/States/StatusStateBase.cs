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
		public virtual void Exit(IStatusStateMachine stateMachine) { }

		/// <summary>
		/// 状態中の更新処理
		/// </summary>
		public virtual void Update(IStatusStateMachine stateMachine) { }

		/// <summary>
		/// タイムアウトをスケジュール
		/// </summary>
		/// <param name="stateMachine">ステートマシン</param>
		/// <param name="seconds">タイムアウト秒数</param>
		protected void ScheduleTimeout(IStatusStateMachine stateMachine, double seconds)
		{
			var startTime = EditorApplication.timeSinceStartup;
			EditorApplication.delayCall += () => CheckTimeout(stateMachine, startTime, seconds);
		}

		/// <summary>
		/// タイムアウトチェック
		/// </summary>
		private void CheckTimeout(IStatusStateMachine stateMachine, double startTime, double seconds)
		{
			// 状態が変わっていたら何もしない
			if (stateMachine.CurrentState != this) return;

			// タイムアウト時間が経過したか確認
			if (EditorApplication.timeSinceStartup - startTime >= seconds)
			{
				stateMachine.OnTimeout();
			}
			else
			{
				// まだ時間が経過していなければ再スケジュール
				EditorApplication.delayCall += () => CheckTimeout(stateMachine, startTime, seconds);
			}
		}
	}
}
