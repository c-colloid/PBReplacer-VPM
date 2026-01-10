namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// 状態インターフェース
	/// ステートマシンで管理される各状態の共通インターフェース
	/// </summary>
	public interface IState
	{
		/// <summary>
		/// 状態タイプ（識別用）
		/// </summary>
		StatusStateType StateType { get; }

		/// <summary>
		/// 表示メッセージ
		/// </summary>
		string Message { get; }

		/// <summary>
		/// 状態に入った時の処理
		/// </summary>
		/// <param name="stateMachine">ステートマシン</param>
		void Enter(IStatusStateMachine stateMachine);

		/// <summary>
		/// 状態から出る時の処理
		/// </summary>
		/// <param name="stateMachine">ステートマシン</param>
		void Exit(IStatusStateMachine stateMachine);

		/// <summary>
		/// 状態中の更新処理
		/// </summary>
		/// <param name="stateMachine">ステートマシン</param>
		void Update(IStatusStateMachine stateMachine);
	}
}
