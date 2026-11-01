namespace CfTunnelManager.Core;

public sealed record AccountInfo(string Id, string Name);

public sealed record ZoneInfo(string Id, string Name, string AccountId);

public sealed record TunnelInfo(
    string Id,
    string Name,
    string Status,
    DateTimeOffset? ActiveAt,
    DateTimeOffset? InactiveAt,
    string? ConnectorVersion = null,
    int ConnectorCount = 0);

public sealed record ConnectorInfo(
    string Id,
    string? Hostname,
    string? OriginIp,
    IReadOnlyList<string> Locations,
    string? Version,
    string? Architecture,
    DateTimeOffset? StartedAt)
{
    public string DisplayName => Hostname ?? (Id.Length > 8 ? Id[..8] : Id);

    public string UptimeText(DateTimeOffset now)
    {
        if (StartedAt is not { } started)
        {
            return "";
        }
        var uptime = now - started;
        return uptime.TotalDays >= 1 ? $"{(int)uptime.TotalDays} 天"
            : uptime.TotalHours >= 1 ? $"{(int)uptime.TotalHours} 小时"
            : uptime.TotalMinutes >= 1 ? $"{(int)uptime.TotalMinutes} 分钟"
            : "不到 1 分钟";
    }

    public static bool ShareOneAddress(IReadOnlyList<ConnectorInfo> connectors) =>
        connectors.Count > 1
        && connectors.All(c => !string.IsNullOrEmpty(c.OriginIp))
        && connectors.Select(c => c.OriginIp).Distinct().Count() == 1;
}

public sealed record DnsRecordInfo(string Id, string Name, string Type, string Content);

public sealed record PublishedApp(
    string Hostname,
    string Service,
    string? Path = null,
    System.Text.Json.Nodes.JsonObject? OriginRequest = null);

public sealed record OAuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

public enum ServiceKind
{
    Http,
    Tcp,
    Other,
}

public enum DnsState
{
    Ok,
    Missing,
    OtherTunnel,
    ZoneNotFound,
}
