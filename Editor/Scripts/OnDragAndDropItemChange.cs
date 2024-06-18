using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using VRC.SDKBase;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
{
	class OnAvatarFieldDragAndDropItemChange : Manipulator
	{
		private GameObject _targetObject;
		private const string title = "衣装用オプション";
		private const string message = "このオブジェクトにはAvatarDiscriptorがついていません\n衣装用オプションを適用しますか？\n\n" +
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
			_targetObject = DragAndDrop.objectReferences[0] as GameObject;
			if (!_targetObject.TryGetComponent<VRC_AvatarDescriptor>(out var component)) DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
		}
		
		private void OnDropItem(DragPerformEvent evt){
			if (_targetObject.TryGetComponent<VRC_AvatarDescriptor>(out var VRCcomponent)) return; 
			
			if (_targetObject.TryGetComponent<Animator>(out var AvatarAnimatorcomponent)) {
				var window = PBReplacer.GetWindow<PBReplacer>();
				window.rootVisualElement.Q<ObjectField>().value = AvatarAnimatorcomponent;
				return;
			}
			
			#if MODULAR_AVATAR
			var resultList = new List<ModularAvatarMergeArmature>();
			_targetObject.transform.GetComponentsInChildren<ModularAvatarMergeArmature>(true,resultList);
			if (resultList.Count > 0){
				var window = PBReplacer.GetWindow<PBReplacer>();
				window.rootVisualElement.Q<ObjectField>().value = _targetObject.transform;
				return;
			}
			
			if (_targetObject.TryGetComponent<ModularAvatarMergeArmature>(out var MAcomponent)) {
			var window = PBReplacer.GetWindow<PBReplacer>();
				window.rootVisualElement.Q<ObjectField>().value = MAcomponent;
			return;
			}
			#endif
			if (EditorUtility.DisplayDialog(title,message,"OK","Cancel"))
			{
				var window = PBReplacer.GetWindow<PBReplacer>();
				window.rootVisualElement.Q<ObjectField>().value = _targetObject.transform;
			}
		}
	}
	
}
