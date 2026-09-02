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
	/// 列（カテゴリ）への Hierarchy からのドロップ = そのカテゴリのコンポーネントを追加。
	/// ドラッグ中は列の上辺と背景を青くして受け入れ可能を示す（PBRemap のノードと同じ語彙）。
	/// カテゴリに複数の型がある（Constraint / Contact）ときはドロップ後にメニューで型を選ぶ。
	/// </summary>
	public class ColumnDropHandler : IDisposable
	{
		private const string HoverClass = "pbr-column--drop-hover";

		private readonly VisualElement _target;
		private readonly ComponentCategory _category;
		private readonly Action _onDropped;

		public ColumnDropHandler(VisualElement target, ComponentCategory category, Action onDropped)
		{
			_target = target;
			_category = category;
			_onDropped = onDropped;

			// TrickleDown: 内側の ListView に先に取られないよう、列ルートで先に受ける
			_target.RegisterCallback<DragEnterEvent>(OnDragEnter, TrickleDown.TrickleDown);
			_target.RegisterCallback<DragLeaveEvent>(OnDragLeave, TrickleDown.TrickleDown);
			_target.RegisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
			_target.RegisterCallback<DragPerformEvent>(OnDragPerform, TrickleDown.TrickleDown);
			_target.RegisterCallback<DragExitedEvent>(OnDragExited, TrickleDown.TrickleDown);
		}

		public void Dispose()
		{
			if (_target == null) return;
			_target.UnregisterCallback<DragEnterEvent>(OnDragEnter, TrickleDown.TrickleDown);
			_target.UnregisterCallback<DragLeaveEvent>(OnDragLeave, TrickleDown.TrickleDown);
			_target.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
			_target.UnregisterCallback<DragPerformEvent>(OnDragPerform, TrickleDown.TrickleDown);
			_target.UnregisterCallback<DragExitedEvent>(OnDragExited, TrickleDown.TrickleDown);
			_target.RemoveFromClassList(HoverClass);
		}

		private static List<GameObject> GetDraggedSceneObjects()
		{
			return DragAndDrop.objectReferences
				.OfType<GameObject>()
				.Where(go => go != null && !EditorUtility.IsPersistent(go))
				.ToList();
		}

		private void OnDragEnter(DragEnterEvent evt)
		{
			if (GetDraggedSceneObjects().Count > 0)
			{
				_target.AddToClassList(HoverClass);
			}
		}

		private void OnDragLeave(DragLeaveEvent evt)
		{
			_target.RemoveFromClassList(HoverClass);
		}

		private void OnDragExited(DragExitedEvent evt)
		{
			_target.RemoveFromClassList(HoverClass);
		}

		private void OnDragUpdated(DragUpdatedEvent evt)
		{
			bool ok = GetDraggedSceneObjects().Count > 0;
			DragAndDrop.visualMode = ok ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
			_target.EnableInClassList(HoverClass, ok);
			evt.StopPropagation();
		}

		private void OnDragPerform(DragPerformEvent evt)
		{
			_target.RemoveFromClassList(HoverClass);
			var objects = GetDraggedSceneObjects();
			if (objects.Count == 0) return;

			DragAndDrop.AcceptDrag();
			evt.StopPropagation();

			var types = ComponentCategoryInfo.ComponentTypes(_category);
			if (types.Length == 0) return;

			if (types.Length == 1)
			{
				Attach(objects, types[0]);
				return;
			}

			// 型が複数あるカテゴリ: ドロップ位置にメニューを出して選ぶ
			var menu = new GenericMenu();
			foreach (var type in types)
			{
				var captured = type;
				menu.AddItem(new GUIContent(NiceTypeName(captured)), false, () => Attach(objects, captured));
			}
			menu.ShowAsContext();
		}

		private static string NiceTypeName(Type type)
		{
			// VRCPositionConstraint → Position Constraint, VRCContactSender → Contact Sender
			string name = type.Name.StartsWith("VRC", StringComparison.Ordinal) ? type.Name.Substring(3) : type.Name;
			return ObjectNames.NicifyVariableName(name);
		}

		private void Attach(List<GameObject> objects, Type type)
		{
			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName($"Add {type.Name}");
			int group = Undo.GetCurrentGroup();

			int added = 0;
			foreach (var go in objects)
			{
				if (go == null || go.GetComponent(type) != null) continue;

				var component = Undo.AddComponent(go, type);
				added++;

				if (component is VRCPhysBone pb)
				{
					pb.rootTransform = go.transform;
				}
				else if (component is VRCPhysBoneCollider pbc)
				{
					pbc.rootTransform = go.transform;
				}
			}

			Undo.CollapseUndoOperations(group);

			if (added > 0)
			{
				_onDropped?.Invoke();
			}
		}
	}
}
