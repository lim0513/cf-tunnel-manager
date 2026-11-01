using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using CfTunnelManager.Core;
using CfTunnelManager.ViewModels;

namespace CfTunnelManager;

public partial class RequirementsWindow : Window
{
    private const string DashboardUrl = "https://dash.cloudflare.com/?to=/:account/oauth-clients";

    private readonly MainViewModel _viewModel;

    public RequirementsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        RedirectBox.Text = OAuthClient.RedirectUri;
        ClientIdBox.Text = viewModel.ClientId;
        ClientIdStatus.Text = viewModel.HasClientId ? "已保存 Client ID。" : "还没有 Client ID。按上面的步骤创建后填到这里。";
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(DashboardUrl) { UseShellExecute = true });

    private void CopyRedirect_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(OAuthClient.RedirectUri);

    private void SaveClientId_Click(object sender, RoutedEventArgs e)
    {
        var clientId = ClientIdBox.Text.Trim();
        if (clientId.Length > 0 && !Regex.IsMatch(clientId, "^[A-Za-z0-9-]+$"))
        {
            ClientIdStatus.Text = "Client ID 只能包含字母、数字和连字符。";
            return;
        }

        _viewModel.SetClientId(clientId);
        ClientIdStatus.Text = clientId.Length > 0 ? "已保存，关闭这个窗口后就能登录。" : "已清除。填写 Client ID 后才能登录。";
    }
}
