using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using CfTunnelManager.Services;
using CfTunnelManager.ViewModels;

namespace CfTunnelManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly TrayIcon _tray;
    private WindowState _stateBeforeMinimize = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _tray = new TrayIcon(BringToFront, Close);
        _viewModel.PropertyChanged += OnViewModelChanged;
        LoadingPanel.IsVisibleChanged += OnLoadingVisibilityChanged;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) =>
        {
            _tray.Dispose();
            _viewModel.Dispose();
        };
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
        else
        {
            _stateBeforeMinimize = WindowState;
        }
    }

    private void OnLoadingVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var animation = (Storyboard)FindResource("TunnelDotAnimation");
        if (LoadingPanel.IsVisible && SystemParameters.ClientAreaAnimation)
        {
            animation.Begin(this, isControllable: true);
        }
        else
        {
            animation.Stop(this);
        }
    }

    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _stateBeforeMinimize;
        }

        // Lifts the window above others even when Windows refuses to give it the foreground.
        Topmost = true;
        Topmost = false;

        var dialog = OwnedWindows.Cast<Window>().LastOrDefault();
        (dialog ?? this).Activate();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Summary) && _viewModel.Summary.Length > 0)
        {
            _tray.SetText(_viewModel.Summary);
        }
        else if (e.PropertyName == nameof(MainViewModel.Message) && !IsVisible && _viewModel.Message is { Length: > 0 } message)
        {
            _tray.Notify(message);
        }
    }

    private void Requirements_Click(object sender, RoutedEventArgs e)
    {
        new RequirementsWindow(_viewModel) { Owner = this }.ShowDialog();
    }

    private async void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppWindow(_viewModel.EditableTunnels.ToList()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.AddAppAsync(dialog.SelectedTunnel!, dialog.Hostname, dialog.Service);
        }
    }
}
