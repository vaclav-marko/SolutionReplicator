using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SolutionBuilder.Models;
using SolutionBuilder.Services;
using SolutionReplicator.Models;
using SolutionReplicator.Services;

namespace SolutionReplicator.ViewModels;

/// <summary>
/// Step 1 — pick the exported solution XML (the template) and its installation-data
/// folder, then parse metadata via the reused <see cref="XmlParserService"/>.
/// </summary>
public partial class LoadSolutionViewModel : WizardStepViewModel
{
    private readonly XmlParserService _parser = new();
    private readonly ScopeItemMapper _scopeMapper = new();

    [ObservableProperty] private string _solutionInfo = string.Empty;
    [ObservableProperty] private string _statusText = "Select the exported solution XML and click Load.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    private bool _isLoading;

    public LoadSolutionViewModel(WizardContext context) : base(context)
    {
        CanAdvance = false;
        Context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WizardContext.SourceXmlPath))
                LoadCommand.NotifyCanExecuteChanged();
        };
    }

    public override string Title => "Load solution";

    [RelayCommand]
    private void BrowseXml()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
            Title = "Select Exported Solution XML (template)"
        };
        if (dialog.ShowDialog() != true) return;

        Context.SourceXmlPath = dialog.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadAsync()
    {
        IsLoading = true;
        CanAdvance = false;
        StatusText = "Loading XML…";
        try
        {
            var progress = new System.Progress<string>(msg => StatusText = msg);
            var solution = await _parser.ParseSolutionMetadataAsync(Context.SourceXmlPath, progress);

            // XmlParserService reads SIs from SCENARIOS_SEQUENCE (ordering only) so their
            // BuildingBlockIds/descriptions are empty. The real SI→BB map lives in the
            // top-level SCENARIOS section — load it and rebuild the scope-item list.
            StatusText = "Mapping scope items to building blocks…";
            var sis = await System.Threading.Tasks.Task.Run(() => _scopeMapper.Map(Context.SourceXmlPath));
            if (sis.Count > 0)
            {
                solution.ScopeItems = sis.Select(s => new ScopeItem
                {
                    ProjectId = s.ProjectId,
                    SirSiId = s.SirSiId,
                    SirSiCountry = s.Country,
                    Description = s.Description,
                    BuildingBlockIds = s.BuildingBlockIds
                }).ToList();
            }

            Context.Solution = solution;
            Context.SourceCountry = solution.SourceCountry;
            if (string.IsNullOrWhiteSpace(Context.NewSolutionName))
                Context.NewSolutionName = solution.SolutionId;
            if (string.IsNullOrWhiteSpace(Context.TargetCountry))
                Context.TargetCountry = solution.SourceCountry;

            SolutionInfo = $"Solution: {solution.SolutionId}  |  Country: {solution.SourceCountry}  |  " +
                           $"Scope Items: {solution.ScopeItems.Count}  |  Building Blocks: {solution.BuildingBlocks.Count}";
            StatusText = $"Loaded {solution.ScopeItems.Count} scope items / {solution.BuildingBlocks.Count} building blocks.";
            CanAdvance = true;
        }
        catch (System.Exception ex)
        {
            StatusText = $"Error loading XML: {ex.Message}";
            SolutionInfo = string.Empty;
            Context.Solution = null;
            CanAdvance = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLoad() => !IsLoading && !string.IsNullOrWhiteSpace(Context.SourceXmlPath);
}
