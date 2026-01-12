using System;

namespace colloid.PBReplacer.StateMachine
{
	/// <summary>
	/// ステータスステートマシンのインターフェース
	/// </summary>
	public interface IStatusStateMachine
	{
		#region 状態プロパティ

		/// <summary>
		/// 現在の状態
		/// </summary>
		IState CurrentState { get; }

		/// <summary>
		/// 現在の状態タイプ
		/// </summary>
		StatusStateType CurrentStateType { get; }

		/// <summary>
		/// 現在の状態コンテキスト
		/// </summary>
		StatusStateContext Context { get; }

		#endregion

		#region 状態遷移

		/// <summary>
		/// 指定した状態タイプに遷移
		/// </summary>
		/// <param name="stateType">遷移先の状態タイプ</param>
		void TransitionTo(StatusStateType stateType);

		#endregion

		#region 外部トリガー

		/// <summary>
		/// アバター設定/解除時に呼び出す
		/// </summary>
		/// <param name="hasAvatar">アバターが設定されているかどうか</param>
		void SetAvatar(bool hasAvatar);

		/// <summary>
		/// データ読み込み完了時に呼び出す
		/// </summary>
		void OnDataLoaded();

		/// <summary>
		/// 処理開始時に呼び出す
		/// </summary>
		void StartProcessing();

		/// <summary>
		/// 処理完了時に呼び出す
		/// </summary>
		/// <param name="processedCount">処理したコンポーネント数</param>
		void Complete(int processedCount);

		/// <summary>
		/// 警告発生時に呼び出す
		/// </summary>
		/// <param name="message">警告メッセージ</param>
		void Warn(string message);

		/// <summary>
		/// エラー発生時に呼び出す
		/// </summary>
		/// <param name="errorMessage">エラーメッセージ</param>
		void Fail(string errorMessage);

		/// <summary>
		/// Idle状態のメッセージを更新
		/// </summary>
		/// <param name="hasUnprocessed">未処理コンポーネントがあるかどうか</param>
		void UpdateIdleState(bool hasUnprocessed);

		/// <summary>
		/// タイムアウト時に呼び出される
		/// アバター有無に応じてLoading/Noneに遷移
		/// </summary>
		void OnTimeout();

		/// <summary>
		/// タブ変更時に呼び出される
		/// Complete/Warning/Error状態からはタイムアウトを待たずに即座にIdleに遷移
		/// </summary>
		/// <param name="hasUnprocessed">未処理コンポーネントがあるかどうか</param>
		void OnTabChanged(bool hasUnprocessed);

		#endregion

		#region イベント

		/// <summary>
		/// 状態変更時に発火するイベント
		/// </summary>
		event Action<StatusStateContext> OnStateChanged;

		#endregion
	}
}
