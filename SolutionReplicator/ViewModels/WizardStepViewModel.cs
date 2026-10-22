using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SolutionReplicator.Models;

namespace SolutionReplicator.ViewModels;

/// <summary>
/// Base class for a single wizard step. The host renders <c>CurrentStep</c> via an
/// implicit DataTemplate (step VM type -> UserControl) declared in App.xaml.
/// </summary>
public abstract partial class WizardStepViewModel : ObservableObject
{
    protected WizardStepViewModel(WizardContext context)
    {
        Context = context;
    }

    public WizardContext Context { get; }

    /// <summary>Short title shown in the header/breadcrumb.</summary>
    public abstract string Title { get; }

    /// <summary>When false, the host disables Next. Raise notifications as validity changes.</summary>
    [ObservableProperty]
    private bool _canAdvance = true;

    /// <summary>Called when the step becomes current (e.g. to scan/populate).</summary>
    public virtual Task OnEnterAsync() => Task.CompletedTask;

    /// <summary>Called before leaving the step forward (e.g. to compute results). Return false to block.</summary>
    public virtual Task<bool> OnLeaveAsync() => Task.FromResult(true);
}
