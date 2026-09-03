using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace colloid.PBReplacer
{
	/// <summary>
	/// PBReplacerWindow - 列と行（カテゴリごとの一覧、データ反映、選択、ドロップ）
	/// </summary>
	public partial class PBReplacerWindow
	{
		#region Models
		/// <summary>列の 1 行。コンポーネント行か、配置済みをまとめる見出し行</summary>
		private class RowItem
		{
			public Component Component;
			public bool Processed;
			public bool IsDoneHeader;
			public int DoneCount;
			public string Path;
		}

		/// <summary>カテゴリ 1 つ分の列</summary>
		private class CategoryColumn
		{
			public ComponentCategory Category;
			public VisualElement Root;
			public Label Count;
			public ListView List;
			public VisualElement Empty;
			public List<RowItem> Rows = new List<RowItem>();
			public List<Component> All = new List<Component>();
			public int Pending;
			public bool ShowDone = true;
		}
		#endregion

		#region Column Construction
		private void InitializeColumns()
		{
			CleanupDropHandlers();
			_columns.Clear();
			_columnViews.Clear();

			var categories = ComponentCategoryInfo.All;
			for (int i = 0; i < categories.Length; i++)
			{
				var column = CreateColumn(categories[i]);
				if (i == categories.Length - 1) column.Root.AddToClassList("pbr-column--last");
				_columnViews[categories[i]] = column;
				_columns.Add(column.Root);

				// 列全体がドロップ先（見出しの ＋ はその目印）
				_dropHandlers.Add(new ColumnDropHandler(column.Root, categories[i], OnColumnDropped));
			}

			ApplyVisibility();
		}

		private CategoryColumn CreateColumn(ComponentCategory category)
		{
			var column = new CategoryColumn { Category = category };

			column.Root = new VisualElement { name = $"column-{category}" };
			column.Root.AddToClassList("pbr-column");

			// 見出し: [アイコン] 名前  未処理/全   [＋]
			var header = new VisualElement();
			header.AddToClassList("pbr-column-header");
			var icon = new Image { image = ComponentIconUtility.GetCategoryIcon(category, out _), scaleMode = ScaleMode.ScaleToFit };
			icon.AddToClassList("pbr-column-header-icon");
			header.Add(icon);
			var title = new Label(ComponentCategoryInfo.DisplayName(category));
			title.AddToClassList("pbr-column-title");
			header.Add(title);
			column.Count = new Label();
			column.Count.AddToClassList("pbr-column-count");
			header.Add(column.Count);
			var dropZone = new VisualElement { tooltip = "Hierarchy のオブジェクトをこの列へドロップで追加" };
			dropZone.AddToClassList("pbr-column-dropzone");
			dropZone.Add(PBRemapIcons.Image(PBRemapIcons.AutoCreate, 10));
			header.Add(dropZone);
			column.Root.Add(header);

			// 一覧
			column.List = new ListView();
			column.List.itemsSource = column.Rows;
#if UNITY_2021_2_OR_NEWER
			column.List.fixedItemHeight = 20;
#else
			column.List.itemHeight = 20;
#endif
			column.List.selectionType = SelectionType.Multiple;
			column.List.makeItem = () => MakeRow(column);
			column.List.bindItem = (element, index) => BindRow(column, element, index);
#if UNITY_2022_2_OR_NEWER
			column.List.selectionChanged += items => OnRowsSelected(column, items);
			column.List.itemsChosen += items => OnRowsChosen(items);
#else
			column.List.onSelectionChange += items => OnRowsSelected(column, items);
			column.List.onItemsChosen += items => OnRowsChosen(items);
#endif
			column.Root.Add(column.List);

			// 空状態: 対象なしのアイコンだけ（説明はツールチップ）
			column.Empty = new VisualElement { tooltip = "Armature 内に対象がありません\nHierarchy のオブジェクトをこの列へドロップすると追加できます" };
			column.Empty.AddToClassList("pbr-column-empty");
			column.Empty.Add(PBRemapIcons.Image(PBRemapIcons.Empty, 20));
			column.Empty.style.display = DisplayStyle.None;
			column.Root.Add(column.Empty);

			return column;
		}

		/// <summary>行の要素。状態アイコン + 名前。右クリックで Ping / 削除</summary>
		private VisualElement MakeRow(CategoryColumn column)
		{
			var row = new VisualElement();
			row.AddToClassList("pbr-row");

			var fold = PBRemapIcons.Image(PBRemapIcons.Dropdown, 10);
			fold.AddToClassList("pbr-row-fold");
			row.Add(fold);

			var icon = new Image { scaleMode = ScaleMode.ScaleToFit };
			icon.AddToClassList("pbr-row-icon");
			row.Add(icon);

			var name = new Label();
			name.AddToClassList("pbr-row-name");
			row.Add(name);

			// ホバーで出る操作: Hierarchy で表示 / 削除（右クリックメニューと同じ内容）
			var actions = new VisualElement();
			actions.AddToClassList("pbr-row-actions");
			var pingButton = PBRemapIcons.IconButton("UnityEditor.SceneHierarchyWindow", "Hierarchy で表示", () => PingRowComponent(row), 12);
			pingButton.AddToClassList("pbr-row-action");
			actions.Add(pingButton);
			var deleteButton = PBRemapIcons.IconButton(PBRemapIcons.Trash, "このコンポーネントを削除", () => DeleteRowComponent(row), 12);
			deleteButton.AddToClassList("pbr-row-action");
			actions.Add(deleteButton);
			row.Add(actions);

			// 見出し行のクリック = 配置済みの開閉
			row.RegisterCallback<ClickEvent>(evt =>
			{
				if (evt.button != 0) return;
				if (row.userData is RowItem item && item.IsDoneHeader)
				{
					column.ShowDone = !column.ShowDone;
					RebuildRows(column);
				}
			});

			row.AddManipulator(new ContextualMenuManipulator(evt =>
			{
				if (!(row.userData is RowItem item) || item.IsDoneHeader || item.Component == null) return;
				var target = item.Component;

				evt.menu.AppendAction("Hierarchy で表示", _ => PingComponent(target));
				evt.menu.AppendSeparator();
				evt.menu.AppendAction("このコンポーネントを削除", _ => DeleteComponent(target));
			}));

			return row;
		}

		private static void PingRowComponent(VisualElement row)
		{
			if (row.userData is RowItem item && !item.IsDoneHeader) PingComponent(item.Component);
		}

		private static void DeleteRowComponent(VisualElement row)
		{
			if (row.userData is RowItem item && !item.IsDoneHeader) DeleteComponent(item.Component);
		}

		private static void PingComponent(Component target)
		{
			if (target == null) return;
			Selection.activeGameObject = target.gameObject;
			EditorGUIUtility.PingObject(target.gameObject);
		}

		private static void DeleteComponent(Component target)
		{
			if (target == null) return;
			bool confirmed = EditorUtility.DisplayDialog(
				"コンポーネントの削除",
				$"以下のコンポーネントを削除します（1件）\n\n{target.name}\n\nこの操作はCtrl+Zで元に戻せます",
				"削除する",
				"やめる");
			if (!confirmed) return;

			Undo.DestroyObjectImmediate(target);
			DataManagerHelper.NotifyComponentsRemoved(target);
			DataManagerHelper.ReloadData();
		}

		private void BindRow(CategoryColumn column, VisualElement element, int index)
		{
			if (index < 0 || index >= column.Rows.Count) return;
			var item = column.Rows[index];
			element.userData = item;

			var fold = element.Q<Image>(className: "pbr-row-fold");
			var icon = element.Q<Image>(className: "pbr-row-icon");
			var name = element.Q<Label>(className: "pbr-row-name");
			var actions = element.Q<VisualElement>(className: "pbr-row-actions");
			actions.EnableInClassList("pbr-row-actions--hidden", item.IsDoneHeader || item.Component == null);

			element.EnableInClassList("pbr-row--header", item.IsDoneHeader);
			element.EnableInClassList("pbr-row--done", item.Processed && !item.IsDoneHeader);

			if (item.IsDoneHeader)
			{
				fold.style.display = DisplayStyle.Flex;
				fold.style.rotate = new StyleRotate(new Rotate(column.ShowDone ? 0 : -90));
				PBRemapIcons.Set(icon, PBRemapIcons.Resolved);
				name.text = item.DoneCount.ToString();
				element.tooltip = column.ShowDone
					? $"配置済み {item.DoneCount} 件（クリックで畳む）"
					: $"配置済み {item.DoneCount} 件（クリックで開く）";
				return;
			}

			fold.style.display = DisplayStyle.None;
			var component = item.Component;
			if (component == null)
			{
				name.text = "(削除済み)";
				icon.image = null;
				element.tooltip = "";
				return;
			}

			PBRemapIcons.Set(icon, item.Processed ? PBRemapIcons.Resolved : PBRemapIcons.Apply);
			name.text = component.name;
			element.tooltip = (item.Processed ? "配置済み: " : "未処理: ") + item.Path;
		}
		#endregion

		#region Data → Rows
		/// <summary>
		/// 全カテゴリの一覧・レール・流れをデータから作り直す。
		/// マネージャーのイベントは同一フレームに複数来るので delayCall でまとめる。
		/// </summary>
		private bool _refreshScheduled;

		private void ScheduleRefresh()
		{
			if (_refreshScheduled) return;
			_refreshScheduled = true;
			EditorApplication.delayCall += () =>
			{
				_refreshScheduled = false;
				RefreshAll();
			};
		}

		private void RefreshAll()
		{
			if (_root == null || _columnViews.Count == 0) return;

			try
			{
				// AvatarDynamics 配下の「対象 4 種」だけを配置済みとして数える（Transform や補助コンポーネントは含めない）
				_processed = new HashSet<Component>(DataManagerHelper.GetAvatarDynamicsComponent<Component>().Where(IsTargetComponent));
			}
			catch
			{
				_processed = new HashSet<Component>();
			}

			foreach (var column in _columnViews.Values)
			{
				column.All = GetCategoryComponents(column.Category);
				column.Pending = column.All.Count(c => c != null && !_processed.Contains(c));
				RebuildRows(column);

				if (_chips.TryGetValue(column.Category, out var chip))
				{
					chip.Update(column.Pending, column.All.Count, IsCategoryVisible(column.Category));
				}
			}

			UpdateIdleStateFromComponents();
			UpdateStrip();
			UpdateTools();
		}

		private void RebuildRows(CategoryColumn column)
		{
			column.Rows.Clear();

			var avatarRoot = AvatarFieldHelper.CurrentAvatar?.AvatarObject?.transform;
			var pending = new List<RowItem>();
			var done = new List<RowItem>();

			foreach (var component in column.All)
			{
				if (component == null) continue;
				bool processed = _processed.Contains(component);
				var item = new RowItem
				{
					Component = component,
					Processed = processed,
					Path = HierarchyPath(component.transform, avatarRoot),
				};
				(processed ? done : pending).Add(item);
			}

			column.Rows.AddRange(pending);
			if (done.Count > 0)
			{
				column.Rows.Add(new RowItem { IsDoneHeader = true, DoneCount = done.Count });
				if (column.ShowDone) column.Rows.AddRange(done);
			}

			column.Count.text = column.All.Count == 0 ? "" : $"{column.Pending} / {column.All.Count}";

			bool empty = column.All.Count == 0;
			column.List.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;
			column.Empty.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;

			RepaintListView(column.List);
		}

		private List<Component> GetCategoryComponents(ComponentCategory category)
		{
			try
			{
				switch (category)
				{
					case ComponentCategory.PhysBone:
						return _pbDataManager.Components.Cast<Component>().Distinct().ToList();
					case ComponentCategory.PhysBoneCollider:
						return _pbcDataManager.Components.Cast<Component>().Distinct().ToList();
					case ComponentCategory.Constraint:
						return _constraintDataManager.Components.Cast<Component>().Distinct().ToList();
					case ComponentCategory.Contact:
						return _contactDataManager.Components.Distinct().ToList();
				}
			}
			catch (Exception) { }
			return new List<Component>();
		}

		private static bool IsTargetComponent(Component c)
		{
			return c != null && (c is VRCPhysBone || c is VRCPhysBoneCollider || c is VRCConstraintBase || c is ContactBase);
		}

		private static string HierarchyPath(Transform t, Transform root)
		{
			if (t == null) return "";
			var parts = new List<string>();
			var current = t;
			while (current != null && current != root)
			{
				parts.Add(current.name);
				current = current.parent;
			}
			parts.Reverse();
			return string.Join("/", parts);
		}

		private void ApplyVisibility()
		{
			CategoryColumn lastVisible = null;
			foreach (var category in ComponentCategoryInfo.All)
			{
				if (!_columnViews.TryGetValue(category, out var column)) continue;
				bool visible = IsCategoryVisible(category);
				column.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
				column.Root.RemoveFromClassList("pbr-column--last");
				if (visible) lastVisible = column;

				if (_chips.TryGetValue(category, out var chip))
				{
					chip.Update(column.Pending, column.All.Count, visible);
				}
			}
			lastVisible?.Root.AddToClassList("pbr-column--last");
		}

		private void RepaintListView(ListView listView)
		{
			if (listView == null) return;
#if UNITY_2021_3_OR_NEWER
			listView.Rebuild();
#else
			listView.Refresh();
#endif
		}
		#endregion

		#region Counts
		/// <summary>表示中のカテゴリが属する処理グループ（PB と PBC は常に同じグループ）</summary>
		private List<int> VisibleProcessGroups()
		{
			var groups = new List<int>();
			foreach (var category in ComponentCategoryInfo.All)
			{
				if (!IsCategoryVisible(category)) continue;
				int g = ComponentCategoryInfo.ProcessGroup(category);
				if (!groups.Contains(g)) groups.Add(g);
			}
			return groups;
		}

		private int PendingCount(int processGroup)
		{
			int n = 0;
			foreach (var column in _columnViews.Values)
			{
				if (ComponentCategoryInfo.ProcessGroup(column.Category) == processGroup) n += column.Pending;
			}
			return n;
		}

		private int TotalPending() => _columnViews.Values.Sum(c => c.Pending);
		private int TotalComponents() => _columnViews.Values.Sum(c => c.All.Count);
		private int TotalProcessed() => _processed.Count;
		#endregion

		#region Selection / Drop
		private void OnRowsSelected(CategoryColumn column, IEnumerable<object> items)
		{
			var objects = items.OfType<RowItem>()
				.Where(r => !r.IsDoneHeader && r.Component != null)
				.Select(r => (UnityEngine.Object)r.Component.gameObject)
				.ToArray();
			if (objects.Length == 0) return;

			// 選択変更は Undo スタックに積まれるので、↶ が「直前の再配置」でなくなる
			InvalidateUndo();

			// 他の列の選択は解除（Unity の選択は 1 つなので）
			foreach (var other in _columnViews.Values)
			{
				if (other != column) other.List.ClearSelection();
			}
			Selection.objects = objects;
		}

		private void OnRowsChosen(IEnumerable<object> items)
		{
			var first = items.OfType<RowItem>().FirstOrDefault(r => !r.IsDoneHeader && r.Component != null);
			if (first != null) EditorGUIUtility.PingObject(first.Component.gameObject);
		}

		private void OnColumnDropped()
		{
			DataManagerHelper.ReloadData();
		}

		private void CleanupDropHandlers()
		{
			foreach (var handler in _dropHandlers) handler?.Dispose();
			_dropHandlers.Clear();
		}
		#endregion
	}
}
