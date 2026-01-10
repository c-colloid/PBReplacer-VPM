namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// 警告状態
	/// 3秒後にタイムアウトしてLoading/Noneに遷移する
	/// </summary>
	public class WarningState : StatusStateBase
	{
		private const double TIMEOUT_SECONDS = 3.0;
		private string _warningMessage = "";

		public override StatusStateType StateType => StatusStateType.Warning;
		public override string Message => $"警告: {_warningMessage}";

		/// <summary>
		/// 警告メッセージを設定
		/// </summary>
		/// <param name="message">警告メッセージ</param>
		public void SetWarningMessage(string message) => _warningMessage = message ?? "";

		public override void Enter(IStatusStateMachine stateMachine)
		{
			ScheduleTimeout(stateMachine, TIMEOUT_SECONDS);
		}
	}
}
