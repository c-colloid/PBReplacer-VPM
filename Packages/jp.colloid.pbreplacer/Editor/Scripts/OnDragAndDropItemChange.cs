using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using VRC.SDKBase;
using VRC.SDK3.Dynamics.PhysBone.Components;

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
	
	class OnListViewDragAndDropItemChange : Manipulator
	{
		GameObject _tempRootObject;
		private GameObject[] _targetObjects;
		PBReplacer _window;
		System.Type _componentType;
		
		public OnListViewDragAndDropItemChange()
		{
			_window = PBReplacer.GetWindow<PBReplacer>();
			_tempRootObject = new GameObject("LitVewTempRoot");
		}
		
		public void OnDisable()
		{
			Object.DestroyImmediate(_tempRootObject);
		}
		
		protected override void RegisterCallbacksOnTarget() {
			_componentType = target.name == "PBListField" ? typeof(VRCPhysBone) : typeof(VRCPhysBoneCollider);
			//throw new System.NotImplementedException();
			target.RegisterCallback<DragEnterEvent>(OnDragEnter);
			target.RegisterCallback<DragLeaveEvent>(OnDragLeave);
			//_window.rootVisualElement.panel.visualTree.RegisterCallback<MouseLeaveWindowEvent>(OnLeave);
			target.RegisterCallback<DragUpdatedEvent>(OnDragItem);
			target.RegisterCallback<DragPerformEvent>(OnDropItem);
		}
		
		protected override void UnregisterCallbacksFromTarget() {
			//throw new System.NotImplementedException();
			Debug.Log("Unregistar");
			Object.DestroyImmediate(_tempRootObject);
			target.UnregisterCallback<DragEnterEvent>(OnDragEnter);
			target.UnregisterCallback<DragLeaveEvent>(OnDragLeave);
			target.UnregisterCallback<DragUpdatedEvent>(OnDragItem);
			target.UnregisterCallback<DragPerformEvent>(OnDropItem);
		}
		
		private void OnDragEnter(DragEnterEvent evt){
			PointerCaptureHelper.CapturePointer(evt.target,0);
			if (target.layout.Contains(evt.localMousePosition)) return;
			
			_targetObjects = DragAndDrop.objectReferences.Select(o => o as GameObject).ToArray();
			_targetObjects.Where(o => !o.TryGetComponent(_componentType,out var component))
				.ToList().ForEach(o => {
					var component = new GameObject(o.name).AddComponent(_componentType);
					component.transform.SetParent(_tempRootObject.transform);
					component.hideFlags = HideFlags.HideAndDontSave;
					(target as ListView).itemsSource.Add(component);
				});
			_window.RepaintList();
		}
		
		private void OnDragLeave(DragLeaveEvent evt){
			evt.StopPropagation();
			Debug.Log($"Leave {evt.localMousePosition}");
			if (target.layout.Contains(evt.localMousePosition)) return;
			
			//if (DragAndDrop.GetGenericData("DragListViewItem") == null)return;
			//_targetObjects = (DragAndDrop.GetGenericData("DragListViewItem") as Object[]).Select(o => o as GameObject).ToArray();
			_targetObjects = DragAndDrop.objectReferences.Select(o => o as GameObject).ToArray();
			_targetObjects.ToList().ForEach(o => {
				if (o.TryGetComponent(_componentType,out var component))
				{
					Undo.DestroyObjectImmediate(component);
				}
			});
			Undo.IncrementCurrentGroup();
			_window.LoadList();
		}
		
		private void OnDragItem(DragUpdatedEvent evt){
			if (_window.Armature == null) return;
			if (!target.layout.Contains(evt.localMousePosition)) return;
			_targetObjects = DragAndDrop.objectReferences.Length > 0 ?
				DragAndDrop.objectReferences.Select(o => o as GameObject).ToArray() :
			(DragAndDrop.GetGenericData("DragListViewItem") as Object[]) != null ?
				(DragAndDrop.GetGenericData("DragListViewItem") as Object[]).Select(o => o as GameObject).ToArray() :
				null;
			if (_targetObjects == null) return;
			if (_targetObjects.All(o => !o.TryGetComponent(_componentType,out var component) && o.transform.IsChildOf(_window.Armature.transform)))
				DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
		}
		
		private void OnDropItem(DragPerformEvent evt){
			Debug.Log("Perform");
			_targetObjects.Where(o => 
				o.TryGetComponent(_componentType,out var component)
				&& component.hideFlags == HideFlags.HideAndDontSave
			)
				.Select(o => o.GetComponent(_componentType)).ToList()
				.ForEach(o => UnityEngine.Object.DestroyImmediate(o));
			foreach (Transform child in _tempRootObject.transform)
			{
				Object.DestroyImmediate(child.gameObject);
			}
			
			_targetObjects.Where(o => !o.TryGetComponent(_componentType,out var component))
				.All(o => Undo.AddComponent(o,_componentType));
			_window.LoadList();
			
			PointerCaptureHelper.ReleasePointer(evt.target,0);
		}
	}
}
