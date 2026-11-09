using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SolutionBuilder.ViewModels;

/// <summary>
/// Represents a node in the tree view (either a Scope Item or Building Block).
/// </summary>
public partial class TreeNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _displayText = string.Empty;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _nodeType = string.Empty; // "SI" or "BB"

    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    public TreeNodeViewModel? Parent { get; set; }

    partial void OnIsCheckedChanged(bool value)
    {
        // Propagate to children
        if (NodeType == "SI")
        {
            foreach (var child in Children)
            {
                child.IsChecked = value;
            }
        }

        // Notify parent
        if (NodeType == "BB" && Parent != null)
        {
            Parent.UpdateCheckStateFromChildren();
        }
    }

    private bool _updatingFromChildren;

    public void UpdateCheckStateFromChildren()
    {
        if (_updatingFromChildren) return;
        _updatingFromChildren = true;

        if (Children.Count > 0)
        {
            bool allChecked = Children.All(c => c.IsChecked);
            bool anyChecked = Children.Any(c => c.IsChecked);

            // For simplicity, if any child is checked, parent is checked
            IsChecked = anyChecked;
        }

        _updatingFromChildren = false;
    }
}