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
using System;

#if MODULAR_AVATAR
using nadena.dev.modular_avatar.core;
#endif

namespace colloid.PBReplacer
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
	
	#if UNITY_2021_3_OR_NEWER
	Func<VisualElement> _makeItem = () => {
		var l = new Label();
		l.AddToClassList("myitem");
		return l;
	};
	Action<VisualElement, int> _bindItem;
	List<string> _itemSource = new List<string>();
	#endif
	ListView _pblist;
	ListView _pbclist;
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
		root.StretchToParentSize();
		
		InitListView();
		
		//ApplyButtonが押された場合
		root.Query<Button>("ApplyButton").First().clicked += () => OnClickApplyBtn();
		
		//ReloadButtonが押された場合
		root.Query<Button>("ReloadButton").First().clicked += () => loadList();
		
		//オブジェクトフィールドのタイプを設定
		var avatarField = root.Q<ObjectField>("AvatarFiled");
		avatarField.objectType = typeof(VRC_AvatarDescriptor);
		avatarField.Q<VisualElement>("","unity-object-field-display").AddManipulator(new OnDragAndDropItemChange());
		#if MODULAR_AVATAR
		avatarField.Q<Label>("","unity-object-field-display__label").text = "None (VRC_Avatar Descriptor or MA Marge Armature)";
		#endif
		
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
	//ListViewの初期化
	private void InitListView()
	{
		_pblist = _root.Query<ListView>("PBListField").First();
		_pbclist = _root.Query<ListView>("PBCListField").First();
		
		#if UNITY_2019
		var pblist = new Foldout(){text = "PBList",name = "PBList"};
		var pbclist = new Foldout(){text = "PBCList",name = "PBCList"};
		
		_pblist.Add(pblist);
		_pbclist.Add(pbclist);
		#else
		var listView = _root.Q<ListView>("PBListField");
		listView.makeItem = _makeItem;
		listView.bindItem = _bindItem = (e, i) => (e as Label).text = _itemSource[i];
		listView.itemsSource = _itemSource;
		
		_pbclist.makeItem = () => {
			var l = new Label();
			l.AddToClassList("myitem");
			return l;
		};
		_pbclist.itemsSource = new List<string>();
		_pbclist.bindItem = (e, i) => (e as Label).text = (string)_pbclist.itemsSource[i];
		#endif
	}

	//ApplyButtonがクリックされた場合
	private void OnClickApplyBtn()
	{
		//Debug.Log("ボタンが押されたよ");
		if (_avatarDynamicsPrefab == null)
			_avatarDynamicsPrefab = Resources.Load<GameObject>("AvatarDynamics");
		
		if (_vrcavatar == null || _pbscript == null && _pbcscripts == null)
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
		var vrcavatar = _selectObject?.gameObject;
	
		ResetList();
		
		if (vrcavatar != null)
		{
			_vrcavatar = vrcavatar;
			//Debug.Log("Avatarをセットしたよ"+_vrcavatar);
			_root.Query<Label>("ToolBarLabel").First().text = "Applyを押してください";
			_armature = _vrcavatar.TryGetComponent<Animator>(out var vrcavatarAnimator) && vrcavatarAnimator.isHuman ?
				vrcavatarAnimator.GetBoneTransform(HumanBodyBones.Hips).parent.gameObject :
				FindArmarture();
			_pbscript = _armature?.GetComponentsInChildren<VRCPhysBone>(true);
			_pbcscripts = _armature?.GetComponentsInChildren<VRCPhysBoneCollider>(true);
			if (_pbscript == null && _pbcscripts == null) {
				_root.Query<Label>("ToolBarLabel").First().text = "Armature内にPhysBoneが見つかりません";
				return _root;
			}
			
			foreach (var item in _pbscript)
			{
				#if UNITY_2019
				var _newLineLabel = new Label(item.name);
				_newLineLabel.AddToClassList("myitem");
				var list = _root.Query<Foldout>("PBList").First();
				list.Add(_newLineLabel);
				#else
				_pblist.itemsSource.Add(item.name);
				#endif
			}
			foreach (var item in _pbcscripts)
			{
				#if UNITY_2019
				var _newLineLabel = new Label(item.name);
				_newLineLabel.AddToClassList("myitem");
				var list = _root.Query<Foldout>("PBCList").First();
				list.Add(_newLineLabel);
				#else
				_pbclist.itemsSource.Add(item.name);
				#endif
			}
		}
		else
		{
			_vrcavatar = null;
			//Debug.Log("Avatarを外したよ");
			_root.Query<Label>("ToolBarLabel").First().text = "アバターをセットしてください";
			
			#if MODULAR_AVATAR
			_root.Q<Label>("","unity-object-field-display__label").text = 
				"None (VRC_Avatar Descriptor or MA Marge Armature)";
			#endif
		}
		
		#if UNITY_2021_3_OR_NEWER
		_pblist.Rebuild();
		_pbclist.Rebuild();
		#endif
		
		return _root;
	}
	
	//リスト表示のリセット
	private void ResetList()
	{
		#if UNITY_2019
		while (_root.Query<VisualElement>(null,"myitem").Last() != null)
		{
			var element = _root.Query<VisualElement>(null,"myitem").Last();
			element.parent.Remove(element);
		}
		#else
		_pblist.itemsSource.Clear();
		_pbclist.itemsSource.Clear();
		#endif
	}
	
	//Avatar内からArmatureを検出
	private GameObject FindArmarture()
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
		return _armature;
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

}

}