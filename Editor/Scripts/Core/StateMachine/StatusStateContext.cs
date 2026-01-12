using UnityEditor;

namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// 状態コンテキスト
	/// 現在の状態情報を保持するイミュータブルなクラス
	/// </summary>
	public class StatusStateContext
	{
		/// <summary>
		/// 現在の状態
		/// </summary>
		public IState State { get; }

		/// <summary>
		/// 状態タイプ
		/// </summary>
		public StatusStateType StateType => State?.StateType ?? StatusStateType.None;

		/// <summary>
		/// 表示メッセージ
		/// </summary>
		public string Message => State?.Message ?? "";

		/// <summary>
		/// コンテキスト作成時刻
		/// </summary>
		public double Timestamp { get; }

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="state">状態</param>
		public StatusStateContext(IState state)
		{
			State = state;
			Timestamp = EditorApplication.timeSinceStartup;
		}
	}
}
