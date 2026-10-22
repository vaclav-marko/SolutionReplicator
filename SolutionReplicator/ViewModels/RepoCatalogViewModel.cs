using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SapVariantReplacer.Services;
using SolutionReplicator.Models;
using SolutionReplicator.Services;

namespace SolutionReplicator.ViewModels;

/// <summary>
/// Step 3 — point at the client repository folder (tokenized TXT files) and the central
/// placeholder catalog. On selection, the repo is parsed once, tokens are auto-discovered and
/// any new ones are seeded into the catalog (existing values untouched).
/// </summary>
public partial class RepoCatalogViewModel : WizardStepViewModel
{
    [ObservableProperty] private string _statusText = "Select the client repository folder and the placeholder catalog.";
    [ObservableProperty] private string _discoveredSummary = string.Empty;
    [ObservableProperty] private bool _isScanning;

    public RepoCatalogViewModel(WizardContext context) : base(context)
    {
        CanAdvance = false;
    }

    public override string Title => "Repository & placeholder catalog";

    public override Task OnEnterAsync()
    {
        // Pre-fill from remembered settings if the fields are empty.
        var s = AppSettingsStore.Load();
        if (string.IsNullOrWhiteSpace(Context.RepoFolder) && !string.IsNullOrWhiteSpace(s.LastRepoFolder))
            Context.RepoFolder = s.LastRepoFolder;
        if (string.IsNullOrWhiteSpace(Context.CatalogPath) && !string.IsNullOrWhiteSpace(s.LastCatalogPath))
            Context.CatalogPath = s.LastCatalogPath;

        if (!string.IsNullOrWhiteSpace(Context.RepoFolder) && Directory.Exists(Context.RepoFolder))
            _ = ScanAsync();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void BrowseRepo()
    {
        var dialog = new OpenFolderDialog { Title = "Select client repository folder (tokenized TXT files)" };
        if (dialog.ShowDialog() == true)
        {
            Context.RepoFolder = dialog.FolderName;
            _ = ScanAsync();
        }
    }

    [RelayCommand]
    private void BrowseCatalog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Nomenclature CSV (*.csv)|*.csv|All Files (*.*)|*.*",
            Title = "Select or name the nomenclature CSV file",
            CheckFileExists = false // allow pointing at a not-yet-created catalog
        };
        if (dialog.ShowDialog() == true)
        {
            Context.CatalogPath = dialog.FileName;
            _ = ScanAsync();
        }
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(Context.RepoFolder) || !Directory.Exists(Context.RepoFolder))
        {
            CanAdvance = false;
            return;
        }

        IsScanning = true;
        StatusText = "Scanning repository for placeholder tokens…";
        try
        {
            var (discovered, files, table) = await Task.Run(() =>
            {
                var disc = TokenScanner.Scan(Context.RepoFolder);
                var (parsed, _) = VariantFileParser.ParseFolder(Context.RepoFolder);
                var tbl = PlaceholderTableStore.Load(Context.CatalogPath);
                return (disc, parsed, tbl);
            });

            Context.RepoVariantFiles = files;
            Context.PlaceholderTable = table;
            Context.DiscoveredTokens = discovered.ToList();

            int txtCount = Directory.GetFiles(Context.RepoFolder, "*.TXT", SearchOption.TopDirectoryOnly).Length;
            bool tableExisted = !string.IsNullOrWhiteSpace(Context.CatalogPath) && File.Exists(Context.CatalogPath);

            DiscoveredSummary =
                $"Repository files : {txtCount} TXT\n" +
                $"Tokens found     : {discovered.Count} distinct\n" +
                $"Nomenclature CSV : {(tableExisted ? $"loaded ({table.Rows.Count} row(s), {table.Systems.Count} system(s))" : "new (will be created on save)")}" +
                (discovered.Count > 0
                    ? "\n\n" + string.Join("\n", discovered.Take(20).Select(d => $"  {d.Token}  →  {d.FieldName}  (x{d.OccurrenceCount})"))
                    : "");

            StatusText = txtCount == 0
                ? "No TXT files in the selected folder."
                : $"Found {discovered.Count} token(s) across {txtCount} file(s).";
            CanAdvance = txtCount > 0;
        }
        catch (System.Exception ex)
        {
            StatusText = $"Error scanning repository: {ex.Message}";
            CanAdvance = false;
        }
        finally
        {
            IsScanning = false;
        }
    }
}
