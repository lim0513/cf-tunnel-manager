using System.Collections.Concurrent;

namespace CfTunnelManager.Core;

/// <summary>Adds and removes published applications, keeping routes and DNS in step.</summary>
public sealed class AppManager(ICloudflareApi api)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tunnelLocks = new();

    public async Task AddAppAsync(
        string accountId,
        string tunnelId,
        string hostname,
        string service,
        IReadOnlyList<ZoneInfo> zones,
        CancellationToken ct)
    {
        var zone = RequireZone(zones, hostname);
        var existing = await api.GetDnsRecordsAsync(zone.Id, hostname, ct);
        var hasRecord = existing.Any(r => DnsEvaluator.IsTunnelRecord(r, tunnelId));
        if (!hasRecord && existing.Count > 0)
        {
            throw new InvalidOperationException($"{hostname} 已有其他 DNS 记录，未做任何修改。");
        }

        await WithTunnelLockAsync(tunnelId, async () =>
        {
            var config = await RequireConfigAsync(accountId, tunnelId, ct);
            await api.PutTunnelConfigAsync(accountId, tunnelId, IngressEditor.WithApp(config, hostname, service), ct);

            if (hasRecord)
            {
                return;
            }
            try
            {
                await api.CreateTunnelDnsRecordAsync(zone.Id, hostname, tunnelId, ct);
            }
            catch
            {
                await api.PutTunnelConfigAsync(accountId, tunnelId, config, CancellationToken.None);
                throw;
            }
        });
    }

    public async Task RemoveAppAsync(
        string accountId,
        string tunnelId,
        PublishedApp app,
        bool deleteDns,
        IReadOnlyList<ZoneInfo> zones,
        CancellationToken ct)
    {
        await WithTunnelLockAsync(tunnelId, async () =>
        {
            var config = await RequireConfigAsync(accountId, tunnelId, ct);
            var updated = IngressEditor.WithoutApp(config, app);
            await api.PutTunnelConfigAsync(accountId, tunnelId, updated, ct);

            var stillRouted = IngressEditor.ListApps(updated).Any(a => IngressEditor.SameHost(a.Hostname, app.Hostname));
            if (!deleteDns || stillRouted || ZoneMatcher.Find(zones, app.Hostname) is not { } zone)
            {
                return;
            }

            var records = await api.GetDnsRecordsAsync(zone.Id, app.Hostname, ct);
            foreach (var record in records.Where(r => DnsEvaluator.IsTunnelRecord(r, tunnelId)))
            {
                await api.DeleteDnsRecordAsync(zone.Id, record.Id, ct);
            }
        });
    }

    public async Task RepairDnsAsync(string tunnelId, string hostname, IReadOnlyList<ZoneInfo> zones, CancellationToken ct)
    {
        var zone = RequireZone(zones, hostname);
        var existing = await api.GetDnsRecordsAsync(zone.Id, hostname, ct);
        if (existing.Count > 0)
        {
            throw new InvalidOperationException($"{hostname} 已有其他 DNS 记录，未做任何修改。");
        }
        await api.CreateTunnelDnsRecordAsync(zone.Id, hostname, tunnelId, ct);
    }

    private static ZoneInfo RequireZone(IReadOnlyList<ZoneInfo> zones, string hostname) =>
        ZoneMatcher.Find(zones, hostname)
        ?? throw new InvalidOperationException($"账号下没有 {hostname} 对应的域名。");

    private async Task<System.Text.Json.Nodes.JsonObject> RequireConfigAsync(string accountId, string tunnelId, CancellationToken ct) =>
        await api.GetTunnelConfigAsync(accountId, tunnelId, ct)
        ?? throw new InvalidOperationException("这条隧道使用本地配置文件，不能通过 API 修改路由。");

    private async Task WithTunnelLockAsync(string tunnelId, Func<Task> action)
    {
        var gate = _tunnelLocks.GetOrAdd(tunnelId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
