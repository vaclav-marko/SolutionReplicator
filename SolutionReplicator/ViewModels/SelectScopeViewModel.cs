using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolutionBuilder.Models;
using SolutionBuilder.ViewModels;
using SolutionReplicator.Models;

namespace SolutionReplicator.ViewModels;

/// <summary>
/// Step 2 — mark the scope items / building blocks to deploy. Builds the SI→BB tree
/// from the loaded solution (reusing <see cref="TreeNodeViewModel"/>) and, on leave,
/// collects the selection into the shared context. Works for custom Z ids (plain
/// string matching). Consumption resolution is wired in increment 2.
/// </summary>
public partial class SelectScopeViewModel : WizardStepViewModel
{
    private readonly List<TreeNodeViewModel> _allTreeNodes = new();

    [ObservableProperty] private string _searchFilter = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;

    public ObservableCollection<TreeNodeViewModel> TreeNodes { get; } = new();

    public SelectScopeViewModel(WizardContext context) : base(context)
    {
    }

    public override string Title => "Select scope items & building blocks";

    public override Task OnEnterAsync()
    {
        BuildTree();
        UpdateCanAdvance();
        return Task.CompletedTask;
    }

    public override Task<bool> OnLeaveAsync()
    {
        CollectSelection();
        return Task.FromResult(Context.SelectedScopeItemIds.Count > 0);
    }

    partial void OnSearchFilterChanged(string value) => ApplyFilter(value);

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var node in _allTreeNodes) node.IsChecked = true;
        UpdateCanAdvance();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var node in _allTreeNodes) node.IsChecked = false;
        UpdateCanAdvance();
    }

    private void BuildTree()
    {
        if (Context.Solution is null) return;
        _allTreeNodes.Clear();
        TreeNodes.Clear();

        var bbLookup = Context.Solution.BuildingBlocks
            .GroupBy(bb => bb.BbId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var si in Context.Solution.ScopeItems.OrderBy(s => s.ProjectId))
        {
            var siNode = new TreeNodeViewModel
            {
                Id = si.ProjectId,
                DisplayText = $"[{si.ProjectId}] {si.Description}",
                NodeType = "SI",
                IsChecked = false
            };

            foreach (var bbId in si.BuildingBlockIds.Distinct())
            {
                string bbDesc = bbLookup.TryGetValue(bbId, out var bb) ? bb.Description : "";
                var bbNode = new TreeNodeViewModel
                {
                    Id = bbId,
                    DisplayText = $"[{bbId}] {bbDesc}",
                    NodeType = "BB",
                    IsChecked = false,
                    Parent = siNode
                };
                bbNode.PropertyChanged += OnNodeCheckChanged;
                siNode.Children.Add(bbNode);
            }

            siNode.PropertyChanged += OnNodeCheckChanged;
            _allTreeNodes.Add(siNode);
            TreeNodes.Add(siNode);
        }
    }

    private void OnNodeCheckChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TreeNodeViewModel.IsChecked))
            UpdateCanAdvance();
    }

    private void UpdateCanAdvance()
    {
        CanAdvance = _allTreeNodes.Any(si => si.IsChecked || si.Children.Any(bb => bb.IsChecked));
    }

    private void CollectSelection()
    {
        Context.SelectedScopeItemIds.Clear();
        Context.SelectedBbIds.Clear();

        foreach (var siNode in _allTreeNodes)
        {
            bool anyBb = false;
            foreach (var bbNode in siNode.Children.Where(b => b.IsChecked))
            {
                Context.SelectedBbIds.Add(bbNode.Id);
                anyBb = true;
            }
            if (siNode.IsChecked || anyBb)
                Context.SelectedScopeItemIds.Add(siNode.Id);
        }

        StatusText = $"Selected {Context.SelectedScopeItemIds.Count} scope item(s), {Context.SelectedBbIds.Count} building block(s).";
    }

    private void ApplyFilter(string filter)
    {
        TreeNodes.Clear();
        if (string.IsNullOrWhiteSpace(filter))
        {
            foreach (var node in _allTreeNodes) TreeNodes.Add(node);
            return;
        }

        foreach (var node in _allTreeNodes)
        {
            bool match = node.DisplayText.Contains(filter, System.StringComparison.OrdinalIgnoreCase)
                         || node.Id.Contains(filter, System.StringComparison.OrdinalIgnoreCase)
                         || node.Children.Any(c =>
                                c.DisplayText.Contains(filter, System.StringComparison.OrdinalIgnoreCase)
                                || c.Id.Contains(filter, System.StringComparison.OrdinalIgnoreCase));
            if (match) TreeNodes.Add(node);
        }
    }
}
