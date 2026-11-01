using System.Collections.ObjectModel;
using CfTunnelManager.Core;

namespace CfTunnelManager.ViewModels;

public sealed class TunnelViewModel : ObservableObject
{
    private readonly Action<bool> _onExpandedChanged;
    private TunnelInfo _info;
    private bool _isExpanded;

    public TunnelViewModel(TunnelInfo info, bool isRemoteManaged, string? loadError, bool isExpanded, Action<bool> onExpandedChanged)
    {
        _info = info;
        IsRemoteManaged = isRemoteManaged;
        LoadError = loadError;
        _isExpanded = isExpanded;
        _onExpandedChanged = onExpandedChanged;
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnChanged(nameof(IsExpanded));
                _onExpandedChanged(value);
            }
        }
    }

    public string AppCountText => $"{Apps.Count} 个应用";

    public string Id => _info.Id;

    public string Name => _info.Name;

    public bool IsRemoteManaged { get; }

    public string? LoadError { get; }

    public string? Notice => LoadError ?? (IsRemoteManaged ? null : "本地配置模式，无法读取已发布应用。");

    public bool IsOnline => _info.Status is "healthy" or "degraded";

    public ObservableCollection<AppViewModel> Apps { get; } = [];

    public string StatusText => _info.Status switch
    {
        "healthy" => "在线",
        "degraded" => "连接异常",
        "down" => "离线",
        "inactive" => "从未连接",
        _ => "状态未知",
    };

    public string StatusColor => _info.Status switch
    {
        "healthy" => "#12A085",
        "degraded" => "#E09A1A",
        "down" => "#B03A7A",
        _ => "#8A96A3",
    };

    public string VersionText => _info.ConnectorVersion is { } version ? $"cloudflared {version}" : "";

    public bool HasNoApps => Apps.Count == 0 && Notice is null;

    public bool HasNotice => Notice is not null;

    public bool HasApps => Apps.Count > 0;

    public string SeenText => IsOnline
        ? Format("上线于", _info.ActiveAt)
        : Format("最后在线", _info.InactiveAt);

    public void Update(TunnelInfo info)
    {
        _info = info;
        OnChanged(string.Empty);
        foreach (var app in Apps)
        {
            app.Refresh();
        }
    }

    private static string Format(string label, DateTimeOffset? value) =>
        value is { } time ? $"{label} {time.LocalDateTime:MM-dd HH:mm}" : "";
}
