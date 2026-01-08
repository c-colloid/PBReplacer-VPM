using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.Dynamics;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerWindow - ListView関連部分
	/// </summary>
	public partial class PBReplacerWindow
	{
		#region ListView Initialization
		/// <summary>
		/// リストビューの初期化
		/// </summary>
		private void InitializeListViews()
		{
			// PhysBoneリストビューの初期化
			InitializeListView(_pbListView, "PhysBone");

			// PhysBoneColliderリストビューの初期化
			InitializeListView(_pbcListView, "PhysBoneCollider");

			// Constraint群リストビューの初期化
			_constraintListViewList.ForEach(listview => InitializeListView(listview,"constraint"));

			// Contactリストビューの初期化
			InitializeListView(_contactSenderListView, "ContactSender");
			InitializeListView(_contactReciverListView, "ContactReciver");

			// ドラッグ&ドロップハンドラーの作成
			InitializeDragHandlers();
		}

		/// <summary>
		/// ドラッグ&ドロップハンドラーを初期化
		/// </summary>
		private void InitializeDragHandlers()
		{
			_pbListDragHandler = new ListViewDragHandler(_pbListView, typeof(VRCPhysBone));
			_pbcListDragHandler = new ListViewDragHandler(_pbcListView, typeof(VRCPhysBoneCollider));
			_constraintDragHandlerList = new List<ListViewDragHandler>();
			_constraintListViewList.Select((list,index)=>(list,index)).ToList().ForEach(item =>
			{
				var type =
					item.index == 0 ? typeof(VRCPositionConstraint) :
					item.index == 1 ? typeof(VRCRotationConstraint) :
					item.index == 2 ? typeof(VRCScaleConstraint) :
					item.index == 3 ? typeof(VRCParentConstraint) :
					item.index == 4 ? typeof(VRCLookAtConstraint) :
					typeof(VRCAimConstraint);
				_constraintDragHandlerList.Add(new ListViewDragHandler(item.list, type));
			});
			_contactSenderDragHandler = new ListViewDragHandler(_contactSenderListView, typeof(VRCContactSender));
			_contactReciverDragHandler = new ListViewDragHandler(_contactReciverListView, typeof(VRCContactReceiver));
		}

		/// <summary>
		/// 単一のリストビューの初期化
		/// </summary>
		private void InitializeListView(ListView listView, string itemType)
		{
			listView.itemsSource = new List<Component>();

			// 要素作成コールバック
			listView.makeItem = () => {
				var label = new Label();
				label.AddToClassList(LIST_ITEM_CLASS_NAME);
				label.focusable = true;
				label.AddManipulator(new ContextualMenuManipulator(evt => {
					var target = label.userData as Component;
					evt.menu.AppendAction("Delete", action => {
						UnityEditor.Undo.DestroyObjectImmediate(target);
						DataManagerHelper.NotifyComponentsRemoved(target);
					});
				}));
				return label;
			};

			GetProcessedComponents();
			// 要素バインドコールバック
			listView.bindItem = (element, index) => {
				if (listView.itemsSource == null || index >= listView.itemsSource.Count) return;

				var component = listView.itemsSource[index] as Component;
				if (component != null)
				{
					(element as Label).text = component.name;
					element.SetEnabled(!_processed.Contains(listView.itemsSource[index]));
				}
				element.userData = component;
			};

			// 選択タイプを複数選択に設定
			listView.selectionType = SelectionType.Multiple;

			// 選択変更イベントの登録
			listView.onSelectionChange += (selectedItems) => {
				SelectGameObject(selectedItems);
			};
			// 選択変更イベント(ダブルクリック)の登録
			listView.onItemsChosen += (selectedItems) => {
				SelectGameObject(selectedItems);
			};

			void SelectGameObject(IEnumerable<object> selectedItems)
			{
				if (selectedItems == null || selectedItems.ToList().Count == 0) return;

				// 選択したアイテムのGameObjectをUnityの選択に反映
				Selection.objects = selectedItems
					.OfType<Component>()
					.Select(c => c.gameObject)
					.Cast<UnityEngine.Object>()
					.ToArray();
			}
		}

		/// <summary>
		/// ドラッグハンドラーのクリーンアップ
		/// </summary>
		private void CleanupDragHandlers()
		{
			if (_pbListDragHandler != null)
			{
				_pbListDragHandler.OnDrop -= OnPhysBoneListDrop;
				_pbListDragHandler.Cleanup();
				_pbListDragHandler = null;
			}

			if (_pbcListDragHandler != null)
			{
				_pbcListDragHandler.OnDrop -= OnPhysBoneColliderListDrop;
				_pbcListDragHandler.Cleanup();
				_pbcListDragHandler = null;
			}
		}
		#endregion

		#region ListView Drag & Drop Events
		/// <summary>
		/// PhysBoneリストにオブジェクトがドロップされた時の処理
		/// </summary>
		private void OnPhysBoneListDrop(List<GameObject> objects)
		{
			// データが変更されたためリスト更新をリクエスト
			_pbDataManager.ReloadData();
		}

		/// <summary>
		/// PhysBoneColliderリストにオブジェクトがドロップされた時の処理
		/// </summary>
		private void OnPhysBoneColliderListDrop(List<GameObject> objects)
		{
			// データが変更されたためリスト更新をリクエスト
			_pbDataManager.ReloadData();
		}
		#endregion

		#region ListView Data Changed Handlers
		/// <summary>
		/// PhysBoneデータ変更時の処理
		/// </summary>
		private void OnPhysBonesDataChanged(List<VRCPhysBone> physBones)
		{
			if (_pbListView == null) return;

			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// リストビューのアイテムソースを更新
				_pbListView.itemsSource = new List<Component>(physBones.Cast<Component>());

				// リストビューを再描画
				RepaintListView(_pbListView);
			};
		}

		/// <summary>
		/// PhysBoneColliderデータ変更時の処理
		/// </summary>
		private void OnPhysBoneCollidersDataChanged(List<VRCPhysBoneCollider> colliders)
		{
			if (_pbcListView == null) return;

			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// リストビューのアイテムソースを更新
				_pbcListView.itemsSource = new List<Component>(colliders.Cast<Component>());

				// リストビューを再描画
				RepaintListView(_pbcListView);
			};
		}

		/// <summary>
		/// Constraintデータ変更時の処理
		/// </summary>
		private void OnVRCConstraintsDataChanged(List<VRCConstraintBase> constraints)
		{
			if (_constraintListViewList.Any(list => list == null)) return;

			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// リストビューのアイテムソースを更新
				_constraintListViewList.ForEach(list =>
				{
					switch (_constraintListViewList.IndexOf(list))
					{
					case 0: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCPositionConstraint));
						break;
					case 1: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCRotationConstraint));
						break;
					case 2: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCScaleConstraint));
						break;
					case 3: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCParentConstraint));
						break;
					case 4: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCLookAtConstraint));
						break;
					case 5: list.itemsSource = new List<Component>(constraints.Where(constraint => constraint is VRCAimConstraint));
						break;
					}

					// リストビューを再描画
					RepaintListView(list);
				});
			};
		}

		/// <summary>
		/// Contactデータ変更時の処理
		/// </summary>
		private void OnVRCContactsDataChanged(List<Component> contacts)
		{
			if (_contactSenderListView == null || _contactReciverListView == null) return;

			// UIスレッドで更新
			EditorApplication.delayCall += () => {
				// リストビューのアイテムソースを更新
				_contactSenderListView.itemsSource = new List<Component>(contacts.Where(component => component is ContactSender));
				_contactReciverListView.itemsSource = new List<Component>(contacts.Where(component => component is ContactReceiver));

				// リストビューを再描画
				RepaintListView(_contactSenderListView);
				RepaintListView(_contactReciverListView);
			};
		}
		#endregion

		#region ListView UI Helpers
		/// <summary>
		/// すべてのリストビューを再描画
		/// </summary>
		private void RepaintAllListViews()
		{
			RepaintListView(_pbListView);
			RepaintListView(_pbcListView);
			_constraintListViewList.ForEach(list => RepaintListView(list));
			RepaintListView(_contactSenderListView);
			RepaintListView(_contactReciverListView);
		}

		/// <summary>
		/// 単一のリストビューを再描画
		/// </summary>
		private void RepaintListView(ListView listView)
		{
			if (listView == null) return;

#if UNITY_2019
			listView.Refresh();
#elif UNITY_2021_3_OR_NEWER
			listView.Rebuild();
#else
			listView.Refresh();
#endif
		}
		#endregion
	}
}
