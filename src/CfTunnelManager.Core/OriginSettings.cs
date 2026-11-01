using System.Text.Json.Nodes;

namespace CfTunnelManager.Core;

/// <summary>
/// The originRequest options the settings window edits. Empty values mean Cloudflare's default,
/// and fields not listed here stay untouched when the settings are written back.
/// </summary>
public sealed record OriginSettings
{
    public string HttpHostHeader { get; init; } = "";
    public bool DisableChunkedEncoding { get; init; }
    public bool Http2Origin { get; init; }

    public string OriginServerName { get; init; } = "";
    public string CaPool { get; init; } = "";
    public bool NoTlsVerify { get; init; }
    public bool MatchSniToHost { get; init; }

    public int? ConnectTimeout { get; init; }
    public int? TlsTimeout { get; init; }
    public int? TcpKeepAlive { get; init; }
    public int? KeepAliveTimeout { get; init; }
    public int? KeepAliveConnections { get; init; }
    public bool NoHappyEyeballs { get; init; }
    public string ProxyType { get; init; } = "";

    public bool AccessEnabled { get; init; }
    public bool AccessRequired { get; init; }
    public string AccessTeamName { get; init; } = "";
    public IReadOnlyList<string> AccessAudTags { get; init; } = [];

    public int CustomHttpCount => Count(HttpHostHeader.Length > 0, DisableChunkedEncoding);

    public int CustomTlsCount => Count(
        OriginServerName.Length > 0, CaPool.Length > 0, TlsTimeout is not null,
        NoTlsVerify, Http2Origin, MatchSniToHost);

    public int CustomConnectionCount => Count(
        ConnectTimeout is not null, KeepAliveTimeout is not null, KeepAliveConnections is not null,
        TcpKeepAlive is not null, ProxyType.Length > 0, NoHappyEyeballs);

    public int CustomAccessCount => AccessEnabled
        ? Math.Max(1, Count(AccessRequired, AccessTeamName.Length > 0, AccessAudTags.Count > 0))
        : 0;

    public static OriginSettings Read(JsonObject? origin)
    {
        var access = origin?["access"] as JsonObject;
        return new OriginSettings
        {
            HttpHostHeader = Text(origin, "httpHostHeader"),
            DisableChunkedEncoding = Flag(origin, "disableChunkedEncoding"),
            Http2Origin = Flag(origin, "http2Origin"),
            OriginServerName = Text(origin, "originServerName"),
            CaPool = Text(origin, "caPool"),
            NoTlsVerify = Flag(origin, "noTLSVerify"),
            MatchSniToHost = Flag(origin, "matchSNItoHost"),
            ConnectTimeout = Number(origin, "connectTimeout"),
            TlsTimeout = Number(origin, "tlsTimeout"),
            TcpKeepAlive = Number(origin, "tcpKeepAlive"),
            KeepAliveTimeout = Number(origin, "keepAliveTimeout"),
            KeepAliveConnections = Number(origin, "keepAliveConnections"),
            NoHappyEyeballs = Flag(origin, "noHappyEyeballs"),
            ProxyType = Text(origin, "proxyType"),
            AccessEnabled = access is not null,
            AccessRequired = Flag(access, "required"),
            AccessTeamName = Text(access, "teamName"),
            AccessAudTags = (access?["audTag"] as JsonArray ?? [])
                .Select(t => t?.GetValue<string>() ?? "")
                .Where(t => t.Length > 0)
                .ToList(),
        };
    }

    public JsonObject ApplyTo(JsonObject? existing)
    {
        var origin = existing?.DeepClone().AsObject() ?? [];

        SetText(origin, "httpHostHeader", HttpHostHeader);
        SetFlag(origin, "disableChunkedEncoding", DisableChunkedEncoding);
        SetFlag(origin, "http2Origin", Http2Origin);
        SetText(origin, "originServerName", OriginServerName);
        SetText(origin, "caPool", CaPool);
        SetFlag(origin, "noTLSVerify", NoTlsVerify);
        SetFlag(origin, "matchSNItoHost", MatchSniToHost);
        SetNumber(origin, "connectTimeout", ConnectTimeout);
        SetNumber(origin, "tlsTimeout", TlsTimeout);
        SetNumber(origin, "tcpKeepAlive", TcpKeepAlive);
        SetNumber(origin, "keepAliveTimeout", KeepAliveTimeout);
        SetNumber(origin, "keepAliveConnections", KeepAliveConnections);
        SetFlag(origin, "noHappyEyeballs", NoHappyEyeballs);
        SetText(origin, "proxyType", ProxyType);

        if (AccessEnabled)
        {
            var access = origin["access"] as JsonObject ?? [];
            access["required"] = AccessRequired;
            access["teamName"] = AccessTeamName;
            access["audTag"] = new JsonArray(AccessAudTags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
            origin["access"] = access;
        }
        else
        {
            origin.Remove("access");
        }
        return origin;
    }

    private static int Count(params bool[] fields) => fields.Count(f => f);

    private static string Text(JsonObject? node, string key) => node?[key]?.GetValue<string>() ?? "";

    private static bool Flag(JsonObject? node, string key) => node?[key]?.GetValue<bool>() ?? false;

    private static int? Number(JsonObject? node, string key) =>
        node?[key] is JsonValue value && value.TryGetValue<double>(out var number) ? (int)number : null;

    private static void SetText(JsonObject origin, string key, string value)
    {
        if (value.Length > 0)
        {
            origin[key] = value;
        }
        else
        {
            origin.Remove(key);
        }
    }

    private static void SetFlag(JsonObject origin, string key, bool value)
    {
        if (value)
        {
            origin[key] = true;
        }
        else
        {
            origin.Remove(key);
        }
    }

    private static void SetNumber(JsonObject origin, string key, int? value)
    {
        if (value is { } number)
        {
            origin[key] = number;
        }
        else
        {
            origin.Remove(key);
        }
    }
}
