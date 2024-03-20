using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using VRC.SDKBase;

namespace colloid.PBReplacer
{
	class OnDragAndDropItemChange : Manipulator
	{
		private GameObject targetObject;
		private string title = "衣装用オプション";
		private string message = "このオブジェクトにはAvatarDiscriptorがついていません\n衣装用オプションを適用しますか？\n\n" +
			"※このオプションは想定外の挙動をする可能性があります\n※ツールの特性を理解したうえでご利用ください";
		
		protected override void RegisterCallbacksOnTarget() {
			//throw new System.NotImplementedException();
			target.RegisterCallback<DragUpdatedEvent>(OnDragItem);
			target.RegisterCallback<DragPerformEvent>(OnDropItem);
		}
		
		protected override void UnregisterCallbacksFromTarget() {
			//throw new System.NotImplementedException();
			target.UnregisterCallback<DragUpdatedEvent>(OnDragItem);
			target.UnregisterCallback<DragPerformEvent>(OnDropItem);
		}
		
		private void OnDragItem(DragUpdatedEvent evt){
			targetObject = DragAndDrop.objectReferences[0] as GameObject;
			if (!targetObject.TryGetComponent<VRC_AvatarDescriptor>(out var component)) DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
		}
		
		private void OnDropItem(DragPerformEvent evt){
			if (targetObject.TryGetComponent<VRC_AvatarDescriptor>(out var VRCcomponent)) return; 
			#if MODULAR_AVATAR
			if (targetObject.TryGetComponent<ModularAvatarMergeArmature>(out var MAcomponent)) {
			var window = GetWindow<PBReplacer>();
			window._root.Q<ObjectField>().value = MAcomponent;
			return;
			}
			#endif
			if (EditorUtility.DisplayDialog(title,message,"OK","Cancel"))
			{
				var window = PBReplacer.GetWindow<PBReplacer>();
				window.rootVisualElement.Q<ObjectField>().value = targetObject.transform;
			}
		}
	}
	
}
