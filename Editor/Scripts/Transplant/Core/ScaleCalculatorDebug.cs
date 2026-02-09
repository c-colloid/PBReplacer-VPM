using UnityEditor;
using UnityEngine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// ScaleCalculatorのデバッグ用MenuItemクラス。
	/// Hierarchyで2つのアバターを選択してスケールファクターを確認する。
	/// </summary>
	public static class ScaleCalculatorDebug
	{
		[MenuItem("PBReplacer/Debug/Test ScaleCalculator")]
		private static void TestScaleCalculator()
		{
			var selected = Selection.gameObjects;

			if (selected == null || selected.Length < 2)
			{
				Debug.LogWarning("[ScaleCalculator] Hierarchyで2つのアバターを選択してください。");
				return;
			}

			var sourceObj = selected[0];
			var destObj = selected[1];

			var sourceAnimator = sourceObj.GetComponent<Animator>();
			var destAnimator = destObj.GetComponent<Animator>();

			// Armatureを検索
			var sourceArmature = sourceObj.transform.Find("Armature");
			var destArmature = destObj.transform.Find("Armature");

			Debug.Log($"[ScaleCalculator] ソース: {sourceObj.name}, デスティネーション: {destObj.name}");
			Debug.Log($"[ScaleCalculator] ソースAnimator: {(sourceAnimator != null ? (sourceAnimator.isHuman ? "Humanoid" : "Generic") : "None")}");
			Debug.Log($"[ScaleCalculator] デストAnimator: {(destAnimator != null ? (destAnimator.isHuman ? "Humanoid" : "Generic") : "None")}");

			if (sourceArmature != null && destArmature != null)
			{
				Debug.Log($"[ScaleCalculator] ソースArmature lossyScale: {sourceArmature.lossyScale}");
				Debug.Log($"[ScaleCalculator] デストArmature lossyScale: {destArmature.lossyScale}");

				float factor = ScaleCalculator.CalculateScaleFactor(
					sourceArmature, destArmature, sourceAnimator, destAnimator);

				Debug.Log($"[ScaleCalculator] スケールファクター: {factor:F6}");
				Debug.Log($"[ScaleCalculator] テスト: ScaleValue(0.1, {factor:F4}) = {ScaleCalculator.ScaleValue(0.1f, factor):F6}");
				Debug.Log($"[ScaleCalculator] テスト: ScaleVector3((1,1,1), {factor:F4}) = {ScaleCalculator.ScaleVector3(Vector3.one, factor)}");
			}
			else
			{
				Debug.LogWarning($"[ScaleCalculator] Armatureが見つかりません。" +
					$" ソース: {(sourceArmature != null ? "OK" : "NOT FOUND")}," +
					$" デスト: {(destArmature != null ? "OK" : "NOT FOUND")}");
			}

			// Humanoidのみの比較も出力
			if (sourceAnimator != null && destAnimator != null
				&& sourceAnimator.isHuman && destAnimator.isHuman)
			{
				float humanoidFactor = ScaleCalculator.CalculateFromHumanoid(sourceAnimator, destAnimator);
				Debug.Log($"[ScaleCalculator] Humanoidボーン距離ベース: {humanoidFactor:F6}");
			}
		}
	}
}
