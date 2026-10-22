using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SapVariantReplacer.Views;
using SolutionReplicator.Models;
using SolutionReplicator.Services;

namespace SolutionReplicator.ViewModels;

/// <summary>
/// One editable grid row: variable + field are read-only, the value for the chosen system is
/// editable. Writes straight back into the underlying CSV <see cref="PlaceholderMapping"/> row.
/// </summary>
public sealed partial class TokenRowViewModel : ObservableObject
{
    private readonly PlaceholderMapping _row;

    public TokenRowViewModel(PlaceholderMapping row)
    {
        _row = row;
    }

    public string Token => _row.Variable;
    public string FieldName => _row.Field;

    public string Value
    {
        get => _row.Value;
        set { if (_row.Value != value) { _row.Value = value ?? string.Empty; OnPropertyChanged(); } }
    }
}

/// <summary>
/// Step 4 — choose the target system (a SysId in the CSV), fill in / edit the real value for each
/// placeholder variable, and save the nomenclature CSV. On leave, builds the compose change-set for
/// the chosen system.
/// </summary>
public partial class ProfileValuesViewModel : WizardStepViewModel
{
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _selectedProfile = string.Empty;

    public ObservableCollection<string> Profiles { get; } = new();
    public ObservableCollection<TokenRowViewModel> Rows { get; } = new();

    public ProfileValuesViewModel(WizardContext context) : base(context)
    {
        CanAdvance = false;
    }

    public override string Title => "Target system & values";

    public override Task OnEnterAsync()
    {
        Profiles.Clear();
        Rows.Clear();

        if (Context.PlaceholderTable is null)
        {
            StatusText = "No nomenclature table loaded.";
            return Task.CompletedTask;
        }

        foreach (var sys in Context.PlaceholderTable.Systems) Profiles.Add(sys);

        // Default to remembered system, else the first, else none.
        var remembered = AppSettingsStore.Load().LastProfile;
        SelectedProfile = Profiles.Contains(remembered) ? remembered
                        : Profiles.FirstOrDefault() ?? string.Empty;

        RebuildRows();
        return Task.CompletedTask;
    }

    public override Task<bool> OnLeaveAsync()
    {
        if (Context.PlaceholderTable is null || string.IsNullOrWhiteSpace(SelectedProfile))
            return Task.FromResult(false);

        Context.SelectedProfile = SelectedProfile;
        Context.FieldEntries = CatalogComposer.BuildChangeSet(Context.PlaceholderTable, SelectedProfile);
        return Task.FromResult(true);
    }

    partial void OnSelectedProfileChanged(string value) => RebuildRows();

    [RelayCommand]
    private void AddProfile()
    {
        if (Context.PlaceholderTable is null) return;

        var dialog = new InputDialog("Add target system", "New target system id (SysId):");
        if (dialog.ShowDialog() != true) return;

        var name = (dialog.ResponseText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (Context.PlaceholderTable.Systems.Any(s => s.Equals(name, System.StringComparison.OrdinalIgnoreCase)))
        {
            SelectedProfile = name; // already exists — just switch to it
            return;
        }

        if (!Profiles.Contains(name)) Profiles.Add(name);
        SelectedProfile = name; // RebuildRows seeds empty rows for the new system
    }

    [RelayCommand]
    private void SaveCatalog()
    {
        if (Context.PlaceholderTable is null || string.IsNullOrWhiteSpace(Context.CatalogPath))
        {
            StatusText = "No nomenclature CSV path set — cannot save.";
            return;
        }
        try
        {
            PlaceholderTableStore.Save(Context.CatalogPath, Context.PlaceholderTable);
            var s = AppSettingsStore.Load();
            s.LastCatalogPath = Context.CatalogPath;
            s.LastProfile = SelectedProfile;
            AppSettingsStore.Save(s);
            StatusText = $"Nomenclature CSV saved to {Context.CatalogPath}.";
        }
        catch (System.Exception ex)
        {
            StatusText = $"Error saving CSV: {ex.Message}";
        }
    }

    private void RebuildRows()
    {
        Rows.Clear();
        var table = Context.PlaceholderTable;
        if (table is null || string.IsNullOrWhiteSpace(SelectedProfile))
        {
            CanAdvance = false;
            return;
        }

        // Ensure every discovered token has a row for this system (empty value to fill in).
        CatalogComposer.SeedMissingTokens(table, Context.DiscoveredTokens, SelectedProfile);

        foreach (var row in table.Rows
                     .Where(r => r.SysId.Equals(SelectedProfile, System.StringComparison.OrdinalIgnoreCase))
                     .OrderBy(r => r.Field).ThenBy(r => r.Variable))
            Rows.Add(new TokenRowViewModel(row));

        int mapped = Rows.Count(r => !string.IsNullOrEmpty(r.Value));
        StatusText = $"{Rows.Count} variable(s) for '{SelectedProfile}'; {mapped} with a value.";
        CanAdvance = Rows.Count > 0;
    }
}
