using System.Windows;
using SolutionReplicator.ViewModels;

namespace SolutionReplicator.Views;

public partial class WizardShellWindow : Window
{
    private readonly WizardHostViewModel _host = new();

    public WizardShellWindow()
    {
        InitializeComponent();
        DataContext = _host;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Single flow: load template -> pick scope -> point at repo + catalog ->
        // choose target system & values -> compose the re-importable package.
        await _host.SetStepsAsync(new WizardStepViewModel[]
        {
            new LoadSolutionViewModel(_host.Context),
            new SelectScopeViewModel(_host.Context),
            new RepoCatalogViewModel(_host.Context),
            new ProfileValuesViewModel(_host.Context),
            new ComposeViewModel(_host.Context),
        });
    }
}
