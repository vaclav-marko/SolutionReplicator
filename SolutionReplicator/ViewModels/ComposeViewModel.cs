using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SapVariantReplacer.Services;
using SolutionBuilder.Services;
using SolutionReplicator.Models;
using SolutionReplicator.Services;

namespace SolutionReplicator.ViewModels;

/// <summary>
/// Step 5 (final) — compose the package: resolve which files the ticked scope consumes, match them
/// to the client repo, substitute the chosen profile's values into the placeholders, and write the
/// re-importable solution XML plus the populated TXT files. SAP import + activation stay manual.
/// </summary>
public partial class ComposeViewModel : WizardStepViewModel
{
    private readonly ConsumptionResolver _resolver = new();
    private readonly SolutionProcessorService _processor = new();

    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private bool _renameSolution;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isProcessing;

    [ObservableProperty] private bool _done;

    public ComposeViewModel(WizardContext context) : base(context)
    {
        CanAdvance = false; // terminal step
    }

    public override string Title => "Compose & export";

    public override Task OnEnterAsync()
    {
        // Pre-fill output folder from remembered settings.
        if (string.IsNullOrWhiteSpace(Context.OutputFolder))
        {
            var last = AppSettingsStore.Load().LastOutputFolder;
            if (!string.IsNullOrWhiteSpace(last)) Context.OutputFolder = last;
        }

        int changed = Context.FieldEntries.Count(e => e.IsChanged);
        Summary =
            $"Template XML   : {Path.GetFileName(Context.SourceXmlPath)}\n" +
            $"Solution id    : {Context.Solution?.SolutionId}" +
            (RenameSolution && !string.IsNullOrWhiteSpace(Context.NewSolutionName)
                ? $"  →  {Context.NewSolutionName}\n"
                : "  (unchanged)\n") +
            $"Scope items    : {Context.SelectedScopeItemIds.Count}\n" +
            $"Building blocks: {Context.SelectedBbIds.Count}\n" +
            $"Repository     : {Context.RepoFolder}\n" +
            $"Target system  : {Context.SelectedProfile}\n" +
            $"Value mappings : {changed} token(s) will be substituted\n" +
            $"Output folder  : {Context.OutputFolder}";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var dialog = new OpenFolderDialog { Title = "Select output folder for the composed package" };
        if (dialog.ShowDialog() == true)
            Context.OutputFolder = dialog.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (Context.Solution is null || string.IsNullOrWhiteSpace(Context.OutputFolder))
        {
            Append("Cannot compose: solution not loaded or no output folder chosen.");
            return;
        }

        IsProcessing = true;
        Done = false;
        LogText = string.Empty;

        try
        {
            string country = Context.SourceCountry; // slice keeps the template's country
            var progress = new System.Progress<string>(Append);

            // 1. Which files does the ticked scope consume?
            Append("Resolving consumed files for the selected scope…");
            var consumption = await Task.Run(() =>
                _resolver.Resolve(Context.SourceXmlPath, Context.SelectedBbIds, Context.SelectedScopeItemIds));
            Context.Consumption = consumption;
            Append($"  consumed files: {consumption.ConsumedFiles.Count}");

            // 2. Match consumed files to the client repository (exact name).
            var match = RepoFileMatcher.Match(consumption.ConsumedFiles, Context.RepoFolder);
            Append($"  matched in repo: {match.MatchedCount}, unmatched (no repo file): {match.UnmatchedCount}");

            if (match.MatchedCount == 0)
            {
                Append("\nERROR: no repository files matched the selected scope. " +
                       "Check that the repo folder was exported for this template.");
                return;
            }

            // 3. Prepare the re-importable solution XML (optionally renamed).
            Append("Preparing solution XML…");
            string solutionName = RenameSolution && !string.IsNullOrWhiteSpace(Context.NewSolutionName)
                ? Context.NewSolutionName
                : Context.Solution.SolutionId;
            var prepared = await _processor.PreparePackageAsync(
                Context.SourceXmlPath, Context.OutputFolder,
                solutionName, country, country,
                Context.SelectedScopeItemIds, Context.SelectedBbIds,
                renameSolution: RenameSolution, progress);

            // 4. Parse only the matched repo files, then substitute the profile's values.
            Append("Substituting placeholder values…");
            var matchedFiles = Context.RepoVariantFiles
                .Where(f => match.MatchedFilePaths.Contains(f.FilePath, System.StringComparer.OrdinalIgnoreCase))
                .ToList();
            // RepoVariantFiles was parsed in step 3; fall back to parsing matched paths if empty.
            if (matchedFiles.Count == 0)
                matchedFiles = match.MatchedFilePaths
                    .Select(VariantFileParser.ParseFile)
                    .Where(f => f is not null)!
                    .ToList<SapVariantReplacer.Models.VariantFile>();

            int modified = await Task.Run(() => ReplacementEngine.Apply(
                matchedFiles, Context.FieldEntries, prepared.OutputDataFolder, copyUnmodified: true));

            Append("\nDONE.");
            Append($"  XML          : {prepared.OutputXmlPath}");
            Append($"  Data folder  : {prepared.OutputDataFolder}");
            Append($"  Files written: {matchedFiles.Count} ({modified} with substitutions)");
            if (match.UnmatchedCount > 0)
                Append($"  NOTE: {match.UnmatchedCount} consumed file(s) had no repo match and were skipped.");
            Append("\nNext: import the generated solution into SAP Solution Builder and activate it manually.");

            // Remember paths for next run.
            var s = AppSettingsStore.Load();
            s.LastTemplateXml = Context.SourceXmlPath;
            s.LastRepoFolder = Context.RepoFolder;
            s.LastCatalogPath = Context.CatalogPath;
            s.LastOutputFolder = Context.OutputFolder;
            s.LastProfile = Context.SelectedProfile;
            AppSettingsStore.Save(s);

            Done = true;
        }
        catch (System.Exception ex)
        {
            Append($"ERROR: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanRun() => !IsProcessing;

    private void Append(string line) => LogText += line + "\n";
}
