namespace CfTunnelManager.Core;

public sealed record AccountInfo(string Id, string Name);

public sealed record ZoneInfo(string Id, string Name, string AccountId);

public sealed record TunnelInfo(
    string Id,
    string Name,
    string Status,
    DateTimeOffset? ActiveAt,
    DateTimeOffset? InactiveAt,
    string? ConnectorVersion = null);

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
