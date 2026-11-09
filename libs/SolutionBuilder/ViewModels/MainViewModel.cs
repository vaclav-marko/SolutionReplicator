using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolutionBuilder.Models;
using SolutionBuilder.Services;

namespace SolutionBuilder.ViewModels;

/// <summary>
/// Main ViewModel for the application window.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly XmlParserService _parserService = new();
    private readonly SolutionProcessorService _processorService = new();
    private SolutionData? _solutionData;

    [ObservableProperty]
    private string _sourceXmlPath = string.Empty;

    [ObservableProperty]
    private string _sourceDataFolder = string.Empty;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private string _newSolutionName = string.Empty;

    [ObservableProperty]
    private string _targetCountry = string.Empty;

    [ObservableProperty]
    private string _sourceCountry = string.Empty;

    [ObservableProperty]
    private string _solutionInfo = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready. Load an XML file to begin.";

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private string _logText = string.Empty;

    public ObservableCollection<TreeNodeViewModel> TreeNodes { get; } = new();

    private ObservableCollection<TreeNodeViewModel> _allTreeNodes = new();

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter(value);
    }

    [RelayCommand]
    private void BrowseSourceXml()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
            Title = "Select Source Solution XML File"
        };

        if (dialog.ShowDialog() == true)
        {
            SourceXmlPath = dialog.FileName;

            // Auto-set source data folder to subfolder with same name as XML (without extension)
            string xmlDir = Path.GetDirectoryName(dialog.FileName) ?? "";
            string xmlNameNoExt = Path.GetFileNameWithoutExtension(dialog.FileName);
            string potentialDataFolder = Path.Combine(xmlDir, xmlNameNoExt);
            if (Directory.Exists(potentialDataFolder))
            {
                SourceDataFolder = potentialDataFolder;
            }
        }
    }

    [RelayCommand]
    private void BrowseSourceData()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Source Data Folder (contains TXT files)"
        };

        if (dialog.ShowDialog() == true)
        {
            SourceDataFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Output Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            OutputFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private async Task LoadXmlAsync()
    {
        if (string.IsNullOrEmpty(SourceXmlPath) || !File.Exists(SourceXmlPath))
        {
            StatusText = "Please select a valid XML file.";
            return;
        }

        IsProcessing = true;
        StatusText = "Loading XML...";
        LogText = "";
        TreeNodes.Clear();

        try
        {
            var progress = new Progress<string>(msg =>
            {
                StatusText = msg;
                AppendLog(msg);
            });

            _solutionData = await _parserService.ParseSolutionMetadataAsync(SourceXmlPath, progress);

            // Auto-detect source country
            SourceCountry = _solutionData.SourceCountry;
            SolutionInfo = $"Solution: {_solutionData.SolutionId} | Country: {_solutionData.SourceCountry} | " +
                           $"Scope Items: {_solutionData.ScopeItems.Count} | BBs: {_solutionData.BuildingBlocks.Count}";

            // Build tree
            BuildTree(_solutionData);

            StatusText = $"Loaded {_solutionData.ScopeItems.Count} scope items with {_solutionData.BuildingBlocks.Count} building blocks.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading XML: {ex.Message}";
            AppendLog($"ERROR: {ex}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task ProcessAsync()
    {
        // Validation
        if (_solutionData == null)
        {
            StatusText = "Please load an XML file first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewSolutionName))
        {
            StatusText = "Please enter a new solution name.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetCountry) || TargetCountry.Length != 2)
        {
            StatusText = "Please enter a valid 2-letter destination country code.";
            return;
        }

        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            StatusText = "Please select an output folder.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SourceDataFolder))
        {
            StatusText = "Please select the source data folder.";
            return;
        }

        // Collect selected items
        var selectedScopeItems = new HashSet<string>();
        var selectedBbIds = new HashSet<string>();

        foreach (var siNode in _allTreeNodes)
        {
            if (siNode.IsChecked)
            {
                selectedScopeItems.Add(siNode.Id);

                foreach (var bbNode in siNode.Children)
                {
                    if (bbNode.IsChecked)
                    {
                        selectedBbIds.Add(bbNode.Id);
                    }
                }
            }
            else
            {
                // Even if SI is not fully checked, include individually checked BBs
                foreach (var bbNode in siNode.Children)
                {
                    if (bbNode.IsChecked)
                    {
                        selectedScopeItems.Add(siNode.Id); // Must include SI if any BB is selected
                        selectedBbIds.Add(bbNode.Id);
                    }
                }
            }
        }

        if (selectedScopeItems.Count == 0)
        {
            StatusText = "Please select at least one scope item.";
            return;
        }

        IsProcessing = true;
        StatusText = "Processing...";
        AppendLog($"\n--- Processing started at {DateTime.Now:HH:mm:ss} ---");
        AppendLog($"New Solution: {NewSolutionName}");
        AppendLog($"Target Country: {TargetCountry}");
        AppendLog($"Selected SIs: {selectedScopeItems.Count}, BBs: {selectedBbIds.Count}");

        try
        {
            var progress = new Progress<string>(msg =>
            {
                StatusText = msg;
                AppendLog(msg);
            });

            await _processorService.ProcessAsync(
                SourceXmlPath,
                SourceDataFolder,
                OutputFolder,
                NewSolutionName,
                TargetCountry.ToUpperInvariant(),
                SourceCountry,
                selectedScopeItems,
                selectedBbIds,
                progress);

            StatusText = "Processing complete!";
            AppendLog($"--- Processing completed at {DateTime.Now:HH:mm:ss} ---");
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            AppendLog($"ERROR: {ex}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var node in _allTreeNodes)
        {
            node.IsChecked = true;
        }
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var node in _allTreeNodes)
        {
            node.IsChecked = false;
        }
    }

    private void BuildTree(SolutionData solution)
    {
        _allTreeNodes.Clear();
        TreeNodes.Clear();

        // Create a lookup for building blocks
        var bbLookup = solution.BuildingBlocks.ToDictionary(bb => bb.BbId, bb => bb);

        foreach (var si in solution.ScopeItems.OrderBy(s => s.ProjectId))
        {
            var siNode = new TreeNodeViewModel
            {
                Id = si.ProjectId,
                DisplayText = $"[{si.ProjectId}] {si.Description}",
                NodeType = "SI",
                IsChecked = true
            };

            // Add building blocks as children
            foreach (var bbId in si.BuildingBlockIds.Distinct())
            {
                string bbDesc = "";
                if (bbLookup.TryGetValue(bbId, out var bb))
                {
                    bbDesc = bb.Description;
                }

                var bbNode = new TreeNodeViewModel
                {
                    Id = bbId,
                    DisplayText = $"[{bbId}] {bbDesc}",
                    NodeType = "BB",
                    IsChecked = true,
                    Parent = siNode
                };

                siNode.Children.Add(bbNode);
            }

            _allTreeNodes.Add(siNode);
            TreeNodes.Add(siNode);
        }
    }

    private void ApplyFilter(string filter)
    {
        TreeNodes.Clear();

        if (string.IsNullOrWhiteSpace(filter))
        {
            foreach (var node in _allTreeNodes)
                TreeNodes.Add(node);
            return;
        }

        string lowerFilter = filter.ToLowerInvariant();

        foreach (var node in _allTreeNodes)
        {
            if (node.DisplayText.Contains(lowerFilter, StringComparison.OrdinalIgnoreCase) ||
                node.Id.Contains(lowerFilter, StringComparison.OrdinalIgnoreCase) ||
                node.Children.Any(c => c.DisplayText.Contains(lowerFilter, StringComparison.OrdinalIgnoreCase) ||
                                       c.Id.Contains(lowerFilter, StringComparison.OrdinalIgnoreCase)))
            {
                TreeNodes.Add(node);
            }
        }
    }

    private void AppendLog(string message)
    {
        LogText += message + "\n";
    }
}