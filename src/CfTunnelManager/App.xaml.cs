using System.Windows;
using CfTunnelManager.Services;

namespace CfTunnelManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;

    internal ThemeManager Theme { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            _instance.ActivateFirstInstance();
            Shutdown();
            return;
        }

        Theme = new ThemeManager(this);
        var window = new MainWindow();
        _instance.Listen(() => Dispatcher.BeginInvoke(window.BringToFront));
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Theme?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
