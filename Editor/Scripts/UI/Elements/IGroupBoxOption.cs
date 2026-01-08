using UnityEngine.UIElements;

namespace colloid.PBReplacer
{
    /// <summary>
    /// Interface for options that need to be handled together in a group box.
    /// Visual elements should inherit from this interface in order to be treated as a group.
    /// </summary>
	public interface IGroupBoxOption
    {
        /// <summary>
        /// Sets the selected state for this element.
        /// </summary>
        /// <param name="selected">If the option should be displayed as selected.</param>
        void SetSelected(bool selected);
    }

    /// <summary>
    /// Interface for a group box that contains group options.
    /// </summary>
    public interface IGroupBox
    {
        void OnOptionAdded(IGroupBoxOption option);
        void OnOptionRemoved(IGroupBoxOption option);
    }

    /// <summary>
    /// Interface for managing groups of options.
    /// </summary>
    public interface IGroupManager
    {
        void Init(IGroupBox groupBox);
        IGroupBoxOption GetSelectedOption();
        void OnOptionSelectionChanged(IGroupBoxOption selectedOption);
        void RegisterOption(IGroupBoxOption option);
        void UnregisterOption(IGroupBoxOption option);
    }
}
