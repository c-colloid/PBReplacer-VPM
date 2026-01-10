namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// データ読み込み中状態
	/// </summary>
	public class LoadingState : StatusStateBase
	{
		public override StatusStateType StateType => StatusStateType.Loading;
		public override string Message => "読み込み中...";
	}
}
