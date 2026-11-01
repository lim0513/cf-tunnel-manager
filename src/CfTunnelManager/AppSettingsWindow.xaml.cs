using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CfTunnelManager.Core;
using CfTunnelManager.ViewModels;

namespace CfTunnelManager;

public partial class AppSettingsWindow : Window
{
    private const string Socks = "socks";
    private const string DefaultState = "使用默认设置";

    private static readonly Brush CustomFill = new SolidColorBrush(Color.FromRgb(0xE3, 0xEE, 0xFA));
    private static readonly Brush CustomText = new SolidColorBrush(Color.FromRgb(0x1F, 0x4A, 0x75));
    private static readonly Brush DefaultBorder = new SolidColorBrush(Color.FromRgb(0xC9, 0xD3, 0xDE));

    private static readonly char[] TagSeparators = [',', '，', ' ', ';', '\n'];

    private readonly string? _originalService;
    private bool _loading;

    /// <summary>Adds a new application to one of the given tunnels.</summary>
    public AppSettingsWindow(IReadOnlyList<TunnelViewModel> tunnels, IReadOnlyList<ZoneInfo> zones)
    {
        InitializeComponent();
        Title = "添加应用";
        IntroText.Text = "把服务发布到隧道。";
        SaveButton.Content = "添加应用";
        TunnelPanel.Visibility = Visibility.Visible;

        _loading = true;
        TunnelBox.ItemsSource = tunnels;
        TunnelBox.SelectedItem = tunnels.FirstOrDefault();
        ZoneBox.ItemsSource = zones.Select(z => z.Name).ToList();
        ZoneBox.SelectedIndex = zones.Count > 0 ? 0 : -1;
        _loading = false;

        Load(new OriginSettings());
        Loaded += (_, _) => SubdomainBox.Focus();
    }

    /// <summary>Edits an existing application.</summary>
    public AppSettingsWindow(AppViewModel app, IReadOnlyList<ZoneInfo> zones, int? localPort)
    {
        InitializeComponent();
        LocalPortBox.Text = localPort?.ToString() ?? "";
        IntroText.Text = "更新此已发布应用程序的配置。";
        SaveButton.Content = "保存更改";
        _originalService = app.App.Service;

        var hostname = app.App.Hostname;
        var zone = ZoneMatcher.Find(zones, hostname)?.Name;
        var zoneNames = zones.Select(z => z.Name).ToList();
        if (zone is null)
        {
            zone = string.Join('.', hostname.Split('.').TakeLast(2));
            zoneNames.Add(zone);
        }

        _loading = true;
        ZoneBox.ItemsSource = zoneNames;
        ZoneBox.SelectedItem = zone;
        SubdomainBox.Text = hostname.Length > zone.Length ? hostname[..^(zone.Length + 1)] : "";
        PathBox.Text = app.App.Path ?? "";
        ServiceBox.Text = app.App.Service;
        _loading = false;

        var settings = OriginSettings.Read(app.App.OriginRequest);
        Load(settings);
        AdvancedToggle.IsChecked = app.HasCustomSettings;
    }

    public OriginSettings? Settings { get; private set; }

    public TunnelViewModel? SelectedTunnel => TunnelBox.SelectedItem as TunnelViewModel;

    public string Hostname
    {
        get
        {
            var subdomain = SubdomainBox.Text.Trim().Trim('.').ToLowerInvariant();
            var zone = ZoneBox.SelectedItem as string ?? "";
            return subdomain.Length > 0 ? $"{subdomain}.{zone}" : zone;
        }
    }

    public string? RoutePath => PathBox.Text.Trim() is { Length: > 0 } path ? path : null;

    public string Service => ServiceBox.Text.Trim();

    public int? LocalPort => IsTcp && int.TryParse(LocalPortBox.Text.Trim(), out var port) ? port : null;

    private bool IsTcp => ServiceClassifier.Classify(Service) == ServiceKind.Tcp;

    private void Load(OriginSettings settings)
    {
        _loading = true;
        HttpHostHeaderBox.Text = settings.HttpHostHeader;
        DisableChunkedBox.IsChecked = settings.DisableChunkedEncoding;
        Http2OriginBox.IsChecked = settings.Http2Origin;
        OriginServerNameBox.Text = settings.OriginServerName;
        CaPoolBox.Text = settings.CaPool;
        NoTlsVerifyBox.IsChecked = settings.NoTlsVerify;
        MatchSniBox.IsChecked = settings.MatchSniToHost;
        ConnectTimeoutBox.Text = settings.ConnectTimeout?.ToString() ?? "";
        TlsTimeoutBox.Text = settings.TlsTimeout?.ToString() ?? "";
        TcpKeepAliveBox.Text = settings.TcpKeepAlive?.ToString() ?? "";
        KeepAliveTimeoutBox.Text = settings.KeepAliveTimeout?.ToString() ?? "";
        KeepAliveConnectionsBox.Text = settings.KeepAliveConnections?.ToString() ?? "";
        NoHappyEyeballsBox.IsChecked = settings.NoHappyEyeballs;
        ProxyTypeBox.SelectedIndex = settings.ProxyType == Socks ? 1 : 0;
        AccessEnabledBox.IsChecked = settings.AccessEnabled;
        AccessRequiredBox.IsChecked = settings.AccessRequired;
        AccessTeamNameBox.Text = settings.AccessTeamName;
        AccessAudTagsBox.Text = string.Join(", ", settings.AccessAudTags);
        _loading = false;
        RefreshStates();
    }

    private OriginSettings Collect() => new()
    {
        HttpHostHeader = HttpHostHeaderBox.Text.Trim(),
        DisableChunkedEncoding = DisableChunkedBox.IsChecked == true,
        Http2Origin = Http2OriginBox.IsChecked == true,
        OriginServerName = OriginServerNameBox.Text.Trim(),
        CaPool = CaPoolBox.Text.Trim(),
        NoTlsVerify = NoTlsVerifyBox.IsChecked == true,
        MatchSniToHost = MatchSniBox.IsChecked == true,
        ConnectTimeout = ParseNumber(ConnectTimeoutBox),
        TlsTimeout = ParseNumber(TlsTimeoutBox),
        TcpKeepAlive = ParseNumber(TcpKeepAliveBox),
        KeepAliveTimeout = ParseNumber(KeepAliveTimeoutBox),
        KeepAliveConnections = ParseNumber(KeepAliveConnectionsBox),
        NoHappyEyeballs = NoHappyEyeballsBox.IsChecked == true,
        ProxyType = ProxyTypeBox.SelectedIndex == 1 ? Socks : "",
        AccessEnabled = AccessEnabledBox.IsChecked == true,
        AccessRequired = AccessRequiredBox.IsChecked == true,
        AccessTeamName = AccessTeamNameBox.Text.Trim(),
        AccessAudTags = AccessAudTagsBox.Text.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries),
    };

    private string? Validate(OriginSettings settings)
    {
        if (TunnelPanel.Visibility == Visibility.Visible && SelectedTunnel is null)
        {
            return "没有可添加应用的隧道。";
        }
        if (ZoneBox.SelectedItem is null)
        {
            return "账号下没有可用的域名。";
        }
        if (!AppInputValidator.IsValidHostname(Hostname))
        {
            return "主机名格式不对，请检查子域名。";
        }
        if (Service != _originalService && !AppInputValidator.IsValidService(Service))
        {
            return "Service URL 要以 http、https、tcp、ssh、rdp 或 smb 开头，并包含地址。";
        }
        if (RoutePath is { } path && !IsValidPattern(path))
        {
            return "路径不是有效的正则表达式。";
        }
        if (IsTcp && LocalPortBox.Text.Trim().Length > 0 && LocalPort is not (>= 1 and <= 65535))
        {
            return "本地端口要填写 1 到 65535 之间的整数，留空表示自动分配。";
        }

        TextBox[] numbers = [ConnectTimeoutBox, TlsTimeoutBox, TcpKeepAliveBox, KeepAliveTimeoutBox, KeepAliveConnectionsBox];
        if (numbers.Any(b => b.Text.Trim().Length > 0 && ParseNumber(b) is null))
        {
            return "超时和连接数要填写不小于 0 的整数，留空表示使用默认值。";
        }
        if (settings.AccessEnabled && (settings.AccessTeamName.Length == 0 || settings.AccessAudTags.Count == 0))
        {
            return "验证 Access JWT 时要填写团队名称和 AUD 标签。";
        }
        return null;
    }

    private void RefreshStates()
    {
        FullHostnameText.Text = Hostname;
        LocalPortPanel.Visibility = IsTcp ? Visibility.Visible : Visibility.Collapsed;
        var settings = Collect();
        ShowState(HttpPill, HttpState, settings.CustomHttpCount);
        ShowState(TlsPill, TlsState, settings.CustomTlsCount);
        ShowState(ConnectionPill, ConnectionState, settings.CustomConnectionCount);
        ShowState(AccessPill, AccessState, settings.CustomAccessCount);
    }

    private static void ShowState(Border pill, TextBlock text, int customCount)
    {
        var isCustom = customCount > 0;
        text.Text = isCustom ? $"{customCount} 项已自定义" : DefaultState;
        text.Foreground = isCustom ? CustomText : Brushes.Black;
        pill.Background = isCustom ? CustomFill : Brushes.White;
        pill.BorderBrush = isCustom ? CustomFill : DefaultBorder;
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsInitialized)
        {
            RefreshStates();
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => Load(new OriginSettings());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = Collect();
        if (Validate(settings) is { } error)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Settings = settings;
        DialogResult = true;
    }

    private static bool IsValidPattern(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int? ParseNumber(TextBox box) =>
        int.TryParse(box.Text.Trim(), out var number) && number >= 0 ? number : null;
}
