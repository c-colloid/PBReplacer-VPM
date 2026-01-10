namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// 待機中状態
	/// 未処理コンポーネントの有無に応じてメッセージが変化する
	/// </summary>
	public class IdleState : StatusStateBase
	{
		private bool _hasUnprocessed;

		public override StatusStateType StateType => StatusStateType.Idle;

		public override string Message => _hasUnprocessed
			? "Applyを押してください"
			: "Armature内にコンポーネントが見つかりません";

		/// <summary>
		/// 未処理コンポーネントの有無を設定
		/// </summary>
		/// <param name="value">未処理コンポーネントがあるかどうか</param>
		public void SetHasUnprocessed(bool value) => _hasUnprocessed = value;
	}
}
