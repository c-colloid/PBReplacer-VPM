namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// アバター未設定状態
	/// </summary>
	public class NoneState : StatusStateBase
	{
		public override StatusStateType StateType => StatusStateType.None;
		public override string Message => "アバターを設定してください";
	}
}
