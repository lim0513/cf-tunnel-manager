using System.Windows.Input;
using CfTunnelManager.Core;

namespace CfTunnelManager.ViewModels;

public sealed class AppViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private DnsState _dns;
    private int? _connectedPort;

    internal AppViewModel(MainViewModel owner, TunnelViewModel tunnel, PublishedApp app, DnsState dns, int? connectedPort)
    {
        _owner = owner;
        Tunnel = tunnel;
        App = app;
        _dns = dns;
        _connectedPort = connectedPort;
        Kind = ServiceClassifier.Classify(app.Service);
        ActionCommand = new AsyncCommand(() => _owner.RunAppActionAsync(this), () => ActionEnabled);
        RepairDnsCommand = new AsyncCommand(() => _owner.RepairDnsAsync(this));
        DeleteCommand = new AsyncCommand(() => _owner.DeleteAppAsync(this));
    }

    public TunnelViewModel Tunnel { get; }

    public PublishedApp App { get; }

    public ServiceKind Kind { get; }

    public string Hostname => App.Path is null ? App.Hostname : App.Hostname + App.Path;

    public string Service => App.Service;

    public ICommand ActionCommand { get; }

    public ICommand RepairDnsCommand { get; }

    public ICommand DeleteCommand { get; }

    public bool IsConnected => _connectedPort is not null;

    public int? ConnectedPort
    {
        get => _connectedPort;
        set
        {
            Set(ref _connectedPort, value);
            OnChanged(string.Empty);
        }
    }

    public DnsState Dns
    {
        get => _dns;
        set
        {
            Set(ref _dns, value);
            OnChanged(string.Empty);
        }
    }

    public string KindText => Kind switch
    {
        ServiceKind.Http => "HTTP",
        ServiceKind.Tcp => "TCP",
        _ => "其他",
    };

    public string DnsText => _dns switch
    {
        DnsState.Ok => "DNS 正常",
        DnsState.Missing => "DNS 缺失",
        DnsState.OtherTunnel => "DNS 指向其他目标",
        _ => "域名不在账号下",
    };

    public bool ShowDns => _dns != DnsState.Ok;

    public bool IsLast { get; set; }

    public bool CanRepairDns => _dns == DnsState.Missing;

    public string ActionText => Kind switch
    {
        ServiceKind.Http => "打开",
        ServiceKind.Tcp when IsConnected => "断开",
        ServiceKind.Tcp => "连接",
        _ => "",
    };

    public bool ShowAction => Kind != ServiceKind.Other;

    public bool ActionEnabled => Kind == ServiceKind.Http || IsConnected || (Kind == ServiceKind.Tcp && Tunnel.IsOnline);

    public string ConnectionText
    {
        get
        {
            if (IsConnected)
            {
                return Tunnel.IsOnline
                    ? $"已映射到 127.0.0.1:{_connectedPort}"
                    : $"127.0.0.1:{_connectedPort}，隧道已离线";
            }
            return Kind == ServiceKind.Tcp && !Tunnel.IsOnline ? "隧道离线" : "";
        }
    }

    public void Refresh() => OnChanged(string.Empty);
}
