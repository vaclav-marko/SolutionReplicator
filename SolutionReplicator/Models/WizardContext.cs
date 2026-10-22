using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using SolutionBuilder.Models;
using SapVariantReplacer.Models;

namespace SolutionReplicator.Models;

/// <summary>
/// Mutable state shared across all wizard steps. Constructed once by the
/// <see cref="ViewModels.WizardHostViewModel"/> and handed to every step.
/// </summary>
public sealed partial class WizardContext : ObservableObject
{
    // ----- Step 1: Load -------------------------------------------------
    [ObservableProperty] private string _sourceXmlPath = string.Empty;

    /// <summary>Parsed solution metadata.</summary>
    public SolutionData? Solution { get; set; }

    // ----- Step 2: Select ----------------------------------------------
    public HashSet<string> SelectedScopeItemIds { get; } = new();
    public HashSet<string> SelectedBbIds { get; } = new();

    /// <summary>Consumed files + parameters for the current selection (Step 2 output).</summary>
    public ConsumptionResult? Consumption { get; set; }

    // ----- Step 3: Repository + catalog --------------------------------
    /// <summary>Client repository folder holding the tokenized TXT files.</summary>
    [ObservableProperty] private string _repoFolder = string.Empty;

    /// <summary>Path to the central placeholder table (nomenclature) CSV file.</summary>
    [ObservableProperty] private string _catalogPath = string.Empty;

    /// <summary>The loaded placeholder table (variable;field;value;sysid rows).</summary>
    public PlaceholderTable? PlaceholderTable { get; set; }

    /// <summary>Parsed variant files from the repo folder (parsed once in the RepoCatalog step).</summary>
    public List<VariantFile> RepoVariantFiles { get; set; } = new();

    /// <summary>Tokens discovered in the repo (so the ProfileValues step can seed the table per system).</summary>
    public List<Services.DiscoveredToken> DiscoveredTokens { get; set; } = new();

    // ----- Step 4: Profile + values ------------------------------------
    /// <summary>Chosen target system (SysId column in the table).</summary>
    [ObservableProperty] private string _selectedProfile = string.Empty;

    /// <summary>Change-set built from (tokens × chosen profile), fed to ReplacementEngine.</summary>
    public List<FieldValueEntry> FieldEntries { get; set; } = new();

    // ----- Step 1 defaults (kept for the reused Load step) -------------
    [ObservableProperty] private string _newSolutionName = string.Empty;
    [ObservableProperty] private string _sourceCountry = string.Empty;
    [ObservableProperty] private string _targetCountry = string.Empty;

    // ----- Step 5: Compose ---------------------------------------------
    [ObservableProperty] private string _outputFolder = string.Empty;
}
