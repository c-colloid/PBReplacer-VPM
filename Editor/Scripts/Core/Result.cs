using System;

namespace colloid.PBReplacer
{
	/// <summary>
	/// 処理結果を表す型（Railway Oriented Programming）
	/// 成功時はTSuccess、失敗時はTErrorを保持する
	/// </summary>
	/// <typeparam name="TSuccess">成功時の値の型</typeparam>
	/// <typeparam name="TError">失敗時のエラー情報の型</typeparam>
	public readonly struct Result<TSuccess, TError>
	{
		private readonly TSuccess _value;
		private readonly TError _error;

		/// <summary>
		/// 成功かどうか
		/// </summary>
		public bool IsSuccess { get; }

		/// <summary>
		/// 失敗かどうか
		/// </summary>
		public bool IsFailure => !IsSuccess;

		/// <summary>
		/// 成功時の値（失敗時はアクセス不可）
		/// </summary>
		public TSuccess Value
		{
			get
			{
				if (IsFailure)
					throw new InvalidOperationException("失敗したResultから値を取得できません");
				return _value;
			}
		}

		/// <summary>
		/// 失敗時のエラー情報（成功時はアクセス不可）
		/// </summary>
		public TError Error
		{
			get
			{
				if (IsSuccess)
					throw new InvalidOperationException("成功したResultからエラーを取得できません");
				return _error;
			}
		}

		private Result(TSuccess value, TError error, bool isSuccess)
		{
			_value = value;
			_error = error;
			IsSuccess = isSuccess;
		}

		/// <summary>
		/// 成功結果を作成
		/// </summary>
		public static Result<TSuccess, TError> Success(TSuccess value)
		{
			return new Result<TSuccess, TError>(value, default, true);
		}

		/// <summary>
		/// 失敗結果を作成
		/// </summary>
		public static Result<TSuccess, TError> Failure(TError error)
		{
			return new Result<TSuccess, TError>(default, error, false);
		}

		/// <summary>
		/// 成功時に変換処理を適用（Map）
		/// </summary>
		public Result<TNew, TError> Map<TNew>(Func<TSuccess, TNew> mapper)
		{
			return IsSuccess
				? Result<TNew, TError>.Success(mapper(_value))
				: Result<TNew, TError>.Failure(_error);
		}

		/// <summary>
		/// 成功時に別のResultを返す処理を適用（FlatMap/Bind）
		/// </summary>
		public Result<TNew, TError> Bind<TNew>(Func<TSuccess, Result<TNew, TError>> binder)
		{
			return IsSuccess ? binder(_value) : Result<TNew, TError>.Failure(_error);
		}

		/// <summary>
		/// 成功/失敗に応じて処理を実行
		/// </summary>
		public TResult Match<TResult>(Func<TSuccess, TResult> onSuccess, Func<TError, TResult> onFailure)
		{
			return IsSuccess ? onSuccess(_value) : onFailure(_error);
		}

		/// <summary>
		/// 成功/失敗に応じてアクションを実行
		/// </summary>
		public void Match(Action<TSuccess> onSuccess, Action<TError> onFailure)
		{
			if (IsSuccess)
				onSuccess(_value);
			else
				onFailure(_error);
		}

		/// <summary>
		/// 成功時のみアクションを実行
		/// </summary>
		public Result<TSuccess, TError> OnSuccess(Action<TSuccess> action)
		{
			if (IsSuccess)
				action(_value);
			return this;
		}

		/// <summary>
		/// 失敗時のみアクションを実行
		/// </summary>
		public Result<TSuccess, TError> OnFailure(Action<TError> action)
		{
			if (IsFailure)
				action(_error);
			return this;
		}

		/// <summary>
		/// 失敗時にデフォルト値を返す
		/// </summary>
		public TSuccess GetValueOrDefault(TSuccess defaultValue = default)
		{
			return IsSuccess ? _value : defaultValue;
		}

		/// <summary>
		/// 失敗時にファクトリ関数でデフォルト値を生成
		/// </summary>
		public TSuccess GetValueOrElse(Func<TError, TSuccess> fallback)
		{
			return IsSuccess ? _value : fallback(_error);
		}
	}

	/// <summary>
	/// Result型の拡張メソッド
	/// </summary>
	public static class ResultExtensions
	{
		/// <summary>
		/// 複数のResultを結合（すべて成功なら成功、一つでも失敗なら失敗）
		/// </summary>
		public static Result<(T1, T2), TError> Combine<T1, T2, TError>(
			this Result<T1, TError> first,
			Result<T2, TError> second)
		{
			if (first.IsFailure)
				return Result<(T1, T2), TError>.Failure(first.Error);
			if (second.IsFailure)
				return Result<(T1, T2), TError>.Failure(second.Error);
			return Result<(T1, T2), TError>.Success((first.Value, second.Value));
		}
	}

	/// <summary>
	/// 処理エラーを表すクラス
	/// </summary>
	public class ProcessingError
	{
		public string Message { get; }
		public Exception Exception { get; }
		public ErrorType Type { get; }

		public ProcessingError(string message, ErrorType type = ErrorType.Unknown, Exception exception = null)
		{
			Message = message;
			Type = type;
			Exception = exception;
		}

		public static ProcessingError FromException(Exception ex, ErrorType type = ErrorType.Unknown)
		{
			return new ProcessingError(ex.Message, type, ex);
		}

		public override string ToString() => Message;
	}

	/// <summary>
	/// エラーの種類
	/// </summary>
	public enum ErrorType
	{
		Unknown,
		AvatarNotSet,
		ComponentNotFound,
		ProcessingFailed,
		ValidationFailed
	}
}
