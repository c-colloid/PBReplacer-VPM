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
			public VisualElement Root;   // UXML の Instance（表示切替に使う）
			public VisualElement Body;   // 列本体 .pbr-column（状態クラス・ドロップ先）
			public Label Count;
			public ListView List;
			public VisualElement Empty;
			public List<RowItem> Rows = new List<RowItem>();
			public List<Component> All = new List<Component>();
			public int Pending;
			public bool ShowDone = true;
		}
		#endregion

		#region Column Binding
		/// <summary>
		/// 列の見た目は PBReplacerColumn.uxml。ComponentCategory ごとに Instantiate して columns に並べる
		/// （UXML の Template/Instance はインポート順で失敗することがあるため C# で行う）。
		/// ここでは各列に名前・アイコン・一覧の生成/バインド・ドロップ処理を結び付けるだけ
		/// </summary>
		private void InitializeColumns()
		{
			CleanupDropHandlers();
			_columnViews.Clear();
			_columns.Clear();

			_rowTemplate = Resources.Load<VisualTreeAsset>("UXML/PBReplacerRow");
			if (_rowTemplate == null) Debug.LogError("PBReplacerRow.uxml が見つかりません。");
			var columnTemplate = Resources.Load<VisualTreeAsset>("UXML/PBReplacerColumn");
			if (columnTemplate == null) { Debug.LogError("PBReplacerColumn.uxml が見つかりません。"); return; }

			foreach (var category in ComponentCategoryInfo.All)
			{
				var root = columnTemplate.Instantiate();
				root.name = $"column-{category}";
				root.AddToClassList("pbr-column-slot");
				var title = root.Q<Label>("title");
				if (title != null) title.text = ComponentCategoryInfo.DisplayName(category);
				_columns.Add(root);

				var column = BindColumn(category, root);
				_columnViews[category] = column;

				// 列全体がドロップ先（見出しの ＋ はその目印）
				_dropHandlers.Add(new ColumnDropHandler(column.Body, category, OnColumnDropped));
			}

			ApplyVisibility();
		}

		private CategoryColumn BindColumn(ComponentCategory category, VisualElement root)
		{
			var column = new CategoryColumn { Category = category, Root = root };
			column.Body = root.Q<VisualElement>("column") ?? root;

			var headerIcon = root.Q<Image>("header-icon");
			if (headerIcon != null) { headerIcon.image = ComponentIconUtility.GetCategoryIcon(category, out _); headerIcon.scaleMode = ScaleMode.ScaleToFit; }
			PBRemapIcons.Set(root.Q<Image>("dropzone-icon"), PBRemapIcons.AutoCreate);
			PBRemapIcons.Set(root.Q<Image>("empty-icon"), PBRemapIcons.Empty);

			column.Count = root.Q<Label>("count");
			column.Empty = root.Q<VisualElement>("empty");

			column.List = root.Q<ListView>("list");
			column.List.itemsSource = column.Rows;
			column.List.makeItem = () => MakeRow(column);
			column.List.bindItem = (element, index) => BindRow(column, element, index);
#if UNITY_2022_2_OR_NEWER
			column.List.selectionChanged += items => OnRowsSelected(column, items);
			column.List.itemsChosen += items => OnRowsChosen(items);
#else
			column.List.onSelectionChange += items => OnRowsSelected(column, items);
			column.List.onItemsChosen += items => OnRowsChosen(items);
#endif
			return column;
		}

		/// <summary>行の要素は PBReplacerRow.uxml。ここでは内蔵アイコンとクリック / 右クリックを結び付ける</summary>
		private VisualElement MakeRow(CategoryColumn column)
		{
			var instance = _rowTemplate != null ? _rowTemplate.Instantiate() : null;
			var row = instance?.Q<VisualElement>("row");
			if (row == null)
			{
				row = new VisualElement();
				row.AddToClassList("pbr-row");
				return row;
			}
			row.RemoveFromHierarchy();

			PBRemapIcons.Set(row.Q<Image>("fold"), PBRemapIcons.Dropdown);
			PBRemapIcons.Set(row.Q<Image>("action-ping-icon"), "UnityEditor.SceneHierarchyWindow");
			PBRemapIcons.Set(row.Q<Image>("action-delete-icon"), PBRemapIcons.Trash);

			var pingButton = row.Q<Button>("action-ping");
			if (pingButton != null) pingButton.clicked += () => PingRowComponent(row);
			var deleteButton = row.Q<Button>("action-delete");
			if (deleteButton != null) deleteButton.clicked += () => DeleteRowComponent(row);

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

			var fold = element.Q<Image>("fold");
			var icon = element.Q<Image>("state");
			var name = element.Q<Label>("name");
			var actions = element.Q<VisualElement>("actions");
			if (fold == null || icon == null || name == null) return;
			actions?.EnableInClassList("pbr-row-actions--hidden", item.IsDoneHeader || item.Component == null);

			element.EnableInClassList("pbr-row--header", item.IsDoneHeader);
			element.EnableInClassList("pbr-row--done", item.Processed && !item.IsDoneHeader);

			icon.RemoveFromClassList("pbr-row-icon--pending");
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

			// 未処理は USS のリング（画像なし）、配置済みは ✔
			icon.EnableInClassList("pbr-row-icon--pending", !item.Processed);
			if (item.Processed) PBRemapIcons.Set(icon, PBRemapIcons.Resolved);
			else icon.image = null;
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
				column.Body.RemoveFromClassList("pbr-column--last");
				if (visible) lastVisible = column;

				if (_chips.TryGetValue(category, out var chip))
				{
					chip.Update(column.Pending, column.All.Count, visible);
				}
			}
			lastVisible?.Body.AddToClassList("pbr-column--last");
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
