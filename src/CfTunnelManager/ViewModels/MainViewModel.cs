using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CfTunnelManager.Core;
using CfTunnelManager.Services;

namespace CfTunnelManager.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxConcurrentConfigReads = 4;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly OAuthClient _oauth;
    private readonly AuthSession _session;
    private readonly CloudflareClient _api;
    private readonly AppManager _manager;
    private readonly AccessConnectionManager _connections = new();
    private readonly CollapsedTunnelStore _collapsed = new();
    private readonly ClientIdStore _clientIds = new();
    private readonly DispatcherTimer _timer = new() { Interval = PollInterval };

    private IReadOnlyList<ZoneInfo> _zones = [];
    private string? _accountId;
    private string? _accountName;
    private bool _isLoading;
    private string? _message;

    public MainViewModel()
    {
        _oauth = new OAuthClient(_http, _clientIds.Load() ?? "");
        _session = new AuthSession(_oauth, new DpapiTokenStore());
        _isLoading = _session.IsSignedIn;
        _api = new CloudflareClient(_http, _session);
        _manager = new AppManager(_api);

        SignInCommand = new AsyncCommand(SignInAsync);
        SignOutCommand = new AsyncCommand(SignOutAsync);
        RefreshCommand = new AsyncCommand(ReloadAsync, () => !IsLoading);

        _timer.Tick += async (_, _) => await PollAsync();
        _connections.ConnectionLost += hostname => Application.Current?.Dispatcher.BeginInvoke(() => OnConnectionLost(hostname));
    }

    public ObservableCollection<TunnelViewModel> Tunnels { get; } = [];

    public ICommand SignInCommand { get; }

    public ICommand SignOutCommand { get; }

    public ICommand RefreshCommand { get; }

    public bool IsSignedIn => _session.IsSignedIn;

    public bool IsSignedOut => !IsSignedIn;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            Set(ref _isLoading, value);
            OnChanged(nameof(ShowLoading));
            OnChanged(nameof(ShowNoTunnels));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowLoading => IsLoading && Tunnels.Count == 0;

    public bool ShowNoTunnels => !IsLoading && _accountId is not null && Tunnels.Count == 0;

    public string? Message
    {
        get => _message;
        private set
        {
            Set(ref _message, value);
            OnChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message);

    public string? AccountName => _accountName;

    public string CountsText
    {
        get
        {
            var online = Tunnels.Count(t => t.StatusText == "在线");
            var degraded = Tunnels.Count(t => t.StatusText == "连接异常");
            var offline = Tunnels.Count - online - degraded;
            return $"{Tunnels.Count} 条隧道，{online} 在线，{degraded} 异常，{offline} 离线";
        }
    }

    public string Summary => Tunnels.Count == 0 ? _accountName ?? "" : $"{_accountName}：{CountsText}";

    public IEnumerable<TunnelViewModel> EditableTunnels => Tunnels.Where(t => t.IsRemoteManaged);

    public string ClientId => _oauth.ClientId;

    public bool HasClientId => _oauth.ClientId.Length > 0;

    public bool NeedsClientId => !HasClientId;

    public void SetClientId(string clientId)
    {
        if (clientId.Length == 0)
        {
            _clientIds.Clear();
        }
        else
        {
            _clientIds.Save(clientId);
        }
        _oauth.ClientId = clientId;
        OnChanged(nameof(HasClientId));
        OnChanged(nameof(NeedsClientId));
    }

    public async Task InitializeAsync()
    {
        if (_session.IsSignedIn)
        {
            await ReloadAsync();
        }
    }

    public IReadOnlyList<ZoneInfo> Zones => _zones;

    public async Task OpenAddAppAsync()
    {
        var dialog = new AppSettingsWindow(EditableTunnels.ToList(), _zones) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true)
        {
            await AddAppAsync(dialog.SelectedTunnel!, dialog.Hostname, dialog.RoutePath, dialog.Service, dialog.Settings!, dialog.LocalPort);
        }
    }

    private async Task AddAppAsync(
        TunnelViewModel tunnel,
        string hostname,
        string? path,
        string service,
        OriginSettings settings,
        int? localPort)
    {
        await RunAsync(async () =>
        {
            RequirePortAvailable(hostname, localPort);
            Message = $"正在添加 {hostname}…";
            await _manager.AddAppAsync(RequireAccount(), tunnel.Id, hostname, path, service, settings, _zones, CancellationToken.None);
            if (localPort is not null)
            {
                _connections.SetSavedPort(hostname, localPort);
            }
            _collapsed.Set(tunnel.Id, false);
            await LoadAsync();
            Message = $"已添加 {hostname}";
        });
    }

    internal async Task RunAppActionAsync(AppViewModel app)
    {
        switch (app.Kind)
        {
            case ServiceKind.Http:
                OpenInBrowser($"https://{app.App.Hostname}{app.App.Path}");
                break;
            case ServiceKind.Tcp when app.IsConnected:
                _connections.Disconnect(app.App.Hostname);
                SetConnectedPort(app.App.Hostname, null);
                break;
            case ServiceKind.Tcp:
                app.IsConnecting = true;
                await RunAsync(async () =>
                {
                    var port = await _connections.ConnectAsync(app.App.Hostname, CancellationToken.None);
                    SetConnectedPort(app.App.Hostname, port);
                });
                app.IsConnecting = false;
                break;
        }
    }

    internal async Task RepairDnsAsync(AppViewModel app)
    {
        await RunAsync(async () =>
        {
            await _manager.RepairDnsAsync(app.Tunnel.Id, app.App.Hostname, _zones, CancellationToken.None);
            await LoadAsync();
        });
    }

    private async Task ShowReplicasAsync(string tunnelId, string tunnelName)
    {
        await RunAsync(async () =>
        {
            var connectors = await _api.GetConnectorsAsync(RequireAccount(), tunnelId, CancellationToken.None);
            new ReplicasWindow(tunnelName, connectors) { Owner = Application.Current.MainWindow }.ShowDialog();
        });
    }

    internal async Task EditSettingsAsync(AppViewModel app)
    {
        var oldHost = app.App.Hostname;
        var savedPort = _connections.GetSavedPort(oldHost);
        var dialog = new AppSettingsWindow(app, _zones, savedPort) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var renamed = !IngressEditor.SameHost(oldHost, dialog.Hostname);
            RequirePortAvailable(dialog.Hostname, dialog.LocalPort);

            var routeChanged = renamed
                || dialog.RoutePath != app.App.Path
                || dialog.Service != app.App.Service
                || !JsonNode.DeepEquals(dialog.Settings!.ApplyTo(app.App.OriginRequest), app.App.OriginRequest ?? new JsonObject());
            if (routeChanged)
            {
                Message = $"正在保存 {dialog.Hostname}…";
                await _manager.UpdateAppAsync(
                    RequireAccount(), app.Tunnel.Id, app.App, dialog.Hostname, dialog.RoutePath, dialog.Service,
                    dialog.Settings!, _zones, CancellationToken.None);
            }

            var hostStaysRouted = !renamed
                || Tunnels.SelectMany(t => t.Apps).Any(a => a != app && IngressEditor.SameHost(a.App.Hostname, oldHost));
            if (!hostStaysRouted || (app.ConnectedPort is { } connected && connected != dialog.LocalPort && dialog.LocalPort is not null))
            {
                _connections.Disconnect(oldHost);
                SetConnectedPort(oldHost, null);
            }

            if (renamed)
            {
                _connections.SetSavedPort(oldHost, null);
            }
            if (renamed || dialog.LocalPort != savedPort)
            {
                _connections.SetSavedPort(dialog.Hostname, dialog.LocalPort);
            }

            if (routeChanged)
            {
                await LoadAsync();
                Message = $"已保存 {dialog.Hostname}";
            }
        });
    }

    internal async Task DeleteAppAsync(AppViewModel app)
    {
        var dialog = new DeleteAppWindow(app) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Message = $"正在删除 {app.Hostname}…";
            await _manager.RemoveAppAsync(
                RequireAccount(), app.Tunnel.Id, app.App, dialog.DeleteDns, _zones, CancellationToken.None);
            var stillRouted = Tunnels.SelectMany(t => t.Apps).Any(a => a != app && IngressEditor.SameHost(a.App.Hostname, app.App.Hostname));
            if (!stillRouted)
            {
                _connections.Disconnect(app.App.Hostname);
            }
            await LoadAsync();
            Message = $"已删除 {app.Hostname}";
        });
    }

    public void Dispose()
    {
        _timer.Stop();
        _connections.Dispose();
        _http.Dispose();
    }

    private async Task SignInAsync()
    {
        await RunAsync(async () =>
        {
            using var timeout = new CancellationTokenSource(SignInTimeout);
            var verifier = OAuthClient.CreateVerifier();
            var state = Guid.NewGuid().ToString("N");

            LoopbackCallback callback;
            try
            {
                callback = new LoopbackCallback(OAuthClient.RedirectPort);
            }
            catch (SocketException)
            {
                throw new InvalidOperationException($"端口 {OAuthClient.RedirectPort} 被占用，无法接收登录回调。");
            }

            using (callback)
            {
                OpenInBrowser(_oauth.CreateAuthorizationUrl(OAuthClient.CreateChallenge(verifier), state));
                Message = "请在浏览器中完成授权。";
                var code = await callback.WaitForCodeAsync(state, timeout.Token);
                await _session.SignInAsync(code, verifier, timeout.Token);
            }

            Message = null;
            OnSignInChanged();
            await LoadAsync();
        });
    }

    private async Task SignOutAsync()
    {
        await RunAsync(async () =>
        {
            _timer.Stop();
            foreach (var app in Tunnels.SelectMany(t => t.Apps).Where(a => a.IsConnected))
            {
                _connections.Disconnect(app.App.Hostname);
            }
            Tunnels.Clear();
            _accountId = null;
            _accountName = null;
            OnSummaryChanged();
            await _session.SignOutAsync(CancellationToken.None);
            OnSignInChanged();
        });
        OnSignInChanged();
    }

    private Task ReloadAsync() => RunAsync(LoadAsync);

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var accounts = await _api.GetAccountsAsync(CancellationToken.None);
            var account = accounts.FirstOrDefault()
                ?? throw new InvalidOperationException("授权里没有可访问的 Cloudflare 账号。");
            _accountId = account.Id;
            _accountName = account.Name;

            var allZones = await _api.GetZonesAsync(CancellationToken.None);
            _zones = allZones.Where(z => z.AccountId == account.Id).ToList();

            var tunnels = await _api.GetTunnelsAsync(account.Id, CancellationToken.None);
            var configs = await ReadConfigsAsync(account.Id, tunnels);
            var records = await ReadTunnelRecordsAsync();

            Tunnels.Clear();
            foreach (var tunnel in tunnels.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var (config, error) = configs[tunnel.Id];
                var view = new TunnelViewModel(
                    tunnel,
                    error is null && config is not null,
                    error,
                    !_collapsed.IsCollapsed(tunnel.Id),
                    expanded => _collapsed.Set(tunnel.Id, !expanded))
                {
                    ShowReplicasCommand = new AsyncCommand(() => ShowReplicasAsync(tunnel.Id, tunnel.Name)),
                };
                if (config is not null)
                {
                    foreach (var app in IngressEditor.ListApps(config))
                    {
                        view.Apps.Add(new AppViewModel(
                            this, view, app, EvaluateDns(app, tunnel.Id, records), _connections.GetPort(app.Hostname)));
                    }
                }
                if (view.Apps.Count > 0)
                {
                    view.Apps[^1].IsLast = true;
                }
                Tunnels.Add(view);
            }

            OnSummaryChanged();
            _timer.Start();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PollAsync()
    {
        if (_accountId is null || IsLoading)
        {
            return;
        }

        try
        {
            var latest = await _api.GetTunnelsAsync(_accountId, CancellationToken.None);
            if (latest.Count != Tunnels.Count || latest.Any(t => Tunnels.All(v => v.Id != t.Id)))
            {
                await LoadAsync();
                return;
            }
            foreach (var tunnel in latest)
            {
                Tunnels.First(v => v.Id == tunnel.Id).Update(tunnel);
            }
            CommandManager.InvalidateRequerySuggested();
            OnSummaryChanged();
            Message = null;
        }
        catch (Exception e)
        {
            HandleError(e);
        }
    }

    private async Task<Dictionary<string, (JsonObject? Config, string? Error)>> ReadConfigsAsync(
        string accountId,
        IReadOnlyList<TunnelInfo> tunnels)
    {
        using var gate = new SemaphoreSlim(MaxConcurrentConfigReads);
        var reads = tunnels.Select(async tunnel =>
        {
            await gate.WaitAsync();
            try
            {
                var config = await _api.GetTunnelConfigAsync(accountId, tunnel.Id, CancellationToken.None);
                return (tunnel.Id, Config: config, Error: (string?)null);
            }
            catch (Exception e) when (e is CloudflareApiException or HttpRequestException or TaskCanceledException)
            {
                return (tunnel.Id, Config: (JsonObject?)null, Error: $"读取应用失败：{e.Message}");
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(reads);
        return results.ToDictionary(r => r.Id, r => (r.Config, r.Error));
    }

    private async Task<List<DnsRecordInfo>> ReadTunnelRecordsAsync()
    {
        var records = new List<DnsRecordInfo>();
        foreach (var zone in _zones)
        {
            records.AddRange(await _api.GetDnsRecordsAsync(zone.Id, null, CancellationToken.None));
        }
        return records;
    }

    private DnsState EvaluateDns(PublishedApp app, string tunnelId, IEnumerable<DnsRecordInfo> records) =>
        ZoneMatcher.Find(_zones, app.Hostname) is null
            ? DnsState.ZoneNotFound
            : DnsEvaluator.Evaluate(app.Hostname, tunnelId, records);

    private void RequirePortAvailable(string hostname, int? port)
    {
        if (port is { } value && _connections.IsPortUsedByAnother(hostname, value))
        {
            throw new InvalidOperationException($"本地端口 {value} 已分配给其他应用。");
        }
    }

    private void SetConnectedPort(string hostname, int? port)
    {
        foreach (var app in Tunnels.SelectMany(t => t.Apps).Where(a => IngressEditor.SameHost(a.App.Hostname, hostname)))
        {
            app.ConnectedPort = port;
        }
    }

    private void OnConnectionLost(string hostname)
    {
        _connections.Forget(hostname);
        SetConnectedPort(hostname, null);
        Message = $"{hostname} 的本地连接已断开。";
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            HandleError(e);
        }
    }

    private void HandleError(Exception error)
    {
        if (error is ReauthenticationRequiredException)
        {
            _timer.Stop();
            OnSignInChanged();
        }
        Message = error switch
        {
            OperationCanceledException => "操作超时，请重试。",
            HttpRequestException => $"网络错误：{error.Message}",
            _ => error.Message,
        };
    }

    private void OnSummaryChanged()
    {
        OnChanged(nameof(AccountName));
        OnChanged(nameof(CountsText));
        OnChanged(nameof(Summary));
    }

    private void OnSignInChanged()
    {
        OnChanged(nameof(IsSignedIn));
        OnChanged(nameof(IsSignedOut));
    }

    private string RequireAccount() =>
        _accountId ?? throw new InvalidOperationException("账号信息尚未加载。");

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
