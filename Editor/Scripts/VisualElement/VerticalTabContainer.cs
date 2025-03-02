using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace colloid.PBReplacer
{
	using Unity.Properties;
	/// <summary>
	/// A container for vertical tabs that manages their grouping and selection.
	/// Similar to RadioButtonGroup but for vertical tabs.
	/// </summary>
	public class VerticalTabContainer : BaseField<int>, IGroupBox
	{		
        #region UXML Factory and Traits for UI Builder
		/// <summary>
		/// Defines UxmlTraits for the VerticalTabContainer.
		/// </summary>
		public new class UxmlTraits : BaseFieldTraits<int, UxmlIntAttributeDescription>
		{
			UxmlStringAttributeDescription m_Choices = new UxmlStringAttributeDescription { name = "choices" };
			
			public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
			{
				base.Init(ve, bag, cc);
				
				var f = (VerticalTabContainer)ve;
				f.choices = ParseChoiceList(m_Choices.GetValueFromBag(bag, cc));
			}
			
			private static List<string> ParseChoiceList(string choices)
			{
				if (string.IsNullOrEmpty(choices))
				    return null;
				    
				// カンマ区切りの文字列を解析
				return choices.Split(',')
				    .Select(s => s.Trim())
				    .ToList();
			}
		}

		/// <summary>
		/// Factory class for creating VerticalTabContainer instances from UXML.
		/// </summary>
		public new class UxmlFactory : UxmlFactory<VerticalTabContainer, UxmlTraits> 
		{
		}
        #endregion

		// CSS classnames
		public static readonly string containerUssClassName = "pb-replacer-vertical-tab-container";
        
		private readonly List<VerticalTabElement> m_RegisteredTabs = new List<VerticalTabElement>();
		private VerticalTabElement m_SelectedTab;
		private EventCallback<VerticalTabElement.ValueChangedEvent> m_TabValueChangedCallback;
		private bool m_UpdatingTabs;
		private UQueryBuilder<VerticalTabElement> m_GetAllTabsQuery;

		/// <summary>
		/// Gets the currently selected tab element.
		/// </summary>
		public VerticalTabElement selectedTab => m_SelectedTab;

		/// <summary>
		/// Gets the text of the currently selected tab.
		/// </summary>
		public string selectedTabText => m_SelectedTab != null ? m_SelectedTab.text : string.Empty;
		
		[CreateProperty]
		public override int value
		{
			get { return base.value; }
			set {
					if (base.value == value)
					{
						return;
					} 
					base.value = value;
					UpdateTabs(); 
				}
		}
		
		public IEnumerable<string> choices
		{
			get
			{
				foreach (var registerdTab in m_RegisteredTabs)
				{
					yield return registerdTab.text;
				}
			}
			set
			{
				if (value == null || !value.Any<string>())
				{
					Clear();
					// パネル接続時の処理...
					if (panel != null)
					{
						return;
					}
					// 既存のラジオボタンのクリーンアップ
					foreach (var registerdTab in m_RegisteredTabs)
					{
						registerdTab.UnregisterCallback(m_TabValueChangedCallback);
					}
					m_RegisteredTabs.Clear();
					return;
				}

				var i = 0;
				foreach (var choice in value)
				{
					if (i < m_RegisteredTabs.Count)
					{
						// 既存のボタンを再利用
						m_RegisteredTabs[i].text = choice;
						Insert(i, m_RegisteredTabs[i]);
					}
					else
					{
						// 新しいボタンを作成
						var tabElement = new VerticalTabElement { text = choice };
						tabElement.RegisterCallback(m_TabValueChangedCallback);
						m_RegisteredTabs.Add(tabElement);
						Add(tabElement);
					}
					i++;
				}

				// 余分なボタンを削除
				var lastIndex = m_RegisteredTabs.Count - 1;
				for (var j = lastIndex; j >= i; j--)
				{
					m_RegisteredTabs[j].RemoveFromHierarchy();
				}

				UpdateTabs();
			}
		}

		/// <summary>
		/// デフォルトコンストラクタ - UI Builderで必要
		/// </summary>
		public VerticalTabContainer() : this(null)
		{
		}

		/// <summary>
		/// Creates a new VerticalTabContainer with the specified label.
		/// </summary>
		/// <param name="label">The label to display.</param>
		public VerticalTabContainer(string label) : base(label, null)
		{
			// Apply styles
			AddToClassList(containerUssClassName);
			//style.width = 60;
			style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
			style.flexShrink = 0;
            
			// BaseFieldのinput要素を非表示にする（直接参照できないので、クラス名で検索）
			var inputElement = this.Q(className: "unity-base-field__input");
			if (inputElement != null)
			{
				inputElement.style.display = DisplayStyle.None;
			}
            
			// Create query to get all tabs
			m_GetAllTabsQuery = this.Query<VerticalTabElement>();
            
			// Create callback for tab value changes
			m_TabValueChangedCallback = TabValueChangedCallback;
            
			// Set initial value to 0 (selected 1st)
			value = 0;
            
			// Set focusable state
			focusable = false;
			delegatesFocus = true;
            
			// Register for child attachment events
			RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
		}

		private void OnAttachToPanel(AttachToPanelEvent evt)
		{
			// When panel is attached, find any existing VerticalTabElement children
			// and make sure they're properly handled
			ScheduleTabUpdate();
		}

		/// <summary>
		/// Handles a tab's value change event.
		/// </summary>
		private void TabValueChangedCallback(VerticalTabElement.ValueChangedEvent evt)
		{
			// Ignore if we're in the middle of updating tabs
			if (m_UpdatingTabs)
				return;

			if (evt.newValue)
			{
				var tab = evt.target as VerticalTabElement;
				// Get all tabs
				List<VerticalTabElement> tabList = new List<VerticalTabElement>();
				GetAllTabs(tabList);
                
				// Set the value to the index of the selected tab
				value = tabList.IndexOf(tab);
                
				// Stop propagation after handling
				evt.StopPropagation();
			}
		}

		/// <summary>
		/// Sets the value without notifying change listeners.
		/// </summary>
		public override void SetValueWithoutNotify(int newValue)
		{
			if (base.value == newValue)
				return;
			base.SetValueWithoutNotify(newValue);
			UpdateTabs();
		}

		/// <summary>
		/// Gets all tab elements in this container.
		/// </summary>
		private void GetAllTabs(List<VerticalTabElement> tabs)
		{
			tabs.Clear();
			m_GetAllTabsQuery.ForEach(tabs.Add);
		}

		/// <summary>
		/// Updates all tabs based on the current value.
		/// </summary>
		private void UpdateTabs()
		{
			if (panel == null)
				return;

			// Get all tabs
			List<VerticalTabElement> tabList = new List<VerticalTabElement>();
			GetAllTabs(tabList);
            
			// Clear the selected tab first
			m_SelectedTab = null;
            
			// If value is valid, select the tab at that index
			if (value >= 0 && value < tabList.Count)
			{
				m_SelectedTab = tabList[value];
				m_SelectedTab.value = true;
			}
            
			// Deselect all other tabs
			foreach (var tab in tabList)
			{
				if (tab != m_SelectedTab)
				{
					tab.value = false;
				}
			}
            
			m_UpdatingTabs = false;
		}

		/// <summary>
		/// Schedules tab updates to occur on the next frame.
		/// </summary>
		private void ScheduleTabUpdate()
		{
			if (m_UpdatingTabs)
				return;
                
			schedule.Execute(UpdateTabs);
			m_UpdatingTabs = true;
		}

		/// <summary>
		/// Registers a tab with this container.
		/// </summary>
		private void RegisterTab(VerticalTabElement tab)
		{
			if (m_RegisteredTabs.Contains(tab))
				return;
                
			m_RegisteredTabs.Add(tab);
			tab.RegisterCallback(m_TabValueChangedCallback);
			ScheduleTabUpdate();
		}

		/// <summary>
		/// Unregisters a tab from this container.
		/// </summary>
		private void UnregisterTab(VerticalTabElement tab)
		{
			if (!m_RegisteredTabs.Contains(tab))
				return;
                
			m_RegisteredTabs.Remove(tab);
			tab.UnregisterCallback(m_TabValueChangedCallback);
            
			// If the unregistered tab was selected, clear selection
			if (m_SelectedTab == tab)
			{
				m_SelectedTab = null;
				value = -1;
			}
            
			ScheduleTabUpdate();
		}

		/// <summary>
		/// Adds a new tab to the container.
		/// </summary>
		/// <param name="tabText">The text to display on the tab.</param>
		/// <returns>The created VerticalTabElement.</returns>
		public VerticalTabElement AddTab(string tabText)
		{
			var tab = new VerticalTabElement(tabText);
			Add(tab);
			return tab;
		}

		/// <summary>
		/// Selects a tab at the specified index.
		/// </summary>
		/// <param name="index">The index of the tab to select.</param>
		/// <returns>True if the tab was found and selected, false otherwise.</returns>
		public bool SelectTab(int index)
		{
			if (index < 0)
			{
				value = -1;
				return true;
			}
            
			// Get all tabs
			List<VerticalTabElement> tabList = new List<VerticalTabElement>();
			GetAllTabs(tabList);
            
			if (index >= 0 && index < tabList.Count)
			{
				value = index;
				return true;
			}
            
			return false;
		}

		/// <summary>
		/// Gets a tab at the specified index.
		/// </summary>
		/// <param name="index">The index of the tab.</param>
		/// <returns>The VerticalTabElement at the specified index, or null if not found.</returns>
		public VerticalTabElement GetTab(int index)
		{
			if (index < 0)
				return null;
                
			int currentIndex = 0;
            
			foreach (var child in Children())
			{
				if (child is VerticalTabElement tab)
				{
					if (currentIndex == index)
					{
						return tab;
					}
					currentIndex++;
				}
			}
            
			return null;
		}	

        #region IGroupBox Implementation
		void IGroupBox.OnOptionAdded(IGroupBoxOption option)
		{
			// Only handle VerticalTabElement options
			if (option is VerticalTabElement tab)
			{
				RegisterTab(tab);
			}
		}

		void IGroupBox.OnOptionRemoved(IGroupBoxOption option)
		{
			// Only handle VerticalTabElement options
			if (option is VerticalTabElement tab)
			{
				UnregisterTab(tab);
			}
		}
        #endregion
	}
}