using System.Collections.Generic;
using UnityEngine.UIElements;

namespace colloid.PBReplacer
{
    /// <summary>
    /// Utility methods for working with IGroupBox and IGroupBoxOption.
    /// </summary>
    public static class GroupBoxUtility
    {
        // Dictionary to cache group managers for group boxes
        private static readonly Dictionary<IGroupBox, IGroupManager> s_GroupManagers = 
            new Dictionary<IGroupBox, IGroupManager>();
            
        // Dictionary to cache panel managers for options without group boxes
        private static readonly Dictionary<IPanel, IGroupManager> s_PanelManagers = 
            new Dictionary<IPanel, IGroupManager>();

        /// <summary>
        /// Registers an option to its closest parent group box.
        /// </summary>
        /// <param name="option">The option to register.</param>
        public static void RegisterGroupBoxOption(this IGroupBoxOption option)
        {
            if (option is VisualElement ve)
            {
                var manager = FindGroupManager(ve);
                if (manager != null)
                {
                    manager.RegisterOption(option);
                }
            }
        }

        /// <summary>
        /// Unregisters an option from its closest parent group box.
        /// </summary>
        /// <param name="option">The option to unregister.</param>
        public static void UnregisterGroupBoxOption(this IGroupBoxOption option)
        {
            if (option is VisualElement ve)
            {
                var manager = FindGroupManager(ve);
                if (manager != null)
                {
                    manager.UnregisterOption(option);
                }
            }
        }

        /// <summary>
        /// Notifies the group manager that this option has been selected.
        /// </summary>
        /// <param name="option">The option that has been selected.</param>
        public static void OnOptionSelected(this IGroupBoxOption option)
        {
            if (option is VisualElement ve)
            {
                var manager = FindGroupManager(ve);
                if (manager != null)
                {
                    manager.OnOptionSelectionChanged(option);
                }
            }
        }

        /// <summary>
        /// Finds the closest group manager for a visual element.
        /// </summary>
        /// <param name="ve">The visual element to find a group manager for.</param>
        /// <returns>The closest group manager, or null if none was found.</returns>
        private static IGroupManager FindGroupManager(VisualElement ve)
        {
            // First, try to find a parent IGroupBox
            var parent = ve;
            while (parent != null)
            {
                if (parent is IGroupBox groupBox)
                {
                    return GetOrCreateGroupManager(groupBox);
                }
                parent = parent.hierarchy.parent;
            }

            // If no group box was found, use the panel as a default container
            var panel = ve.panel;
            if (panel != null)
            {
                return GetOrCreatePanelManager(panel);
            }

            return null;
        }

        /// <summary>
        /// Gets or creates a group manager for the specified group box.
        /// </summary>
        /// <param name="groupBox">The group box to get a manager for.</param>
        /// <returns>The group manager for this group box.</returns>
        private static IGroupManager GetOrCreateGroupManager(IGroupBox groupBox)
        {
            if (!s_GroupManagers.TryGetValue(groupBox, out var manager))
            {
                manager = new DefaultGroupManager();
                manager.Init(groupBox);
                s_GroupManagers[groupBox] = manager;
            }
            return manager;
        }
        
        /// <summary>
        /// Gets or creates a group manager for the specified panel.
        /// </summary>
        /// <param name="panel">The panel to get a manager for.</param>
        /// <returns>The group manager for this panel.</returns>
        private static IGroupManager GetOrCreatePanelManager(IPanel panel)
        {
            if (!s_PanelManagers.TryGetValue(panel, out var manager))
            {
                // Create a dummy group box that does nothing
                var dummyGroupBox = new DummyGroupBox();
                manager = new DefaultGroupManager();
                manager.Init(dummyGroupBox);
                s_PanelManagers[panel] = manager;
            }
            return manager;
        }
        
        // A dummy implementation of IGroupBox that does nothing
        private class DummyGroupBox : IGroupBox
        {
            public void OnOptionAdded(IGroupBoxOption option) { }
            public void OnOptionRemoved(IGroupBoxOption option) { }
        }
    }
}
