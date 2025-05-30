﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace colloid.PBReplacer
{
	/// <summary>
	/// AvatarFieldの各種処理を扱うUtilityクラス
	/// </summary>
	public static class AvatarFieldHelper
	{
		#region Events
		// データの変更を通知するイベント
		public static event Action<AvatarData> OnAvatarChanged;
		public static event Action<string> OnStatusMessageChanged;
        #endregion
		
		#region Data References
		// 設定への参照
		private static PBReplacerSettings _settings;
        #endregion
        
        #region Properties
		// 現在のアバターデータ
		private static AvatarData _currentAvatar;
		public static AvatarData CurrentAvatar => _currentAvatar;
        #endregion
		
		#region Public Methods
		/// <summary>
		/// アバターを設定し、コンポーネントを読み込む
		/// </summary>
		/// <param name="avatarObject">アバターのGameObject</param>
		/// <returns>成功した場合はtrue</returns>
		public static bool SetAvatar(GameObject avatarObject)
		{
			if (avatarObject == null)
			{
				ClearAvatar();
				NotifyStatusMessage("アバターをセットしてください");
				return false;
			}

			try
			{
				// アバターデータを生成
				_currentAvatar = new AvatarData(avatarObject);
                
				// 変更通知
				OnAvatarChanged.Invoke(_currentAvatar);
				
				return true;
			}
				catch (Exception ex)
				{
					Debug.LogError($"アバターの設定中にエラーが発生しました: {ex.Message}");
					ClearAvatar();
					OnStatusMessageChanged.Invoke($"エラー: {ex.Message}");
					return false;
				}
		}
		
		public static void ClearAvatar()
		{
			_currentAvatar = null;
			OnAvatarChanged?.Invoke(null);
		}
		
		public static void Cleanup()
		{
			_currentAvatar = null;
			OnAvatarChanged = null;
			OnStatusMessageChanged = null;
		}
		#endregion
		
		private static void NotifyStatusMessage(string message)
		{
			OnStatusMessageChanged?.Invoke(message);
		}
	}	
}
