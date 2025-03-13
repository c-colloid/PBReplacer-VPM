using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// リストビューのドラッグ&ドロップ操作を処理するクラス
	/// </summary>
	public class ListViewDragHandler
	{
        #region Variables
		// 操作対象のリストビュー
		private ListView _listView;
        
		// ドラッグ対象のコンポーネントの種類
		private Type _componentType;
		
		private Label _dropArea;
        
		// 一時オブジェクトのルート（ドラッグ&ドロップ処理用）
		private GameObject _tempRootObject;
        
		// ドラッグ中フラグ
		private bool _isDragging;
        
		// 選択中アイテムのキャッシュ
		private List<UnityEngine.Object> _selectedObjects = new List<UnityEngine.Object>();
        
		// データマネージャへの参照
		private PhysBoneDataManager _dataManager => PhysBoneDataManager.Instance;
		
		// イベントの多重発生を防止
		private bool _event = false;
        #endregion

        #region Events
		// ドラッグ開始イベント
		public event Action<List<GameObject>> OnDragStart;
        
		// ドラッグ終了イベント
		public event Action<List<GameObject>> OnDragEnd;
        
		// ドラッグエンターイベント
		public event Action<List<GameObject>> OnDragEnter;
        
		// ドラッグリーブイベント
		public event Action OnDragLeave;
        
		// ドロップイベント
		public event Action<List<GameObject>> OnDrop;
        #endregion

        #region Constructor & Cleanup
		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="listView">ドラッグ操作を登録するリストビュー</param>
		/// <param name="componentType">ドラッグ対象のコンポーネントの型</param>
		public ListViewDragHandler(ListView listView, Type componentType)
		{
			_listView = listView;
			_componentType = componentType;
			
			var dropAreaElement = _dropArea = new Label("DropArea")
			{
				name = "dropArea",
				//pickingMode = PickingMode.Ignore,
				style = 
				{
					flexGrow = 1,
					backgroundColor = Color.gray * 0.25f,
					unityTextAlign = TextAnchor.MiddleCenter,
					fontSize = 20,
					unityFontStyleAndWeight = FontStyle.Bold,
					color = Color.gray,
					visibility = Visibility.Hidden
				}
			};
			_listView.parent.Q<VisualElement>("DropAreaContainer").Add(dropAreaElement);
            
			// 一時オブジェクトの作成
			_tempRootObject = new GameObject("ListViewTempRoot");
			_tempRootObject.hideFlags = HideFlags.HideAndDontSave;
            
			// イベント登録
			RegisterEvents();
		}

		/// <summary>
		/// クリーンアップ
		/// </summary>
		public void Cleanup()
		{
			// イベント登録解除
			UnregisterEvents();
            
			// 一時オブジェクトの削除
			if (_tempRootObject != null)
			{
				UnityEngine.Object.DestroyImmediate(_tempRootObject);
				_tempRootObject = null;
			}
		}
        #endregion

        #region Event Registration
		/// <summary>
		/// イベントハンドラの登録
		/// </summary>
		private void RegisterEvents()
		{
			// 重複登録の防止
			UnregisterEvents();
			
			// ポインタイベントの登録
			//_dropArea.RegisterCallback<PointerDownEvent>(HandlePointerDown);
			//_dropArea.RegisterCallback<PointerMoveEvent>(HandlePointerMove);
			//_dropArea.RegisterCallback<PointerUpEvent>(HandlePointerUp);
			
			AvatarFieldHelper.OnAvatarChanged += OnAvatarChanged;
            
			// ドラッグイベントの登録
			_listView.RegisterCallback<DragEnterEvent>(HandleDragEnterEvent);
			_dropArea.RegisterCallback<DragLeaveEvent>(HandleDragLeaveEvent);
			_dropArea.RegisterCallback<DragUpdatedEvent>(HandleDragUpdatedEvent);
			_dropArea.RegisterCallback<DragPerformEvent>(HandleDragPerformEvent);
		}

		/// <summary>
		/// イベントハンドラの登録解除
		/// </summary>
		private void UnregisterEvents()
		{
			if (_listView == null) return;
            
			// ポインタイベントの登録解除
			//_dropArea.UnregisterCallback<PointerDownEvent>(HandlePointerDown);
			//_dropArea.UnregisterCallback<PointerMoveEvent>(HandlePointerMove);
			//_dropArea.UnregisterCallback<PointerUpEvent>(HandlePointerUp);
			
			AvatarFieldHelper.OnAvatarChanged -= OnAvatarChanged;
            
			// ドラッグイベントの登録解除
			_listView.UnregisterCallback<DragEnterEvent>(HandleDragEnterEvent);
			_dropArea.UnregisterCallback<DragLeaveEvent>(HandleDragLeaveEvent);
			_dropArea.UnregisterCallback<DragUpdatedEvent>(HandleDragUpdatedEvent);
			_dropArea.UnregisterCallback<DragPerformEvent>(HandleDragPerformEvent);
		}
        #endregion

        #region Pointer Event Handlers
		/// <summary>
		/// ポインタダウンイベントの処理
		/// </summary>
		private void HandlePointerDown(PointerDownEvent evt)
		{
			// ドラッグ開始準備
			_isDragging = false;
            
			// 選択されたアイテムの状態を記録
			CacheSelectedItems();
		}

		/// <summary>
		/// ポインタ移動イベントの処理
		/// </summary>
		private void HandlePointerMove(PointerMoveEvent evt)
		{
			// マウスドラッグが開始されたかチェック
			if (Event.current != null && Event.current.type == EventType.MouseDrag && !_isDragging)
			{
				// 選択アイテムがあればドラッグ開始
				if (_selectedObjects.Count > 0)
				{
					StartDrag();
					evt.StopPropagation();
				}
			}
		}

		/// <summary>
		/// ポインタアップイベントの処理
		/// </summary>
		private void HandlePointerUp(PointerUpEvent evt)
		{
			if (_isDragging)
			{
				EndDrag();
			}
            
			_isDragging = false;
		}
        #endregion

        #region Drag Event Handlers
		/// <summary>
		/// ドラッグエンターイベントの処理
		/// </summary>
		private void HandleDragEnterEvent(DragEnterEvent evt)
		{
			// イベントの同レーム内の発生を1回に制限
			if (_event) return;
			EditorApplication.delayCall += () => _event = false;
			_event = true;
			
			VisibleDropArea();
			
			// ドラッグ中のオブジェクト参照を取得
			var draggedObjects = GetDraggedObjects();
			if (draggedObjects.Count == 0) return;
            
			// イベント通知
			OnDragEnter?.Invoke(draggedObjects);
            
			// 一時的なコンポーネントを追加
			AddTemporaryComponents(draggedObjects);
            
			// リストの更新をリクエスト
			RefreshListView();
            
			evt.StopPropagation();
		}

		/// <summary>
		/// ドラッグリーブイベントの処理
		/// </summary>
		private void HandleDragLeaveEvent(DragLeaveEvent evt)
		{
			HiddenDropArea();
			
			// 一時的なコンポーネントをクリーンアップ
			CleanupTemporaryComponents();
            
			// イベント通知
			OnDragLeave?.Invoke();
            
			// リストの更新をリクエスト
			RefreshListView();
            
			evt.StopPropagation();
		}

		/// <summary>
		/// ドラッグ更新イベントの処理
		/// </summary>
		private void HandleDragUpdatedEvent(DragUpdatedEvent evt)
		{
			// ドラッグされているオブジェクトをチェック
			var draggedObjects = GetDraggedObjects();
			if (draggedObjects.Count > 0)
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
			}
			else
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
			}
            
			evt.StopPropagation();
		}

		/// <summary>
		/// ドラッグ実行イベントの処理
		/// </summary>
		private void HandleDragPerformEvent(DragPerformEvent evt)
		{
			HiddenDropArea();
			
			// ドラッグされているオブジェクトを取得
			var draggedObjects = GetDraggedObjects();
			if (draggedObjects.Count == 0) return;
            
			// 実際のコンポーネントをアタッチする
			AttachComponentsToObjects(draggedObjects);
            
			// 一時的なコンポーネントをクリーンアップ
			CleanupTemporaryComponents();
            
			// イベント通知
			OnDrop?.Invoke(draggedObjects);
            
			// データマネージャにリロードをリクエスト
			_dataManager.ReloadData();
            
			evt.StopPropagation();
		}
        #endregion

        #region Helper Methods
		private void OnAvatarChanged(AvatarData obj)
		{
			if (obj != null)
			{
				// 一時オブジェクトの作成
				//_tempRootObject = new GameObject("ListViewTempRoot");
				//_tempRootObject.hideFlags = HideFlags.HideAndDontSave;
			}
			else
			{
				//CleanupTemporaryComponents();
			}
		}
        
		/// <summary>
		/// 選択されたアイテムを内部キャッシュに保存
		/// </summary>
		private void CacheSelectedItems()
		{
			_selectedObjects.Clear();
            
			if (_listView.selectedIndices.ToList().Count == 0) return;
            
			foreach (int index in _listView.selectedIndices)
			{
				if (index < _listView.itemsSource.Count)
				{
					var component = _listView.itemsSource[index] as Component;
					if (component != null)
					{
						_selectedObjects.Add(component.gameObject);
					}
				}
			}
		}

		/// <summary>
		/// ドラッグを開始
		/// </summary>
		private void StartDrag()
		{
			if (_selectedObjects.Count == 0) return;
            
			_isDragging = true;
            
			// ドラッグ操作を準備
			DragAndDrop.PrepareStartDrag();
			DragAndDrop.objectReferences = _selectedObjects.ToArray();
			DragAndDrop.StartDrag("Drag Items");
            
			// イベント通知
			var gameObjects = _selectedObjects.OfType<GameObject>().ToList();
			OnDragStart?.Invoke(gameObjects);
		}

		/// <summary>
		/// ドラッグを終了
		/// </summary>
		private void EndDrag()
		{
			if (!_isDragging) return;
            
			_isDragging = false;
            
			// ドラッグ操作をクリア
			DragAndDrop.PrepareStartDrag();
            
			// イベント通知
			var gameObjects = _selectedObjects.OfType<GameObject>().ToList();
			OnDragEnd?.Invoke(gameObjects);
		}

		/// <summary>
		/// ドラッグされているオブジェクトを取得
		/// </summary>
		private List<GameObject> GetDraggedObjects()
		{
			if (DragAndDrop.objectReferences.Length == 0) return new List<GameObject>();
            
			return DragAndDrop.objectReferences
				.OfType<GameObject>()
				.ToList();
		}

		/// <summary>
		/// 一時的なコンポーネントを追加
		/// </summary>
		private void AddTemporaryComponents(List<GameObject> objects)
		{
			foreach (var obj in objects)
			{
				if (obj == null || _tempRootObject.transform.childCount > 0) continue;
                
				// 既にコンポーネントがあるかチェック
				if (obj.GetComponent(_componentType) != null) continue;
                
				// 一時的なゲームオブジェクトを作成
				var tempObj = new GameObject(obj.name);
				tempObj.transform.SetParent(_tempRootObject.transform);
				tempObj.hideFlags = HideFlags.HideAndDontSave;
                
				// コンポーネントを追加
				var component = tempObj.AddComponent(_componentType);
                
				var listSource = _listView.itemsSource as List<Component>;
				// リストのアイテムソースに追加
				if (listSource != null)
				{
					listSource.Add(component);
				}
			}
		}

		/// <summary>
		/// 一時的なコンポーネントをクリーンアップ
		/// </summary>
		private void CleanupTemporaryComponents()
		{
			if (_tempRootObject == null) return;
            
			// 一時的なコンポーネントをリストから削除
			var listSource = _listView.itemsSource as List<Component>;
			if (listSource != null)
			{
				for (int i = listSource.Count - 1; i >= 0; i--)
				{
					var component = listSource[i];
					if (component != null && component.transform != null && component.transform.parent == _tempRootObject.transform)
					{
						listSource.RemoveAt(i);
					}
				}
			}
            
			// 一時的なオブジェクトを削除
			foreach (Transform child in _tempRootObject.transform)
			{
				UnityEngine.Object.DestroyImmediate(child.gameObject);
			}
		}

		/// <summary>
		/// オブジェクトに実際のコンポーネントを追加
		/// </summary>
		private void AttachComponentsToObjects(List<GameObject> objects)
		{
			// Undoグループを開始
			Undo.SetCurrentGroupName($"Add {_componentType.Name}");
			int undoGroup = Undo.GetCurrentGroup();
            
			foreach (var obj in objects)
			{
				if (obj == null) continue;
                
				// 既にコンポーネントがあるかチェック
				if (obj.GetComponent(_componentType) != null) continue;
                
				// コンポーネントを追加 (Undo対応)
				Undo.AddComponent(obj, _componentType);
                
				// PhysBoneコンポーネントの場合は追加設定
				if (_componentType == typeof(VRCPhysBone))
				{
					var physBone = obj.GetComponent<VRCPhysBone>();
					if (physBone != null)
					{
						physBone.rootTransform = obj.transform;
					}
				}
				else if (_componentType == typeof(VRCPhysBoneCollider))
				{
					var collider = obj.GetComponent<VRCPhysBoneCollider>();
					if (collider != null)
					{
						collider.rootTransform = obj.transform;
					}
				}
			}
            
			// Undoグループを終了
			Undo.CollapseUndoOperations(undoGroup);
		}

		/// <summary>
		/// リストビューを更新
		/// </summary>
		private void RefreshListView()
		{
            #if UNITY_2019
			_listView.Refresh();
            #elif UNITY_2021_3_OR_NEWER
			_listView.Rebuild();
            #else
			_listView.Refresh();
            #endif
		}
		
		private void VisibleDropArea()
		{
			_dropArea.style.visibility = Visibility.Visible;
		}
		
		private void HiddenDropArea()
		{
			_dropArea.style.visibility = Visibility.Hidden;
		}
        #endregion
	}
}