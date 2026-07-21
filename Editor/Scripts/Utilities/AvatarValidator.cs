using UnityEngine;
using VRC.SDKBase;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
	/// <summary>
	/// アバター有効判定の結果
	/// </summary>
	public class AvatarValidationResult
	{
		/// <summary>アバターとして有効かどうか（VRC_AvatarDescriptor / Animator / MergeArmatureのいずれかを保持）</summary>
		public bool IsValid { get; }

		/// <summary>検出根拠の種別</summary>
		public AvatarDetectionMethod Method { get; }

		/// <summary>有効でない場合に表示する警告メッセージ（有効な場合はnull）</summary>
		public string WarningMessage { get; }

		public AvatarValidationResult(bool isValid, AvatarDetectionMethod method, string warningMessage = null)
		{
			IsValid = isValid;
			Method = method;
			WarningMessage = warningMessage;
		}
	}

	/// <summary>
	/// GameObjectがアバターとして有効かどうかを判定する共通ロジック。
	/// VRC_AvatarDescriptor / Animator / ModularAvatarのMergeArmatureのいずれかを持つ場合に有効と判定する。
	/// 各所（ドラッグ&ドロップ判定・アバターフィールド設定など）に散在していた判定を集約する。
	/// </summary>
	public static class AvatarValidator
	{
		// アバターとして有効なコンポーネントが見つからない場合の警告メッセージ
		private const string WARNING_MESSAGE =
			"このオブジェクトにはVRC_AvatarDescriptor / Animator / MergeArmatureのいずれも見つかりません";

		/// <summary>
		/// GameObjectがアバターとして有効かどうかを判定する
		/// 判定優先順位: VRC_AvatarDescriptor → MergeArmature(ModularAvatar) → Animator
		/// </summary>
		/// <param name="obj">判定対象のGameObject</param>
		/// <returns>判定結果</returns>
		public static AvatarValidationResult Validate(GameObject obj)
		{
			if (obj == null)
			{
				return new AvatarValidationResult(false, AvatarDetectionMethod.None, WARNING_MESSAGE);
			}

			// アバターディスクリプタ
			if (obj.GetComponent<VRC_AvatarDescriptor>() != null)
			{
				return new AvatarValidationResult(true, AvatarDetectionMethod.VRCAvatarDescriptor);
			}

			// ModularAvatar
			#if MODULAR_AVATAR
			if (HasModularAvatarMergeArmature(obj))
			{
				return new AvatarValidationResult(true, AvatarDetectionMethod.MergeArmature);
			}
			#endif

			// アニメーター
			if (obj.GetComponent<Animator>() != null)
			{
				return new AvatarValidationResult(true, AvatarDetectionMethod.Animator);
			}

			// いずれも持たない場合は無効
			return new AvatarValidationResult(false, AvatarDetectionMethod.None, WARNING_MESSAGE);
		}

		#if MODULAR_AVATAR
		/// <summary>
		/// ModularAvatarのマージアーマチュアを持つかどうかを判定
		/// </summary>
		private static bool HasModularAvatarMergeArmature(GameObject obj)
		{
			return obj.GetComponent<ModularAvatarMergeArmature>() != null ||
			obj.GetComponentInChildren<ModularAvatarMergeArmature>(true) != null;
		}
		#endif
	}
}
