﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace colloid.PBReplacer
{
	public static class DataManagerHelper
	{
		public static event Action<Component> OnComponentsRemoved;
		
		static AvatarData CurrentAvatar => AvatarFieldHelper.CurrentAvatar;
		
		public static List<TComponent> GetAvatarDynamicsComponent<TComponent>() where TComponent : Component
		{
			var result = new List<TComponent>();
			
			// AvatarDynamics内にすでに移動されているコンポーネントを検索（再実行時用）
			if (CurrentAvatar?.AvatarObject == null) return result;
    
			var avatarDynamicsTransform = CurrentAvatar.AvatarObject.transform.Find("AvatarDynamics");
			if (avatarDynamicsTransform == null) return result;
    
			//var avatarDynamics = avatarDynamicsTransform.gameObject;
    
			// コンポーネントを検索
			//var componentsParentFolder = avatarDynamics.transform.Find(FolderName);
			//if (componentsParentFolder == null) return result;
    
			// 該当するコンポーネントをすべて収集
			result.AddRange(avatarDynamicsTransform.GetComponentsInChildren<TComponent>(true));

			return result;
		}
		
		public static void ReloadData()
		{
			if (CurrentAvatar?.AvatarObject != null)
			{
				GameObject currentAvatar = CurrentAvatar.AvatarObject;
				AvatarFieldHelper.SetAvatar(currentAvatar);
			}
		}
		
		public static void NotifyComponentsRemoved(Component component)
		{
			OnComponentsRemoved?.Invoke(component);
		}
	}	
}
