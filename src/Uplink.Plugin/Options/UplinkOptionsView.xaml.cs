using System.Windows;
using System.Windows.Controls;

namespace MyUniverseUplink.Options;

/// <summary>Code-behind minimo: le PasswordBox non sono bindabili, quindi inoltrano il testo al ViewModel come parametro.</summary>
public partial class UplinkOptionsView : UserControl
{
    private OptionsViewModel? _viewModel;

    public UplinkOptionsView()
    {
        InitializeComponent();
        Loaded += (_, _) => EnsureViewModel();
        Unloaded += (_, _) => ReleaseViewModel();
    }

    private void EnsureViewModel()
    {
        if (_viewModel is not null)
        {
            return;
        }

        if (MyUniverseUplinkPlugin.Instance is not { } plugin)
        {
            NotReadyText.Visibility = Visibility.Visible;
            return;
        }

        NotReadyText.Visibility = Visibility.Collapsed;
        _viewModel = new OptionsViewModel(plugin);
        DataContext = _viewModel;
        _viewModel.Activity.ScrollRequested += OnActivityScrollRequested;
    }

    private void ReleaseViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.Activity.ScrollRequested -= OnActivityScrollRequested;
        }

        _viewModel?.Dispose();
        _viewModel = null;
        DataContext = null;
    }

    /// <summary>Follow: scorre all'ultima voce del feed quando ne arrivano di nuove.</summary>
    private void OnActivityScrollRequested(object? sender, EventArgs e)
    {
        var items = ActivityList.Items;
        if (items.Count > 0)
        {
            ActivityList.ScrollIntoView(items[items.Count - 1]);
        }
    }

    private void OnSaveToken(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.SaveToken(TokenBox.Password);
        TokenBox.Clear();
    }

    private void OnSaveHeaderValue(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.SaveHeaderValue(HeaderValueBox.Password);
        HeaderValueBox.Clear();
    }

    private void OnTestConnection(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _ = _viewModel.TestConnectionAsync(TokenBox.Password);
    }
}
