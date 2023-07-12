using System.Runtime.Versioning;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;
using VRC.Dynamics;
using System.Linq;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.EditorExpression
{
	
public class PBReplacer : EditorWindow
{
#region Variables
	[SerializeField]
	private VisualTreeAsset _tree;
	private TemplateContainer _root;
	private VRCPhysBone[] _pbscript;
	private VRCPhysBoneCollider[] _pbcscripts;
	private GameObject _vrcavatar;
	private GameObject _armature;
	[SerializeField]
	private GameObject _avatarDynamicsPrefab;
	private GameObject _avatarDynamicsObject;
	private Component _selectObject;
	private List<VRCPhysBoneColliderBase> _colliders;
#endregion

#region Methods/Unity Methods
	//メニュー追加
	[MenuItem("Tools/PBReplacer")][MenuItem("GameObject/PBReplacer",false,25)]
	public static void ShowWindow()
	{
		//ウィンドウ作成
		var wnd = GetWindow<PBReplacer>();
		//ウィンドウのタイトル
		wnd.titleContent = new GUIContent("PBReplacer");
		
		//エラーでウィンドウが見つけられなくなった時に初期値に戻すために使用
		//wnd.minSize = new Vector2(600,400);
		//wnd.position = new Rect(0,0,0,0);
	}
	
	private void OnEnable()
	{
		//初期化
		_vrcavatar = null;
	}
	
	private void CreateGUI()
	{
		//UXMLからツリー構造を読み取り
		if (_tree == null)
			_tree = Resources.Load<VisualTreeAsset>("PBReplacer");
		var root = _tree.CloneTree();
		_root = root;
		
		//ApplyButtonが押された場合
		root.Query<Button>("ApplyButton").First().clicked += () => OnClickApplyBtn();
		
		//ReloadButtonが押された場合
		root.Query<Button>("ReloadButton").First().clicked += () => loadList();
		
		//オブジェクトフィールドのタイプを設定
		var avatarField = root.Q<ObjectField>("AvatarFiled");
		avatarField.objectType = typeof(VRC_AvatarDescriptor);
		avatarField.Q<VisualElement>("","unity-object-field-display").AddManipulator(new OnDragAndDropItemChange());
		avatarField.Q<Label>("","unity-object-field-display__label").text = "None (VRC_Avatar Descriptor or MA Marge Armature)";
		
		//オブジェクトフィールドが更新された場合
		avatarField.RegisterValueChangedCallback(evt =>
		{
			_selectObject = evt.newValue as Component;
			root = loadList();
		});
		
		rootVisualElement.Add(root);
	}
#endregion

#region Methods/Other Methods
	//ApplyButtonがクリックされた場合
	private void OnClickApplyBtn()
	{
		//Debug.Log("ボタンが押されたよ");
		if (_avatarDynamicsPrefab == null)
			_avatarDynamicsPrefab = Resources.Load<GameObject>("AvatarDynamics");
		
		if (_vrcavatar == null)
		{
			return;
		}
		if (_vrcavatar.transform.Find("AvatarDynamics") != null)
		{
			//_root.Query<Label>("ToolBarLabel").First().text = "既に実行しています。他のアバターを使用してください。";
			//return;
		}
		
		PlacePrefab();
		ReplacePBC();
		ReplacePB();
		
		_root.Query<Label>("ToolBarLabel").First().text = "完了!!!";
	}
	
	//リスト表示
	private TemplateContainer loadList()
	{
		ResetList();
	
		var vrcavatar = _selectObject?.gameObject;
		if (vrcavatar != null)
		{
			_vrcavatar = vrcavatar;
			//Debug.Log("Avatarをセットしたよ"+_vrcavatar);
			_root.Query<Label>("ToolBarLabel").First().text = "Applyを押してください";
			FindArmarture();
			_pbscript = _armature.GetComponentsInChildren<VRCPhysBone>(true);
			_pbcscripts = _armature.GetComponentsInChildren<VRCPhysBoneCollider>(true);
			
			foreach (var item in _pbscript)
			{
				var _newLineLabel = new Label(item.name);
				_newLineLabel.AddToClassList("myitem");
				var list = _root.Query<Foldout>("PBList").First();
				list.Add(_newLineLabel);
			}
			foreach (var item in _pbcscripts)
			{
				var _newLineLabel = new Label(item.name);
				_newLineLabel.AddToClassList("myitem");
				var list = _root.Query<Foldout>("PBCList").First();
				list.Add(_newLineLabel);
			}
		}
		else
		{
			_vrcavatar = null;
			//Debug.Log("Avatarを外したよ");
			_root.Query<Label>("ToolBarLabel").First().text = "アバターをセットしてください";
			_root.Q<ObjectField>().Q<Label>("","unity-object-field-display__label").text = "None (VRC_Avatar Descriptor or MA Marge Armature)";
		}
		
		return _root;
	}
	
	//リスト表示のリセット
	private void ResetList()
	{
		ListView foldout = _root.Query<ListView>("PBListField").First();
		while (foldout.Query<VisualElement>(null,"myitem").Last() != null)
		{
			var element = foldout.Query<VisualElement>(null,"myitem").Last();
			element.parent.Remove(element);
		}
	}
	
	//Avatar内からArmatureを検出
	private void FindArmarture()
	{
		_armature = null;
		IEnumerable<Transform> avatarDynamicsobjs = null;
		
		//AvatarDynamicsが既に分けられて要る場合、検索範囲から除外してArmatureを検出
		if (_vrcavatar.transform.Find("AvatarDynamics") != null)
		{
			_avatarDynamicsObject = _vrcavatar.transform.Find("AvatarDynamics").gameObject;
			avatarDynamicsobjs = _avatarDynamicsObject.GetComponentsInChildren<Transform>();
		}
    	foreach (var item in _vrcavatar.GetComponentsInChildren<Transform>())
    	{
    		if (item == _vrcavatar.transform) continue;
    		if (avatarDynamicsobjs != null && avatarDynamicsobjs.Contains<Transform>(item)) continue;
    		if (!_armature) _armature = item.gameObject;
    		var length = item.GetComponentsInChildren<Transform>().Length;
    		if (length > _armature.GetComponentsInChildren<Transform>().Length)
    		{
    			_armature = item.gameObject;
    		}
    	}
	}
#endregion
	
#region Methods/replacePB Methods
	//プレファブをアバターにセット
	private void PlacePrefab()
	{
		if (_vrcavatar.transform.Find("AvatarDynamics") != null)
		{
			_avatarDynamicsObject = _vrcavatar.transform.Find("AvatarDynamics").gameObject;
			return;
		}
		_avatarDynamicsObject = PrefabUtility.InstantiatePrefab(_avatarDynamicsPrefab) as GameObject;
		_avatarDynamicsObject.transform.SetParent(_vrcavatar.transform);
		_avatarDynamicsObject.transform.localPosition = Vector3.zero;
		//Debug.Log("Prefabを配置");
	}
	
	//PBを再配置
	private void ReplacePB()
	{
		foreach (var item in _pbscript)
		{
			if (item.rootTransform == null)
			{
				item.rootTransform = item.transform;
			}

			GameObject obj = new GameObject(item.rootTransform.name);
			Transform physBones = _avatarDynamicsObject.transform.Find("PhysBones");
			obj.transform.SetParent(physBones);
			if (item.rootTransform != item.transform)
			{
				if (physBones.transform.Find(item.name) == null)
				{
					GameObject objParent = new GameObject(item.name);
					objParent.transform.SetParent(physBones);
					objParent.transform.localPosition = Vector3.zero;
					obj.transform.SetParent(objParent.transform);
				}
				else
				{
					obj.transform.SetParent(physBones.Find(item.name));
				}
				
			}

			obj.transform.localPosition = Vector3.zero;
			
			var newphysbone = obj.AddComponent<VRCPhysBone>();
			
			System.Type type = item.GetType();
			
			FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
			foreach (var field in fields)
			{
				field.SetValue(newphysbone, field.GetValue(item));
			}
			DestroyImmediate(item);
		}
	}
	private void ReplacePBC()
	{
		foreach (var item in _pbcscripts)
		{
			if (item.rootTransform == null)
			{
				item.rootTransform = item.transform;
			}

			GameObject obj = new GameObject(item.rootTransform.name);
			Transform physBoneColliders = _avatarDynamicsObject.transform.Find("PhysBoneColliders");
			obj.transform.SetParent(physBoneColliders);
			if (item.rootTransform != item.transform)
			{
				if (physBoneColliders.transform.Find(item.name) == null)
				{
					GameObject objParent = new GameObject(item.name);
					objParent.transform.SetParent(physBoneColliders);
					objParent.transform.localPosition = Vector3.zero;
					obj.transform.SetParent(objParent.transform);
				}
				else
				{
					obj.transform.SetParent(physBoneColliders.Find(item.name));
				}
				
			}

			obj.transform.localPosition = Vector3.zero;
			
			var newphysbonecollider = obj.AddComponent<VRCPhysBoneCollider>();
			
			System.Type type = item.GetType();
			
			FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
			foreach (var field in fields)
			{
				//if (FieldInfo.Name == "roottransform")continue;
				
				field.SetValue(newphysbonecollider, field.GetValue(item));
			}
			
			//PBのコライダーを付け替え
			foreach (var pb in _pbscript)
			{
				var index = pb.colliders.IndexOf(item);
				if (index >= 0)
				{
					pb.colliders[index] = newphysbonecollider;
				}
				
			}
			
			DestroyImmediate(item);
		}
	}
#endregion

#region Manipulator
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
				var window = GetWindow<PBReplacer>();
				window._root.Q<ObjectField>().value = targetObject.transform;
			}
		}
	}
#endregion
}

}