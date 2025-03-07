using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;

namespace colloid.PBReplacer
{
	/// <summary>
	/// A custom visual element that represents a vertical tab with rotated text.
	/// </summary>
	public class VerticalTabElement : VisualElement, IGroupBoxOption
	{
		/// <summary>
		/// Event sent when the tab value changes.
		/// </summary>
		public class ValueChangedEvent : EventBase<ValueChangedEvent>
		{
			public bool newValue { get; set; }

			protected override void Init()
			{
				base.Init();
				newValue = false;
			}

			public static ValueChangedEvent GetPooled(bool value)
			{
				var evt = EventBase<ValueChangedEvent>.GetPooled();
				evt.newValue = value;
				return evt;
			}
		}

        #region UXML Factory and Traits for UI Builder
		/// <summary>
		/// Defines UxmlTraits for the VerticalTabElement.
		/// </summary>
		public new class UxmlTraits : VisualElement.UxmlTraits
		{
			private UxmlStringAttributeDescription m_Text = new UxmlStringAttributeDescription { name = "text", defaultValue = "Tab" };
			private UxmlBoolAttributeDescription m_Value = new UxmlBoolAttributeDescription { name = "value", defaultValue = false };
			private UxmlStringAttributeDescription m_IconPath = new UxmlStringAttributeDescription { name = "icon-path", defaultValue = "" };
			private UxmlBoolAttributeDescription m_AutoHeight = new UxmlBoolAttributeDescription { name = "auto-height", defaultValue = false };

			/// <summary>
			/// Constructor.
			/// </summary>
			public UxmlTraits()
			{
			}

			/// <summary>
			/// Initialize VerticalTabElement properties using values from the attribute bag.
			/// </summary>
			public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
			{
				base.Init(ve, bag, cc);
				var element = (VerticalTabElement)ve;
                
				element.text = (m_Text.GetValueFromBag(bag, cc));
				element.value = m_Value.GetValueFromBag(bag, cc);
				element.autoHeight = m_AutoHeight.GetValueFromBag(bag, cc);
                
				element.iconPath = m_IconPath.GetValueFromBag(bag, cc);
				
				element.UpdateContainerSize();
			}
		}

		/// <summary>
		/// Factory class for creating VerticalTabElement instances from UXML.
		/// </summary>
		public new class UxmlFactory : UxmlFactory<VerticalTabElement, UxmlTraits> 
		{
		}
        #endregion

		private Label m_Label;
		private VisualElement m_Icon;
		private bool m_Value;
		private string m_IconPath = string.Empty;
		private bool m_AutoHeight = false;
		private const int TEXT_MARGIN = 20; // 文字のマージン（左右10pxずつ）
		
		private VisualElement m_conteiner = new VisualElement(){style = {justifyContent = Justify.Center}};

		private IVisualElementScheduledItem m_ContainerSizeSchedule;
		private IVisualElementScheduledItem m_TabHeightSchedule;
		
		// プロパティ
		public string text
		{
			get => m_Label.text;
			set 
			{
				if (m_Label.text != value)
				{
					m_Label.text = value;
					if (string.IsNullOrEmpty(m_Label.text))
					{
						m_Label.RemoveFromHierarchy();
					}
					else
					{
						if (!this.Contains(m_Label))
						{
							m_conteiner.Add(m_Label);
						}
					}
					
					SetText(value);
				}
			}
		}

		/// <summary>
		/// テキスト長さに応じて高さを自動調整するかどうか
		/// </summary>
		public bool autoHeight
		{
			get => m_AutoHeight;
			set
			{
				if (m_AutoHeight != value)
				{
					m_AutoHeight = value;
					if (m_AutoHeight && m_Label != null)
					{
						UpdateTabHeight();
					}
					if (!m_AutoHeight)
					{
						style.height = StyleKeyword.Auto;
						// 自動高さ調整をオフにした場合、明示的に高さが設定されていなければデフォルト値に
						if (style.height == StyleKeyword.Auto)
						{
							//style.height = 80;
						}
						UpdateContainerSize();
					}
				}
			}
		}

		/// <summary>
		/// Gets or sets the path to the icon texture.
		/// </summary>
		public string iconPath
		{
			get => m_IconPath;
			set
			{
				if (m_IconPath != value)
				{
					m_IconPath = value;
					// エディタ上での実行時のみアイコンを更新
                    #if UNITY_EDITOR
					if (!string.IsNullOrEmpty(m_IconPath))
					{
						if (m_Icon == null)
						{
							InitIcon();
						}
						
						var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(m_IconPath);
						if (texture != null)
						{
							SetIcon(texture);
						}
					}
					else if (m_Icon != null)
					{
						// アイコンをクリア
						m_Icon.style.backgroundImage = null;
						m_Icon.RemoveFromHierarchy();
						m_Icon = null;
					}
                    #endif
				}
			}
		}

		/// <summary>
		/// Gets or sets the selected state of the tab.
		/// </summary>
		public bool value
		{
			get => m_Value;
			set
			{
				if (m_Value != value)
				{
					m_Value = value;
					UpdateVisualState();
                    
					// Notify listeners that the value has changed
					using (var evt = ValueChangedEvent.GetPooled(value))
					{
						evt.target = this;
						SendEvent(evt);
					}
                    
					if (value)
					{
						// Notify the group box that this option was selected
						GroupBoxUtility.OnOptionSelected(this);
					}
				}
			}
		}

		/// <summary>
		/// 引数なしのコンストラクタ - UI Builderで必要
		/// </summary>
		public VerticalTabElement() : this(null)
		{
		}

		/// <summary>
		/// Creates a new VerticalTabElement with the specified label text.
		/// </summary>
		/// <param name="text">The text to display on the tab.</param>
		public VerticalTabElement(string text)
		{
			// Add USS class for styling
			AddToClassList("pb-replacer-vertical-tab");
            
			// Setup base container styles
			style.borderBottomWidth = 1;
			style.borderBottomColor = new Color(0.1f, 0.1f, 0.1f);
			style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            
			iconPath = null;
            
			InitLabel(text);
			if (text != null)
			{
				this.text = text;
			}
			
			Add(m_conteiner);
			m_ContainerSizeSchedule = schedule.Execute(UpdateContainerSize);
            
			// Register for mouse events
			RegisterCallback<ClickEvent>(OnClick);
			RegisterCallback<MouseOverEvent>(OnMouseOver);
			RegisterCallback<MouseOutEvent>(OnMouseOut);
            
			// Register for layout events
			RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            
			// Register with the group box when attached to a panel
			RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
			RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
			
			// スタイル変更を監視するイベントを登録
			//RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
		}
		
		private void InitIcon()
		{
			// Add icon placeholder (will be populated later)
			m_Icon = new VisualElement();
			m_Icon.AddToClassList("pb-replacer-vertical-tab__icon");
			m_Icon.style.width = 24;
			m_Icon.style.height = 24;
			m_Icon.style.marginBottom = 2;
			m_Icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
			Insert(0,m_Icon);
		}
		
		private void InitLabel(string text)
		{
			// Add label with rotated text
			m_Label = new Label(text);
			m_Label.AddToClassList("pb-replacer-vertical-tab__text");
			m_Label.style.rotate = new StyleRotate(new Rotate(-90));
			m_Label.style.color = new Color(0.7f, 0.7f, 0.7f);
			m_Label.style.fontSize = this.style.fontSize;
			m_Label.style.unityTextAlign = this.style.unityTextAlign;
			m_Label.style.unityFontStyleAndWeight = this.style.unityFontStyleAndWeight;
			m_Label.style.whiteSpace = this.style.whiteSpace;
			m_Label.style.letterSpacing = this.style.letterSpacing;
		}

		private void OnGeometryChanged(GeometryChangedEvent evt)
		{
			UpdateContainerSize();
			if (m_AutoHeight && evt.target == m_Label)
			{
				UpdateTabHeight();
			}
		}
		
		private void OnStyleResolved(CustomStyleResolvedEvent evt)
		{
			UpdateContainerSize();
		}
		
		private void UpdateContainerSize()
		{
			var size = m_Label.MeasureTextSize(m_Label.text,resolvedStyle.width,MeasureMode.Undefined,resolvedStyle.height,MeasureMode.Undefined);
			m_conteiner.style.width = Mathf.RoundToInt(size.y);
			m_conteiner.style.height = Mathf.RoundToInt(size.x);
			MarkDirtyRepaint();
		}

		/// <summary>
		/// テキストのサイズに基づいてタブの高さを更新します
		/// </summary>
		private void UpdateTabHeight()
		{
			UpdateContainerSize();
			
			var size = m_Label.MeasureTextSize(m_Label.text,resolvedStyle.width,MeasureMode.AtMost,resolvedStyle.height,MeasureMode.AtMost);
			style.minWidth = Mathf.RoundToInt(size.y + m_Icon?.resolvedStyle.width ?? size.y);
			style.minHeight = Mathf.RoundToInt(size.x + m_Icon?.resolvedStyle.height ?? size.x);
			
			if (m_Label == null || !m_AutoHeight)
				return;
			
			// Font sizeの1.5倍をテキスト1文字あたりの幅として概算
			float charWidth = m_Label.resolvedStyle.fontSize * 1.5f;
			// テキスト長に基づいた高さを計算（テキストが-90度回転しているため、テキスト長は高さに影響）
			float textHeight = charWidth + TEXT_MARGIN; // 最低でも1文字分+マージン
            
			if (m_Label.text.Length > 1)
			{
				// 文字数に基づいて高さを計算（日本語で約1em、英語で約0.5em）
				// 日本語と英語が混在する場合を想定して中間値を使用
				textHeight = m_Label.text.Length * charWidth * 0.75f + TEXT_MARGIN;
			}
            
			// アイコンのためのスペースを追加（アイコンの高さ + マージン）
			if (m_Icon != null)
			{
				textHeight += 24 + 8;	
			}
            
			// 最小高さ・最大高さのデフォルト値
			float minHeight = 80;
			float maxHeight = 200;
            
			// style.minHeightとstyle.maxHeightの値を確認
			var currentMinHeight = style.minHeight;
			var currentMaxHeight = style.maxHeight;
            
			if (currentMinHeight != StyleKeyword.Auto && currentMinHeight != StyleKeyword.Initial && currentMinHeight != StyleKeyword.None)
			{
				try
				{
					// 明示的に設定されていれば、それを使用
					float parsedMin = float.Parse(currentMinHeight.ToString().Replace("px", ""));
					if (parsedMin > 0)
					{
						minHeight = parsedMin;
					}
				}
					catch {}
			}
            
			if (currentMaxHeight != StyleKeyword.Auto && currentMaxHeight != StyleKeyword.Initial && currentMaxHeight != StyleKeyword.None)
			{
				try
				{
					// 明示的に設定されていれば、それを使用
					float parsedMax = float.Parse(currentMaxHeight.ToString().Replace("px", ""));
					if (parsedMax > 0 && parsedMax < 1000)
					{
						maxHeight = parsedMax;
					}
				}
					catch {}
			}
            
			// 最小高さ・最大高さの範囲内に収める
			int newHeight = Mathf.Clamp(Mathf.RoundToInt(textHeight), (int)minHeight, (int)maxHeight);
			style.height = newHeight;
			style.height = StyleKeyword.Auto;
		}

		/// <summary>
		/// Sets the icon for the tab.
		/// </summary>
		/// <param name="iconTexture">The texture to use as an icon.</param>
		public void SetIcon(Texture2D iconTexture)
		{
			m_Icon.style.backgroundImage = iconTexture;
		}
        
		/// <summary>
		/// Sets the text displayed on the tab.
		/// </summary>
		/// <param name="text">The text to display.</param>
		public void SetText(string text)
		{
			if (m_AutoHeight)
			{
				// パネルにアタッチされていれば即座に高さを更新
				if (panel != null)
				{
					UpdateTabHeight();
				}
				// そうでなければ次のフレームで更新をスケジュール
				else
				{
					m_TabHeightSchedule = schedule.Execute(UpdateTabHeight);
				}
			}
		}

		private void OnClick(ClickEvent evt)
		{
			value = true;
			evt.StopPropagation();
		}

		private void OnMouseOver(MouseOverEvent evt)
		{
			style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
		}

		private void OnMouseOut(MouseOutEvent evt)
		{
			UpdateVisualState();
		}

		private void OnAttachToPanel(AttachToPanelEvent evt)
		{
			GroupBoxUtility.RegisterGroupBoxOption(this);
            
			if (m_AutoHeight)
			{
				// パネルにアタッチされたときに高さを計算
				m_TabHeightSchedule = schedule.Execute(UpdateTabHeight);
			}
		}

		private void OnDetachFromPanel(DetachFromPanelEvent evt)
		{
			GroupBoxUtility.UnregisterGroupBoxOption(this);
			
			m_ContainerSizeSchedule = null;
			m_TabHeightSchedule = null;
		}

		private void UpdateVisualState()
		{
			style.backgroundColor = m_Value ?
				new Color(0.25f, 0.25f, 0.25f) : new Color(0.18f, 0.18f, 0.18f);
			style.borderRightWidth = m_Value ?
				4 : 0;
			m_Label.style.color = m_Value ?
				Color.white : new Color(0.7f, 0.7f, 0.7f);
            
			if (m_Value)
			{
				style.borderRightColor = new Color(0.243f, 0.475f, 0.776f);
                
				AddToClassList("pb-replacer-vertical-tab--selected");
			}
			else
			{
				RemoveFromClassList("pb-replacer-vertical-tab--selected");
			}
		}

		// IGroupBoxOption実装
		void IGroupBoxOption.SetSelected(bool selected)
		{
			// Only update the value if it's changing to true
			// This ensures you can't deselect a selected tab
			if (selected)
			{
				value = true;
			}
            
			else
			{
				// If in a RadioButtonGroup-like container, we need to respond to deselection too
				value = false;
			}
		}
	}
}