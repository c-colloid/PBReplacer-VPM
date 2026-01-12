namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// 処理完了状態
	/// 2秒後にタイムアウトしてLoading/Noneに遷移する
	/// </summary>
	public class CompleteState : StatusStateBase
	{
		private const double TIMEOUT_SECONDS = 2.0;
		private int _processedCount;

		public override StatusStateType StateType => StatusStateType.Complete;
		public override string Message => $"処理完了! 処理数: {_processedCount}";

		/// <summary>
		/// 処理件数を設定
		/// </summary>
		/// <param name="count">処理したコンポーネント数</param>
		public void SetProcessedCount(int count) => _processedCount = count;

		public override void Enter(IStatusStateMachine stateMachine)
		{
			ScheduleTimeout(stateMachine, TIMEOUT_SECONDS);
		}
	}
}
