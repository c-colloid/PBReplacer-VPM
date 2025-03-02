using System;
using System.Linq;
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
	private List<Component> _pbscripts = new List<Component>();
	private List<Component> _pbcscripts = new List<Component>();
	private List<Component> _temppbscripts = new List<Component>();
	private List<Component> _temppbcscripts = new List<Component>();
	private GameObject _vrcavatar;
	private GameObject _armature;
	[SerializeField]
	private GameObject _avatarDynamicsPrefab;
	private GameObject _avatarDynamicsObject;
	private Component _selectObject;
	private List<VRCPhysBoneColliderBase> _colliders;
	
	Func<VisualElement> _makeItem = () => {
		var l = new Label();
		l.AddToClassList(_listItemClassName);
		return l;
	};
	Action<VisualElement, int> _bindItem;
	List<string> _itemSource;
	ListView _pblist;
	ListView _pbclist;
	OnListViewDragAndDropItemChange _pbManipulator,_pbcManipulator;
	
	const string _avatarFieldDefaultTextwithMA = "None (VRC_Avatar Descriptor or MA Setuped Object)";
	const string _listItemClassName = "listitem";
#endregion

#region Propaty
	public List<VRCPhysBone> PBscripts {
		get => _pbscripts.Select(o => o as VRCPhysBone).ToList();
		set => _pbscripts = value.Select(o => o as Component).ToList();
	}
	public List<VRCPhysBoneCollider> PBCscripts {
		get => _pbcscripts.Select(o => o as VRCPhysBoneCollider).ToList();
		set => _pbcscripts = value.Select(o => o as Component).ToList();
	}
	public GameObject Armature { get => _armature; set => _armature = value; }
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
		//wnd.minSize = new Vector2(300,200);
		//wnd.position = new Rect(0,0,0,0);
	}
	
	public static void ShowSelectedWindow()
	{
		ShowWindow();
		var wnd = GetWindow<PBReplacer>();
		wnd.rootVisualElement.Q<ObjectField>("AvatarFiled").value = Selection.activeGameObject;
		Debug.Log(wnd.rootVisualElement.Q<ObjectField>("AvatarFiled").value);
	}
	
	private void OnEnable()
	{
		//初期化
		_vrcavatar = null;
	}
	
	private void OnDisable()
	{
		//_pbManipulator.OnDisable();
		//_pbcManipulator.OnDisable();
		_pblist.RemoveManipulator(_pbManipulator);
		_pbclist.RemoveManipulator(_pbcManipulator);
	}
	
	private void OnDestroy()
	{
		
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
		root.Query<Button>("ReloadButton").First().clicked += () => LoadList();
		
		//オブジェクトフィールドのタイプを設定
		var avatarField = root.Q<ObjectField>("AvatarFiled");
		avatarField.objectType = typeof(VRC_AvatarDescriptor);
		avatarField.Q<VisualElement>("","unity-object-field-display").AddManipulator(new OnAvatarFieldDragAndDropItemChange());
		#if MODULAR_AVATAR
		avatarField.Q<Label>("","unity-object-field-display__label").text = _avatarFieldDefaultTextwithMA;
		#endif
		
		//オブジェクトフィールドが更新された場合
		avatarField.RegisterValueChangedCallback(evt =>
		{
			_selectObject = evt.newValue as Component;
			root = LoadList();
		});
		
		var sideBar = root.Q<VerticalTabContainer>();
		sideBar.value = 0;
		
		sideBar.RegisterValueChangedCallback(evt => 
		{
			root.Query<Box>().ForEach(b => b.style.display = DisplayStyle.None);
			root.Query<Box>().ToList().ElementAt(evt.newValue).style.display = DisplayStyle.Flex;
		});
		
		Undo.undoRedoPerformed += () => LoadList();
		
		rootVisualElement.Add(root);
	}
#endregion

#region Methods/Other Methods
	//ListViewの初期化
	private void InitListView()
	{
		_pblist = _root.Query<ListView>("PBListField").First();
		_pbclist = _root.Query<ListView>("PBCListField").First();
		
		_pbManipulator = new OnListViewDragAndDropItemChange();
		_pbcManipulator = new OnListViewDragAndDropItemChange();
		_pblist.AddManipulator(_pbManipulator);
		_pbclist.AddManipulator(_pbcManipulator);
		//VisualElementExtensions.AddManipulator(_pblist,new OnListViewDragAndDropItemChange());
		//VisualElementExtensions.AddManipulator(_pbclist,new OnListViewDragAndDropItemChange());
		
		#if UNITY_2019
		var pblist = new Foldout(){text = "PBList",name = "PBList",style = {flexGrow = 1}};
		var pbclist = new Foldout(){text = "PBCList",name = "PBCList",style = {flexGrow = 1}};
		
		pblist.contentContainer.style.flexGrow = 1;
		pblist.contentContainer.style.marginLeft = 1;
		pbclist.contentContainer.style.flexGrow = 1;
		pbclist.contentContainer.style.marginLeft = 1;
		
		_pblist.parent.Insert(0,pblist);
		pblist.Add(_pblist);
		_pbclist.parent.Add(pbclist);
		pbclist.Add(_pbclist);
		#else
		#endif
				
		bool isSelectList = false;
		UnityEngine.Object[] select = null;
		void BindListView(ListView listview, List<Component> list)
		{
			listview.itemsSource = list;
			listview.makeItem = _makeItem;
			listview.bindItem = (e,i) => {
				(e as Label).text = (listview.itemsSource[i] as Component).name;
				e.RegisterCallback<PointerDownEvent>(evt => {
					DragAndDrop.PrepareStartDrag();
					
				});
			};
			listview.selectionType = SelectionType.Multiple;
		}
		BindListView(_pblist,_pbscripts);
		BindListView(_pbclist,_pbcscripts);
		
		void SelectList(List<object> obj)
		{
			select = obj.Select(t => (t as Component).gameObject).ToArray() as UnityEngine.Object[];
			isSelectList = true;
		}
		#if UNITY_2019
		_pblist.onSelectionChanged += o => SelectList(o);
		_pbclist.onSelectionChanged += o => SelectList(o);
		#else
		_pblist.selectionChanged += o => SelectList(o.ToList());
		_pbclist.selectionChanged += o => SelectList(o.ToList());
		#endif
		
		void ListOnDrag(PointerMoveEvent evt)
		{
			if (Event.current.type != EventType.MouseDrag) return;
			if (!isSelectList) return;
			Debug.Log("Drag");
			//DragAndDrop.SetGenericData("DragListViewItem",select);
			DragAndDrop.objectReferences = select;
			DragAndDrop.StartDrag(string.Empty);
			isSelectList = false;
			
			PointerCaptureHelper.CapturePointer(evt.target,evt.pointerId);
		}
		_pblist.RegisterCallback<PointerMoveEvent>(ListOnDrag);
		_pbclist.RegisterCallback<PointerMoveEvent>(ListOnDrag);
		
		void SelectListMouseUp(PointerUpEvent evt)
		{
			PointerCaptureHelper.ReleasePointer(evt.target,evt.pointerId);
			
			if (!isSelectList) return;
			Selection.objects = select;
			isSelectList = false;
		}
		_pblist.RegisterCallback<PointerUpEvent>(SelectListMouseUp);
		_pbclist.RegisterCallback<PointerUpEvent>(SelectListMouseUp);
		
		_pblist.RegisterCallback<PointerCaptureOutEvent>(evt => {
			Debug.Log("Capture is Out");
		});
	}
	
	//ApplyButtonがクリックされた場合
	private void OnClickApplyBtn()
	{
		//Debug.Log("ボタンが押されたよ");
		if (_avatarDynamicsPrefab == null)
			_avatarDynamicsPrefab = Resources.Load<GameObject>("AvatarDynamics");
		
		if (_vrcavatar == null || _pbscripts == null && _pbcscripts == null)
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
		LoadList();
		
		_root.Query<Label>("ToolBarLabel").First().text = "完了!!!";
	}
	
	//リスト表示
	public TemplateContainer LoadList()
	{
		var vrcavatar = _selectObject?.gameObject;
	
		ResetList();
		
		if (vrcavatar != null)
		{
			_vrcavatar = vrcavatar;
			//Debug.Log("Avatarをセットしたよ"+_vrcavatar);
			_root.Query<Label>("ToolBarLabel").First().text = "Applyを押してください";
			Animator vrcavatarAnimator;
			_armature = _vrcavatar.TryGetComponent<Animator>(out vrcavatarAnimator) &&
				vrcavatarAnimator.GetBoneTransform(HumanBodyBones.Hips) ?
				vrcavatarAnimator.GetBoneTransform(HumanBodyBones.Hips).parent.gameObject :
				FindArmarture();
				
			_pbscripts.AddRange(_armature?.GetComponentsInChildren<VRCPhysBone>(true));
			_pbcscripts.AddRange(_armature?.GetComponentsInChildren<VRCPhysBoneCollider>(true));
			if (_pbscripts.Count <= 0 && _pbcscripts.Count <= 0) {
				_root.Query<Label>("ToolBarLabel").First().text = "Armature内にPhysBoneが見つかりません";
				
				RepaintList();
				
				return _root;
			}
			
			/*
			foreach (var item in _pbscripts)
			{
				#if UNITY_2019
				//AddLabel(item.gameObject,"PBList");
				#else
				//_pblist.itemsSource.Add(item.name);
				#endif
			}
			foreach (var item in _pbcscripts)
			{
				#if UNITY_2019
				//AddLabel(item.gameObject,"PBCList");
				#else
				//_pbclist.itemsSource.Add(item.name);
				#endif
			}
			*/
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
		
		RepaintList();
		
		return _root;
	}
	
	public void RepaintList()
	{
		#if UNITY_2019
		_pblist.Refresh();
		_pbclist.Refresh();
		#elif UNITY_2021_3_OR_NEWER
		_pblist.Rebuild();
		_pbclist.Rebuild();
		#endif
	}
	
	//リスト表示のリセット
	private void ResetList()
	{
		/*
		#if UNITY_2019
		//while (_root.Query<VisualElement>(null,_listItemClassName).Last() != null)
		//{
		//	var element = _root.Query<VisualElement>(null,_listItemClassName).Last();
		//	element.parent.Remove(element);
		//}
		#else
		#endif
		*/
		_pblist.itemsSource.Clear();
		_pbclist.itemsSource.Clear();
		
		RepaintList();
	}
	
	//private void AddLabel(GameObject target, string targetTypeName)
	//{
	//	var newLineLabel = new Label(target.name);
	//	newLineLabel.AddToClassList(_listItemClassName);
	//	newLineLabel.focusable = true;
	//	newLineLabel.RegisterCallback<MouseDownEvent>(evt => {
	//		Selection.activeGameObject = target;
	//		newLineLabel.Focus();
	//	});
	//	var list = _root.Query<Foldout>(targetTypeName).First();
	//	list.Add(newLineLabel);
	//}
	
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
		
		Undo.RegisterCreatedObjectUndo(_avatarDynamicsObject,"生成したAvatarDynamics");
	}
	
	//PBを再配置
	private void ReplacePB() // VRCPhysBone
	{
		foreach (VRCPhysBone item in _pbscripts)
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
			Undo.DestroyObjectImmediate(item);
		}
	}
	private void ReplacePBC() // VRCPhysBoneCollider
	{
		foreach (VRCPhysBoneCollider item in _pbcscripts)
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
			
			Type type = item.GetType();
			
			FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
			foreach (var field in fields)
			{
				//if (FieldInfo.Name == "roottransform")continue;
				
				field.SetValue(newphysbonecollider, field.GetValue(item));
			}
			
			//PBのコライダーを付け替え
			foreach (VRCPhysBone pb in _pbscripts) // VRCPhysBone
			{
				var index = pb.colliders.IndexOf(item);
				if (index >= 0)
				{
					pb.colliders[index] = newphysbonecollider;
				}
				
			}
			
			Undo.DestroyObjectImmediate(item);
		}
	}
#endregion

}

}