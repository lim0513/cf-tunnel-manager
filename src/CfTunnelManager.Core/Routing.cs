using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace CfTunnelManager.Core;

/// <summary>
/// Edits a tunnel configuration. PUT replaces the whole config, so every method works on a copy
/// and leaves fields it does not know about, such as originRequest and warp-routing, in place.
/// </summary>
public static class IngressEditor
{
    private const string CatchAll = "http_status:404";

    public static IReadOnlyList<PublishedApp> ListApps(JsonObject config) =>
        Rules(config)
            .Where(r => Hostname(r) is not null)
            .Select(r => new PublishedApp(Hostname(r)!, r["service"]?.GetValue<string>() ?? "", r["path"]?.GetValue<string>()))
            .ToList();

    public static JsonObject WithApp(JsonObject config, string hostname, string service)
    {
        var copy = config.DeepClone().AsObject();
        if (copy["ingress"] is not JsonArray ingress)
        {
            ingress = [];
            copy["ingress"] = ingress;
        }

        if (Rules(copy).Any(r => SameHost(Hostname(r), hostname)))
        {
            throw new InvalidOperationException($"{hostname} 已经在这条隧道上。");
        }

        var catchAllIndex = ingress.ToList().FindIndex(r => r is not null && Hostname(r) is null);
        if (catchAllIndex < 0)
        {
            ingress.Add(new JsonObject { ["service"] = CatchAll });
            catchAllIndex = ingress.Count - 1;
        }
        ingress.Insert(catchAllIndex, new JsonObject { ["hostname"] = hostname, ["service"] = service });
        return copy;
    }

    public static JsonObject WithoutApp(JsonObject config, PublishedApp app)
    {
        var copy = config.DeepClone().AsObject();
        if (copy["ingress"] is JsonArray ingress)
        {
            var target = ingress.FirstOrDefault(r => r is not null
                && SameHost(Hostname(r), app.Hostname)
                && r["path"]?.GetValue<string>() == app.Path);
            if (target is not null)
            {
                ingress.Remove(target);
            }
        }
        return copy;
    }

    public static bool SameHost(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string? Hostname(JsonNode rule) => rule["hostname"]?.GetValue<string>();

    private static IEnumerable<JsonNode> Rules(JsonObject config) =>
        (config["ingress"] as JsonArray ?? []).Where(r => r is not null).Select(r => r!);
}

public static class ServiceClassifier
{
    public static ServiceKind Classify(string service)
    {
        var separator = service.IndexOf("://", StringComparison.Ordinal);
        var scheme = separator < 0 ? "" : service[..separator].ToLowerInvariant();
        return scheme switch
        {
            "http" or "https" => ServiceKind.Http,
            "tcp" or "ssh" or "rdp" or "smb" => ServiceKind.Tcp,
            _ => ServiceKind.Other,
        };
    }
}

public static partial class AppInputValidator
{
    public static bool IsValidHostname(string hostname) => HostnameRegex().IsMatch(hostname);

    public static bool IsValidService(string service) =>
        ServiceClassifier.Classify(service) != ServiceKind.Other
        && Uri.TryCreate(service, UriKind.Absolute, out var uri)
        && uri.Host.Length > 0;

    [GeneratedRegex(@"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$")]
    private static partial Regex HostnameRegex();
}

public static class ZoneMatcher
{
    public static ZoneInfo? Find(IEnumerable<ZoneInfo> zones, string hostname) =>
        zones
            .Where(z => IngressEditor.SameHost(z.Name, hostname)
                || hostname.EndsWith("." + z.Name, StringComparison.OrdinalIgnoreCase))
            .MaxBy(z => z.Name.Length);
}

public static class DnsEvaluator
{
    public static string TunnelTarget(string tunnelId) => $"{tunnelId}.cfargotunnel.com";

    public static DnsState Evaluate(string hostname, string tunnelId, IEnumerable<DnsRecordInfo> records)
    {
        var matching = records.Where(r => IngressEditor.SameHost(r.Name, hostname)).ToList();
        if (matching.Count == 0)
        {
            return DnsState.Missing;
        }
        return matching.Any(r => IsTunnelRecord(r, tunnelId)) ? DnsState.Ok : DnsState.OtherTunnel;
    }

    public static bool IsTunnelRecord(DnsRecordInfo record, string tunnelId) =>
        record.Type.Equals("CNAME", StringComparison.OrdinalIgnoreCase)
        && record.Content.Equals(TunnelTarget(tunnelId), StringComparison.OrdinalIgnoreCase);
}
