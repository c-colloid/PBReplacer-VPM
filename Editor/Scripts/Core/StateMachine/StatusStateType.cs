namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// ステータス状態の種類
	/// </summary>
	public enum StatusStateType
	{
		/// <summary>アバター未設定</summary>
		None,

		/// <summary>データ読み込み中</summary>
		Loading,

		/// <summary>待機中（処理可能）</summary>
		Idle,

		/// <summary>処理中</summary>
		Processing,

		/// <summary>処理完了</summary>
		Complete,

		/// <summary>警告あり</summary>
		Warning,

		/// <summary>エラー発生</summary>
		Error
	}
}
