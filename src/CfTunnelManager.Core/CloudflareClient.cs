using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace CfTunnelManager.Core;

public interface ICloudflareApi
{
    Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(CancellationToken ct);

    Task<IReadOnlyList<ZoneInfo>> GetZonesAsync(CancellationToken ct);

    Task<IReadOnlyList<TunnelInfo>> GetTunnelsAsync(string accountId, CancellationToken ct);

    Task<IReadOnlyList<ConnectorInfo>> GetConnectorsAsync(string accountId, string tunnelId, CancellationToken ct);

    /// <summary>Returns null for a tunnel whose routes are managed by a local config file.</summary>
    Task<JsonObject?> GetTunnelConfigAsync(string accountId, string tunnelId, CancellationToken ct);

    Task PutTunnelConfigAsync(string accountId, string tunnelId, JsonObject config, CancellationToken ct);

    Task<IReadOnlyList<DnsRecordInfo>> GetDnsRecordsAsync(string zoneId, string? name, CancellationToken ct);

    Task CreateTunnelDnsRecordAsync(string zoneId, string hostname, string tunnelId, CancellationToken ct);

    Task DeleteDnsRecordAsync(string zoneId, string recordId, CancellationToken ct);
}

public sealed class CloudflareApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class CloudflareClient(HttpClient http, IAccessTokenProvider tokens) : ICloudflareApi
{
    private const string BaseUrl = "https://api.cloudflare.com/client/v4";
    private const int PageSize = 50;

    public async Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(CancellationToken ct)
    {
        var items = await GetAllPagesAsync("/accounts", ct);
        return items.Select(a => new AccountInfo(Text(a, "id"), Text(a, "name"))).ToList();
    }

    public async Task<IReadOnlyList<ZoneInfo>> GetZonesAsync(CancellationToken ct)
    {
        var items = await GetAllPagesAsync("/zones", ct);
        return items.Select(z => new ZoneInfo(Text(z, "id"), Text(z, "name"), z["account"]?["id"]?.GetValue<string>() ?? "")).ToList();
    }

    public async Task<IReadOnlyList<TunnelInfo>> GetTunnelsAsync(string accountId, CancellationToken ct)
    {
        var items = await GetAllPagesAsync($"/accounts/{accountId}/cfd_tunnel?is_deleted=false", ct);
        return items.Select(t => new TunnelInfo(
            Text(t, "id"),
            Text(t, "name"),
            t["status"]?.GetValue<string>() ?? "unknown",
            Date(t, "conns_active_at"),
            Date(t, "conns_inactive_at"),
            ConnectorVersions(t),
            ConnectorCount(t))).ToList();
    }

    public async Task<IReadOnlyList<ConnectorInfo>> GetConnectorsAsync(string accountId, string tunnelId, CancellationToken ct)
    {
        var root = await SendAsync(HttpMethod.Get, $"/accounts/{accountId}/cfd_tunnel/{tunnelId}/connections", null, ct);
        return (root["result"] as JsonArray ?? [])
            .Where(c => c is not null)
            .Select(c =>
            {
                var connections = (c!["conns"] as JsonArray ?? []).Where(n => n is not null).ToList();
                return new ConnectorInfo(
                    Text(c, "id"),
                    Optional(c, "hostname"),
                    connections.Select(n => Optional(n!, "origin_ip")).FirstOrDefault(ip => ip is not null),
                    connections.Select(n => Text(n!, "colo_name")).Where(n => n.Length > 0).Distinct().ToList(),
                    Optional(c, "version"),
                    Optional(c, "arch"),
                    connections.Select(n => Date(n!, "opened_at")).Min() ?? Date(c, "run_at"));
            })
            .ToList();
    }

    public async Task<JsonObject?> GetTunnelConfigAsync(string accountId, string tunnelId, CancellationToken ct)
    {
        var root = await SendAsync(HttpMethod.Get, $"/accounts/{accountId}/cfd_tunnel/{tunnelId}/configurations", null, ct);
        return root["result"]?["config"]?.DeepClone() as JsonObject;
    }

    public Task PutTunnelConfigAsync(string accountId, string tunnelId, JsonObject config, CancellationToken ct) =>
        SendAsync(
            HttpMethod.Put,
            $"/accounts/{accountId}/cfd_tunnel/{tunnelId}/configurations",
            new JsonObject { ["config"] = config.DeepClone() },
            ct);

    public async Task<IReadOnlyList<DnsRecordInfo>> GetDnsRecordsAsync(string zoneId, string? name, CancellationToken ct)
    {
        var filter = name is null ? "?type=CNAME" : $"?name={Uri.EscapeDataString(name)}";
        var items = await GetAllPagesAsync($"/zones/{zoneId}/dns_records{filter}", ct);
        return items.Select(r => new DnsRecordInfo(Text(r, "id"), Text(r, "name"), Text(r, "type"), Text(r, "content"))).ToList();
    }

    public Task CreateTunnelDnsRecordAsync(string zoneId, string hostname, string tunnelId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"/zones/{zoneId}/dns_records", new JsonObject
        {
            ["type"] = "CNAME",
            ["name"] = hostname,
            ["content"] = DnsEvaluator.TunnelTarget(tunnelId),
            ["proxied"] = true,
        }, ct);

    public Task DeleteDnsRecordAsync(string zoneId, string recordId, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"/zones/{zoneId}/dns_records/{recordId}", null, ct);

    private async Task<List<JsonNode>> GetAllPagesAsync(string path, CancellationToken ct)
    {
        var results = new List<JsonNode>();
        var separator = path.Contains('?') ? '&' : '?';
        for (var page = 1; ; page++)
        {
            var root = await SendAsync(HttpMethod.Get, $"{path}{separator}page={page}&per_page={PageSize}", null, ct);
            var items = root["result"]!.AsArray().Where(i => i is not null).Select(i => i!).ToList();
            results.AddRange(items);

            var totalPages = root["result_info"]?["total_pages"]?.GetValue<int>();
            var finished = totalPages is { } total ? page >= total : items.Count < PageSize;
            if (finished)
            {
                return results;
            }
        }
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAccessTokenAsync(ct));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        JsonNode? root = null;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            // Reported below using the status code.
        }

        if (!response.IsSuccessStatusCode || root?["success"]?.GetValue<bool>() != true)
        {
            var errors = root?["errors"]?.AsArray().Select(e => e?["message"]?.GetValue<string>()).Where(m => m is not null);
            var detail = errors is null ? "" : string.Join("; ", errors);
            throw new CloudflareApiException(
                (int)response.StatusCode,
                $"Cloudflare API 错误 {(int)response.StatusCode}{(detail.Length > 0 ? $"：{detail}" : "")}");
        }
        return root!;
    }

    private static string? ConnectorVersions(JsonNode tunnel)
    {
        var versions = (tunnel["connections"] as JsonArray ?? [])
            .Select(c => c?["client_version"]?.GetValue<string>())
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct()
            .ToList();
        return versions.Count > 0 ? string.Join("、", versions) : null;
    }

    /// <summary>Each connector holds several connections, all sharing its client_id.</summary>
    private static int ConnectorCount(JsonNode tunnel)
    {
        var connections = (tunnel["connections"] as JsonArray ?? []).Where(c => c is not null).ToList();
        var clients = connections
            .Select(c => c!["client_id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .Count();
        return clients > 0 ? clients : Math.Min(connections.Count, 1);
    }

    private static string? Optional(JsonNode node, string key) =>
        node[key]?.GetValue<string>() is { Length: > 0 } value ? value : null;

    private static string Text(JsonNode node, string key) => node[key]?.GetValue<string>() ?? "";

    private static DateTimeOffset? Date(JsonNode node, string key) =>
        DateTimeOffset.TryParse(node[key]?.GetValue<string>(), out var value) ? value : null;
}
