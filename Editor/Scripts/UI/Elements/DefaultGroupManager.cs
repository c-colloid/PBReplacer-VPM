using System.Collections.Generic;
using UnityEngine.UIElements;

namespace colloid.PBReplacer
{
    /// <summary>
    /// Default implementation of the IGroupManager interface.
    /// Manages a group of radio-like options where only one can be selected at a time.
    /// </summary>
    public class DefaultGroupManager : IGroupManager
    {
        private readonly List<IGroupBoxOption> m_GroupOptions = new List<IGroupBoxOption>();
        private IGroupBoxOption m_SelectedOption;
        private IGroupBox m_GroupBox;

        public void Init(IGroupBox groupBox)
        {
            m_GroupBox = groupBox;
        }

        public IGroupBoxOption GetSelectedOption()
        {
            return m_SelectedOption;
        }

        public void OnOptionSelectionChanged(IGroupBoxOption selectedOption)
        {
            if (m_SelectedOption == selectedOption)
                return;

            m_SelectedOption = selectedOption;

            foreach (var option in m_GroupOptions)
            {
                option.SetSelected(option == m_SelectedOption);
            }
        }

        public void RegisterOption(IGroupBoxOption option)
        {
            if (!m_GroupOptions.Contains(option))
            {
                m_GroupOptions.Add(option);
                m_GroupBox?.OnOptionAdded(option);
                
                // If this is the first option, select it by default
                if (m_GroupOptions.Count == 1 && m_SelectedOption == null)
                {
                    m_SelectedOption = option;
                    option.SetSelected(true);
                }
            }
        }

        public void UnregisterOption(IGroupBoxOption option)
        {
            if (m_GroupOptions.Contains(option))
            {
                m_GroupOptions.Remove(option);
                m_GroupBox?.OnOptionRemoved(option);
                
                // If the selected option was removed, select another one if available
                if (m_SelectedOption == option)
                {
                    m_SelectedOption = null;
                    if (m_GroupOptions.Count > 0)
                    {
                        m_SelectedOption = m_GroupOptions[0];
                        m_SelectedOption.SetSelected(true);
                    }
                }
            }
        }
    }
}
