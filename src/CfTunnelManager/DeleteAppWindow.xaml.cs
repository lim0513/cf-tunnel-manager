using System.Windows;
using CfTunnelManager.ViewModels;

namespace CfTunnelManager;

public partial class DeleteAppWindow : Window
{
    public DeleteAppWindow(AppViewModel app)
    {
        InitializeComponent();
        TitleText.Text = $"删除 {app.Hostname}？";
        DetailText.Text = $"删除后，这个地址不再通过隧道 {app.Tunnel.Name} 访问 {app.Service}。";
        ConnectionText.Visibility = app.IsConnected ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool DeleteDns => DeleteDnsBox.IsChecked == true;

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
