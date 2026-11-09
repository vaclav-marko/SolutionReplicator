using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SapVariantReplacer.Models;
using SapVariantReplacer.Services;
using SapVariantReplacer.Views;

namespace SapVariantReplacer.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private bool _copyUnmodified = true;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 100;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchReplaceCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _showChangedOnly;

    private CancellationTokenSource? _cts;

    public ObservableCollection<FieldValueEntry> FieldEntries { get; } = [];

    private List<VariantFile> _parsedFiles = [];

    private ICollectionView? _fieldEntriesView;
    public ICollectionView FieldEntriesView
    {
        get
        {
            _fieldEntriesView ??= CollectionViewSource.GetDefaultView(FieldEntries);
            return _fieldEntriesView;
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnShowChangedOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var hasTextFilter = !string.IsNullOrWhiteSpace(FilterText);
        if (!hasTextFilter && !ShowChangedOnly)
        {
            FieldEntriesView.Filter = null;
            return;
        }

        var text = FilterText;
        var changedOnly = ShowChangedOnly;
        FieldEntriesView.Filter = obj =>
        {
            if (obj is not FieldValueEntry e) return false;
            if (changedOnly && !e.IsChanged) return false;
            if (hasTextFilter)
            {
                return e.FieldName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                       e.OriginalValue.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                       e.NewValue.Contains(text, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        };
    }

    [RelayCommand]
    private void BrowseSource()
    {
        var dlg = new OpenFolderDialog { Title = "Select source folder containing variant files" };
        if (dlg.ShowDialog() == true)
            SourcePath = dlg.FolderName;
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var dlg = new OpenFolderDialog { Title = "Select output folder" };
        if (dlg.ShowDialog() == true)
            OutputPath = dlg.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "Cancelling…";
    }

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusMessage = "Scanning files…";
        FieldEntries.Clear();
        _parsedFiles.Clear();
        ProgressValue = 0;
        ProgressMax = 1;

        try
        {
            var sourcePath = SourcePath;
            var ct = _cts.Token;

            var scanProgress = new Progress<(int Current, int Total, string FileName)>(p =>
            {
                ProgressMax = p.Total;
                ProgressValue = p.Current;
                StatusMessage = $"Loading file {p.Current} of {p.Total}: {p.FileName}";
            });

            var (files, errors) = await Task.Run(() =>
                VariantFileParser.ParseFolder(sourcePath, scanProgress, ct), ct);

            _parsedFiles = files;
            StatusMessage = $"Extracting unique parameters from {files.Count} file(s)…";

            var entries = await Task.Run(() => FieldExtractor.Extract(files), ct);

            foreach (var entry in entries)
                FieldEntries.Add(entry);

            ApplyCommand.NotifyCanExecuteChanged();
            SearchReplaceCommand.NotifyCanExecuteChanged();

            var status = $"Found {FieldEntries.Count} unique parameters across {_parsedFiles.Count} file(s).";
            if (errors.Count > 0)
                status += $" ({errors.Count} file(s) skipped due to errors.)";
            StatusMessage = status;

            if (errors.Count > 0)
            {
                var errorList = string.Join("\n", errors.Take(20));
                if (errors.Count > 20)
                    errorList += $"\n… and {errors.Count - 20} more.";
                MessageBox.Show(errorList, $"{errors.Count} File(s) Skipped",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanScan() => !string.IsNullOrWhiteSpace(SourcePath) && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var changes = FieldEntries.Where(e => e.IsChanged).ToList();
        if (changes.Count == 0)
        {
            MessageBox.Show("No values have been changed.", "Nothing to apply",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Dry-run preview.
        var (filesAffected, totalReplacements) = ReplacementEngine.Preview(_parsedFiles, changes);

        var msg = $"{totalReplacements} replacement(s) in {filesAffected} file(s) will be applied.\n";
        if (CopyUnmodified)
            msg += $"{_parsedFiles.Count - filesAffected} unmodified file(s) will be copied as-is.\n";
        msg += $"\nOutput folder: {OutputPath}\n\nProceed?";

        var result = MessageBox.Show(msg, "Confirm Changes",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (result != MessageBoxResult.OK)
            return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        ProgressMax = _parsedFiles.Count;
        ProgressValue = 0;
        StatusMessage = "Applying changes…";

        try
        {
            var outputPath = OutputPath;
            var copyUnmod = CopyUnmodified;
            var allEntries = FieldEntries.ToList();
            var parsedFiles = _parsedFiles;
            var ct = _cts.Token;

            var progress = new Progress<(int Current, int Total)>(p =>
            {
                ProgressValue = p.Current;
            });

            int modifiedCount = await Task.Run(() =>
                ReplacementEngine.Apply(parsedFiles, allEntries, outputPath, copyUnmod, progress, ct), ct);

            StatusMessage = $"Done. {modifiedCount} file(s) modified" +
                            (copyUnmod ? $", {_parsedFiles.Count - modifiedCount} copied unchanged." : ".");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Apply cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanApply() =>
        !string.IsNullOrWhiteSpace(OutputPath) && !IsBusy && FieldEntries.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSearchReplace))]
    private void SearchReplace()
    {
        if (string.IsNullOrEmpty(SearchText)) return;

        int count = 0;
        foreach (var entry in FieldEntries)
        {
            if (entry.NewValue.Contains(SearchText, StringComparison.Ordinal))
            {
                entry.NewValue = entry.NewValue.Replace(SearchText, ReplaceText, StringComparison.Ordinal);
                count++;
            }
        }

        if (count > 0)
            FieldEntriesView.Refresh();

        StatusMessage = count > 0
            ? $"Replaced '{SearchText}' → '{ReplaceText}' in {count} row(s)."
            : $"No matches found for '{SearchText}'.";
    }

    private bool CanSearchReplace() => !string.IsNullOrWhiteSpace(SearchText) && FieldEntries.Count > 0;

    [RelayCommand]
    private void BulkSetFieldValue(FieldValueEntry? entry)
    {
        if (entry is null) return;
        var fieldName = entry.FieldName;

        var dialog = new InputDialog("Bulk Set Value", $"Enter new value for all '{fieldName}' rows:")
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            var newValue = dialog.ResponseText;
            int count = 0;
            foreach (var e in FieldEntries)
            {
                if (e.FieldName == fieldName)
                {
                    e.NewValue = newValue;
                    count++;
                }
            }

            FieldEntriesView.Refresh();
            StatusMessage = $"Updated {count} '{fieldName}' row(s).";
        }
    }
}
