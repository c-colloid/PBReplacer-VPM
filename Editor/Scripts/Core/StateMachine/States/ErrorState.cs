namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// エラー状態
	/// 3秒後にタイムアウトしてLoading/Noneに遷移する
	/// </summary>
	public class ErrorState : StatusStateBase
	{
		private const double TIMEOUT_SECONDS = 3.0;
		private string _errorMessage = "";

		public override StatusStateType StateType => StatusStateType.Error;
		public override string Message => $"エラー: {_errorMessage}";

		/// <summary>
		/// エラーメッセージを設定
		/// </summary>
		/// <param name="message">エラーメッセージ</param>
		public void SetErrorMessage(string message) => _errorMessage = message ?? "";

		public override void Enter(IStatusStateMachine stateMachine)
		{
			ScheduleTimeout(stateMachine, TIMEOUT_SECONDS);
		}
	}
}
