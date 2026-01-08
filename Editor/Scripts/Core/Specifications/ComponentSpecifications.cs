using UnityEngine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// コンポーネント検索用の条件定義
	///
	/// 【使用例】
	/// var spec = new ComponentSpecs.IsProcessTarget<VRCPhysBone>(armature, avatarDynamics);
	/// var targets = allPhysBones.Where(pb => spec.IsSatisfiedBy(pb)).ToList();
	/// </summary>
	public static class ComponentSpecs
	{
		/// <summary>Armature内にあるコンポーネント</summary>
		public class InArmature<T> : Specification<T> where T : Component
		{
			private readonly Transform _armature;

			public InArmature(Transform armature)
			{
				_armature = armature;
			}

			public override bool IsSatisfiedBy(T item)
				=> item != null && _armature != null && item.transform.IsChildOf(_armature);
		}

		/// <summary>AvatarDynamics内にあるコンポーネント</summary>
		public class InAvatarDynamics<T> : Specification<T> where T : Component
		{
			private readonly Transform _avatarDynamics;

			public InAvatarDynamics(Transform avatarDynamics)
			{
				_avatarDynamics = avatarDynamics;
			}

			public override bool IsSatisfiedBy(T item)
				=> item != null && _avatarDynamics != null && item.transform.IsChildOf(_avatarDynamics);
		}

		/// <summary>
		/// 処理対象（AvatarDynamics内にない）
		///
		/// Armature内にあり、かつAvatarDynamics内にないコンポーネントを判定
		/// </summary>
		public class IsProcessTarget<T> : Specification<T> where T : Component
		{
			private readonly ISpecification<T> _spec;

			public IsProcessTarget(Transform armature, Transform avatarDynamics)
			{
				var inArmature = new InArmature<T>(armature);
				var inAvatarDynamics = new InAvatarDynamics<T>(avatarDynamics);

				// Armature内にあり、かつAvatarDynamics内にない
				_spec = inArmature.And(inAvatarDynamics.Not());
			}

			public override bool IsSatisfiedBy(T item) => _spec.IsSatisfiedBy(item);
		}

		/// <summary>アクティブなコンポーネント</summary>
		public class IsActive<T> : Specification<T> where T : Component
		{
			public override bool IsSatisfiedBy(T item)
				=> item != null && item.gameObject.activeInHierarchy;
		}

		/// <summary>有効なコンポーネント（nullでない）</summary>
		public class IsValid<T> : Specification<T> where T : Component
		{
			public override bool IsSatisfiedBy(T item)
				=> item != null;
		}

		/// <summary>特定のGameObject配下のコンポーネント</summary>
		public class IsChildOf<T> : Specification<T> where T : Component
		{
			private readonly Transform _parent;

			public IsChildOf(Transform parent)
			{
				_parent = parent;
			}

			public override bool IsSatisfiedBy(T item)
				=> item != null && _parent != null && item.transform.IsChildOf(_parent);
		}

		/// <summary>
		/// 指定したコレクションに含まれないコンポーネント
		/// </summary>
		public class NotInCollection<T> : Specification<T> where T : Component
		{
			private readonly System.Collections.Generic.HashSet<T> _excludeSet;

			public NotInCollection(System.Collections.Generic.IEnumerable<T> excludeCollection)
			{
				_excludeSet = new System.Collections.Generic.HashSet<T>(excludeCollection);
			}

			public override bool IsSatisfiedBy(T item)
				=> item != null && !_excludeSet.Contains(item);
		}
	}
}
