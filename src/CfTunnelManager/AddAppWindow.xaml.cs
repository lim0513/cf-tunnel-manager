using System.Windows;
using CfTunnelManager.Core;
using CfTunnelManager.ViewModels;

namespace CfTunnelManager;

public partial class AddAppWindow : Window
{
    public AddAppWindow(IReadOnlyList<TunnelViewModel> tunnels)
    {
        InitializeComponent();
        TunnelBox.ItemsSource = tunnels;
        TunnelBox.SelectedIndex = tunnels.Count > 0 ? 0 : -1;
        Loaded += (_, _) => HostnameBox.Focus();
    }

    public TunnelViewModel? SelectedTunnel => TunnelBox.SelectedItem as TunnelViewModel;

    public string Hostname => HostnameBox.Text.Trim().ToLowerInvariant();

    public string Service => ServiceBox.Text.Trim();

    private void Preview_Changed(object sender, RoutedEventArgs e)
    {
        PreviewPanel.DataContext = SelectedTunnel;
        PreviewHostname.Text = Hostname.Length > 0 ? Hostname : "app.example.com";
        PreviewService.Text = Service.Length > 0 ? Service : "http://localhost:8080";
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var error = SelectedTunnel is null ? "没有可添加应用的隧道。"
            : !AppInputValidator.IsValidHostname(Hostname) ? "访问地址格式不对，应该像 app.example.com。"
            : !AppInputValidator.IsValidService(Service) ? "后端服务要以 http、https、tcp、ssh、rdp 或 smb 开头，并包含地址。"
            : null;

        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
