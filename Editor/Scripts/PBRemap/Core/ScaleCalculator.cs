using UnityEngine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// アバター間のスケール差異を算出するユーティリティ。
	/// FBXインポート時のメッシュスケール差異はTransform.lossyScaleでは
	/// 吸収できないため、Humanoidボーン距離による算出を優先する。
	/// </summary>
	public static class ScaleCalculator
	{
		private const float Epsilon = 1e-6f;

		/// <summary>
		/// ソースとデスティネーションのArmature間のスケールファクターを算出する。
		/// 両方がHumanoidの場合はボーン距離ベース、それ以外はlossyScaleベースで算出する。
		/// </summary>
		/// <param name="sourceArmature">ソース側のArmature Transform</param>
		/// <param name="destArmature">デスティネーション側のArmature Transform</param>
		/// <param name="sourceAnimator">ソース側のAnimator（Humanoid判定用、省略可）</param>
		/// <param name="destAnimator">デスティネーション側のAnimator（Humanoid判定用、省略可）</param>
		/// <returns>デスティネーション / ソースのスケール比率。算出不能な場合は1.0f</returns>
		public static float CalculateScaleFactor(
			Transform sourceArmature,
			Transform destArmature,
			Animator sourceAnimator = null,
			Animator destAnimator = null)
		{
			if (sourceArmature == null || destArmature == null)
				return 1.0f;

			// ソース側スケールが0に近い場合は1.0fを返す
			if (Mathf.Abs(sourceArmature.lossyScale.y) < Epsilon)
				return 1.0f;

			// 両方Humanoidならボーン距離ベースで算出
			if (sourceAnimator != null && destAnimator != null
				&& sourceAnimator.isHuman && destAnimator.isHuman)
			{
				return CalculateFromHumanoid(sourceAnimator, destAnimator);
			}

			// lossyScaleのy成分の比率
			return destArmature.lossyScale.y / sourceArmature.lossyScale.y;
		}

		/// <summary>
		/// HumanoidアバターのHips→Head間距離を比較してスケールファクターを算出する。
		/// Hips/Headボーンが取得できない場合はlossyScaleベースにフォールバックする。
		/// </summary>
		/// <param name="source">ソース側のAnimator（isHuman == true）</param>
		/// <param name="destination">デスティネーション側のAnimator（isHuman == true）</param>
		/// <returns>デスティネーション / ソースのスケール比率</returns>
		public static float CalculateFromHumanoid(Animator source, Animator destination)
		{
			if (source == null || destination == null)
				return 1.0f;

			var sourceHips = source.GetBoneTransform(HumanBodyBones.Hips);
			var sourceHead = source.GetBoneTransform(HumanBodyBones.Head);
			var destHips = destination.GetBoneTransform(HumanBodyBones.Hips);
			var destHead = destination.GetBoneTransform(HumanBodyBones.Head);

			// Hips/Headが取得できない場合はlossyScaleフォールバック
			if (sourceHips == null || sourceHead == null
				|| destHips == null || destHead == null)
			{
				float sourceScale = source.transform.lossyScale.y;
				float destScale = destination.transform.lossyScale.y;

				if (Mathf.Abs(sourceScale) < Epsilon)
					return 1.0f;

				return destScale / sourceScale;
			}

			float sourceDistance = Vector3.Distance(sourceHips.position, sourceHead.position);
			float destDistance = Vector3.Distance(destHips.position, destHead.position);

			if (sourceDistance < Epsilon)
				return 1.0f;

			return destDistance / sourceDistance;
		}

		/// <summary>
		/// スカラー値にスケールファクターを適用する。
		/// PhysBoneのradiusやColliderのサイズ等に使用。
		/// </summary>
		/// <param name="value">元の値</param>
		/// <param name="scaleFactor">スケールファクター</param>
		/// <returns>スケーリングされた値</returns>
		public static float ScaleValue(float value, float scaleFactor)
		{
			return value * scaleFactor;
		}

		/// <summary>
		/// Vector3にスケールファクターを適用する。
		/// 位置オフセットやサイズパラメータ等に使用。
		/// </summary>
		/// <param name="value">元のベクトル</param>
		/// <param name="scaleFactor">スケールファクター</param>
		/// <returns>スケーリングされたベクトル</returns>
		public static Vector3 ScaleVector3(Vector3 value, float scaleFactor)
		{
			return value * scaleFactor;
		}
	}
}
