using System.Windows;
using CfTunnelManager.Services;

namespace CfTunnelManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;

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

        var window = new MainWindow();
        _instance.Listen(() => Dispatcher.BeginInvoke(window.BringToFront));
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
