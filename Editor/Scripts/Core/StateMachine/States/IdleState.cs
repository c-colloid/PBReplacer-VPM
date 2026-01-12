namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// Idle状態の種類
	/// </summary>
	public enum IdleStateKind
	{
		/// <summary>未処理コンポーネントあり</summary>
		HasUnprocessed,
		/// <summary>全て処理済み</summary>
		AllProcessed,
		/// <summary>対象なし</summary>
		NoComponents
	}

	/// <summary>
	/// 待機中状態
	/// 未処理コンポーネントの有無に応じてメッセージが変化する
	/// </summary>
	public class IdleState : StatusStateBase
	{
		private IdleStateKind _kind = IdleStateKind.NoComponents;

		public override StatusStateType StateType => StatusStateType.Idle;

		public override string Message => _kind switch
		{
			IdleStateKind.HasUnprocessed => "Applyを押してください",
			IdleStateKind.AllProcessed => "すべて処理済みです",
			IdleStateKind.NoComponents => "Armature内にコンポーネントが見つかりません",
			_ => ""
		};

		/// <summary>
		/// Idle状態の種類を設定
		/// </summary>
		/// <param name="kind">Idle状態の種類</param>
		public void SetKind(IdleStateKind kind) => _kind = kind;
	}
}
