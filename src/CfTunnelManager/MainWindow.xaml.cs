using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Animation;
using CfTunnelManager.Services;
using CfTunnelManager.ViewModels;

namespace CfTunnelManager;

public partial class MainWindow : Window
{
    private const string LightIcon = "M6.76 4.84l-1.8-1.79-1.41 1.41 1.79 1.79 1.42-1.41zM4 10.5H1v2h3v-2zm9-9.95h-2V3.5h2V.55zm7.45 3.91l-1.41-1.41-1.79 1.79 1.41 1.41 1.79-1.79zm-3.21 13.7l1.79 1.8 1.41-1.41-1.8-1.79-1.4 1.4zM20 10.5v2h3v-2h-3zm-8-5c-3.31 0-6 2.69-6 6s2.69 6 6 6 6-2.69 6-6-2.69-6-6-6zm-1 16.95h2V19.5h-2v2.95zm-7.45-3.91l1.41 1.41 1.79-1.8-1.41-1.41-1.79 1.8z";
    private const string DarkIcon = "M12 3c-4.97 0-9 4.03-9 9s4.03 9 9 9 9-4.03 9-9c0-.46-.04-.92-.1-1.36-.98 1.37-2.58 2.26-4.4 2.26-2.98 0-5.4-2.42-5.4-5.4 0-1.81.89-3.42 2.26-4.4-.44-.06-.9-.1-1.36-.1z";
    private const string AutoIcon = "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20zM12 4a8 8 0 0 0 0 16z";

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
        ShowThemeChoice();
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

    private void ShowThemeChoice()
    {
        var (icon, label) = ((App)Application.Current).Theme.Choice switch
        {
            ThemeChoice.Light => (LightIcon, "配色：浅色"),
            ThemeChoice.Dark => (DarkIcon, "配色：深色"),
            _ => (AutoIcon, "配色：自动"),
        };
        ThemeButton.Content = icon;
        ThemeButton.ToolTip = $"{label}，点击切换";
        AutomationProperties.SetName(ThemeButton, label);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        var theme = ((App)Application.Current).Theme;
        theme.Select(theme.Choice switch
        {
            ThemeChoice.Auto => ThemeChoice.Light,
            ThemeChoice.Light => ThemeChoice.Dark,
            _ => ThemeChoice.Auto,
        });
        ShowThemeChoice();
    }

    private void Requirements_Click(object sender, RoutedEventArgs e)
    {
        new RequirementsWindow(_viewModel) { Owner = this }.ShowDialog();
    }

    private async void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppSettingsWindow(_viewModel.EditableTunnels.ToList(), _viewModel.Zones) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.AddAppAsync(dialog.SelectedTunnel!, dialog.Hostname, dialog.RoutePath, dialog.Service, dialog.Settings!);
        }
    }
}
