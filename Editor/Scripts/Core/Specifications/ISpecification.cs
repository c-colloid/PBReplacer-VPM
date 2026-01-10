namespace colloid.PBReplacer
{
	/// <summary>
	/// 条件を表すインターフェース
	///
	/// 【なぜこのパターンを使うか】
	/// - 条件を組み合わせられる（AND, OR, NOT）
	/// - 条件に名前を付けて再利用できる
	/// - テストしやすい
	/// </summary>
	public interface ISpecification<T>
	{
		/// <summary>条件を満たすかどうか</summary>
		bool IsSatisfiedBy(T item);

		/// <summary>AND条件を作成</summary>
		ISpecification<T> And(ISpecification<T> other);

		/// <summary>OR条件を作成</summary>
		ISpecification<T> Or(ISpecification<T> other);

		/// <summary>NOT条件を作成</summary>
		ISpecification<T> Not();
	}

	/// <summary>
	/// 基底実装
	/// </summary>
	public abstract class Specification<T> : ISpecification<T>
	{
		public abstract bool IsSatisfiedBy(T item);

		public ISpecification<T> And(ISpecification<T> other)
			=> new AndSpecification<T>(this, other);

		public ISpecification<T> Or(ISpecification<T> other)
			=> new OrSpecification<T>(this, other);

		public ISpecification<T> Not()
			=> new NotSpecification<T>(this);
	}

	/// <summary>
	/// AND条件
	/// </summary>
	public class AndSpecification<T> : Specification<T>
	{
		private readonly ISpecification<T> _left;
		private readonly ISpecification<T> _right;

		public AndSpecification(ISpecification<T> left, ISpecification<T> right)
		{
			_left = left;
			_right = right;
		}

		public override bool IsSatisfiedBy(T item)
			=> _left.IsSatisfiedBy(item) && _right.IsSatisfiedBy(item);
	}

	/// <summary>
	/// OR条件
	/// </summary>
	public class OrSpecification<T> : Specification<T>
	{
		private readonly ISpecification<T> _left;
		private readonly ISpecification<T> _right;

		public OrSpecification(ISpecification<T> left, ISpecification<T> right)
		{
			_left = left;
			_right = right;
		}

		public override bool IsSatisfiedBy(T item)
			=> _left.IsSatisfiedBy(item) || _right.IsSatisfiedBy(item);
	}

	/// <summary>
	/// NOT条件
	/// </summary>
	public class NotSpecification<T> : Specification<T>
	{
		private readonly ISpecification<T> _inner;

		public NotSpecification(ISpecification<T> inner)
		{
			_inner = inner;
		}

		public override bool IsSatisfiedBy(T item)
			=> !_inner.IsSatisfiedBy(item);
	}
}
