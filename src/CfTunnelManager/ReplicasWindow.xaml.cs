using System.Windows;
using CfTunnelManager.Core;

namespace CfTunnelManager;

public partial class ReplicasWindow : Window
{
    public ReplicasWindow(string tunnelName, IReadOnlyList<ConnectorInfo> connectors)
    {
        InitializeComponent();
        TitleText.Text = $"{tunnelName} 的 {connectors.Count} 个副本";
        SameAddressText.Visibility = ConnectorInfo.ShareOneAddress(connectors) ? Visibility.Visible : Visibility.Collapsed;

        var now = DateTimeOffset.Now;
        RowsList.ItemsSource = connectors
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(c => new
            {
                Name = c.DisplayName,
                c.Id,
                c.OriginIp,
                Locations = string.Join(", ", c.Locations),
                c.Version,
                c.Architecture,
                Uptime = c.UptimeText(now),
            })
            .ToList();
    }
}
