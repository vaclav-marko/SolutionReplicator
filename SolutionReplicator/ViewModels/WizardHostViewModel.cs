using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolutionReplicator.Models;

namespace SolutionReplicator.ViewModels;

/// <summary>
/// Drives wizard navigation: owns the ordered step list, the current step, and the
/// Back/Next/Cancel commands. Advancing runs the current step's OnLeaveAsync, then the
/// next step's OnEnterAsync.
/// </summary>
public partial class WizardHostViewModel : ObservableObject
{
    public WizardContext Context { get; } = new();

    public ObservableCollection<WizardStepViewModel> Steps { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private WizardStepViewModel? _currentStep;

    [ObservableProperty]
    private int _currentStepIndex = -1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _isBusy;

    public bool IsFirstStep => CurrentStepIndex <= 0;
    public bool IsLastStep => CurrentStepIndex >= Steps.Count - 1;

    public string HeaderText =>
        CurrentStep is null
            ? "Solution Replicator"
            : $"Step {CurrentStepIndex + 1} of {Steps.Count}  •  {CurrentStep.Title}";

    /// <summary>Replace the step list (used when the skip path changes the flow) and show the first step.</summary>
    public async Task SetStepsAsync(IEnumerable<WizardStepViewModel> steps)
    {
        Steps.Clear();
        foreach (var s in steps)
        {
            s.PropertyChanged += OnStepPropertyChanged;
            Steps.Add(s);
        }
        CurrentStepIndex = -1;
        await GoToAsync(0);
    }

    private void OnStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WizardStepViewModel.CanAdvance) && ReferenceEquals(sender, CurrentStep))
            NextCommand.NotifyCanExecuteChanged();
    }

    private async Task GoToAsync(int index)
    {
        if (index < 0 || index >= Steps.Count) return;
        CurrentStepIndex = index;
        CurrentStep = Steps[index];
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        try
        {
            await CurrentStep.OnEnterAsync();
        }
        catch (System.Exception ex)
        {
            // Surface unhandled step errors rather than crashing the window.
            System.Windows.MessageBox.Show(
                $"Unexpected error entering step:\n{ex.GetType().Name}: {ex.Message}",
                "Step Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task BackAsync()
    {
        if (CurrentStepIndex > 0)
            await GoToAsync(CurrentStepIndex - 1);
    }

    private bool CanGoBack() => !IsBusy && CurrentStepIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (CurrentStep is null || IsLastStep) return;
        IsBusy = true;
        try
        {
            if (!await CurrentStep.OnLeaveAsync()) return;
            await GoToAsync(CurrentStepIndex + 1);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanGoNext() => !IsBusy && CurrentStep is { CanAdvance: true } && !IsLastStep;

    [RelayCommand]
    private void Cancel() => Application.Current.Shutdown();
}
