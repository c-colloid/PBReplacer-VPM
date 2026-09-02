using System;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// メインウィンドウで扱うコンポーネントのカテゴリ（レールのチップ 1 つ = 列 1 つ）
	/// </summary>
	public enum ComponentCategory
	{
		PhysBone = 0,
		PhysBoneCollider = 1,
		Constraint = 2,
		Contact = 3,
	}

	/// <summary>
	/// カテゴリの表示名・対象型・処理グループなどの静的情報
	/// </summary>
	public static class ComponentCategoryInfo
	{
		public static readonly ComponentCategory[] All =
		{
			ComponentCategory.PhysBone,
			ComponentCategory.PhysBoneCollider,
			ComponentCategory.Constraint,
			ComponentCategory.Contact,
		};

		/// <summary>列見出し・ツールチップに使う名前</summary>
		public static string DisplayName(ComponentCategory category)
		{
			switch (category)
			{
				case ComponentCategory.PhysBone: return "PhysBone";
				case ComponentCategory.PhysBoneCollider: return "PhysBone Collider";
				case ComponentCategory.Constraint: return "Constraint";
				case ComponentCategory.Contact: return "Contact";
				default: return category.ToString();
			}
		}

		/// <summary>ドロップで追加できるコンポーネント型（複数あるときはメニューで選ぶ）</summary>
		public static Type[] ComponentTypes(ComponentCategory category)
		{
			switch (category)
			{
				case ComponentCategory.PhysBone:
					return new[] { typeof(VRCPhysBone) };
				case ComponentCategory.PhysBoneCollider:
					return new[] { typeof(VRCPhysBoneCollider) };
				case ComponentCategory.Constraint:
					return new[]
					{
						typeof(VRCPositionConstraint), typeof(VRCRotationConstraint), typeof(VRCScaleConstraint),
						typeof(VRCParentConstraint), typeof(VRCLookAtConstraint), typeof(VRCAimConstraint),
					};
				case ComponentCategory.Contact:
					return new[] { typeof(VRCContactSender), typeof(VRCContactReceiver) };
				default:
					return Array.Empty<Type>();
			}
		}

		/// <summary>SDK のスクリプトアイコンが無いときに使う Unity 内蔵の型</summary>
		public static Type FallbackIconType(ComponentCategory category)
		{
			switch (category)
			{
				case ComponentCategory.PhysBone: return typeof(HingeJoint);
				case ComponentCategory.PhysBoneCollider: return typeof(CapsuleCollider);
				case ComponentCategory.Constraint: return typeof(ParentConstraint);
				case ComponentCategory.Contact: return typeof(SphereCollider);
				default: return typeof(Component);
			}
		}

		/// <summary>
		/// 処理グループ（CreateCommand / Managers.ReloadForTab のインデックス）。
		/// PhysBone と PhysBoneCollider は参照解決のため常に同じグループで処理する。
		/// </summary>
		public static int ProcessGroup(ComponentCategory category)
		{
			switch (category)
			{
				case ComponentCategory.PhysBone:
				case ComponentCategory.PhysBoneCollider: return 0;
				case ComponentCategory.Constraint: return 1;
				case ComponentCategory.Contact: return 2;
				default: return -1;
			}
		}

		/// <summary>処理グループの表示名</summary>
		public static string ProcessGroupName(int group)
		{
			switch (group)
			{
				case 0: return "PhysBone";
				case 1: return "Constraint";
				case 2: return "Contact";
				default: return "";
			}
		}
	}
}
