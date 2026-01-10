namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// 処理中状態
	/// </summary>
	public class ProcessingState : StatusStateBase
	{
		public override StatusStateType StateType => StatusStateType.Processing;
		public override string Message => "処理中...";
	}
}
