using System.Text.Json.Nodes;
using CfTunnelManager.Core;

namespace CfTunnelManager.Tests;

public class AppManagerTests
{
    private static readonly ZoneInfo[] Zones = [new("z", "example.com", "acct")];

    [Fact]
    public async Task AddApp_WritesRouteAndDns()
    {
        var api = new FakeApi();

        await new AppManager(api).AddAppAsync("acct", "t1", "b.example.com", "tcp://h:1", Zones, default);

        Assert.Contains(IngressEditor.ListApps(api.Config), a => a.Hostname == "b.example.com");
        Assert.Single(api.Records);
        Assert.Equal("t1.cfargotunnel.com", api.Records[0].Content);
    }

    [Fact]
    public async Task AddApp_RefusesWhenAnotherRecordExists()
    {
        var api = new FakeApi();
        api.Records.Add(new DnsRecordInfo("r", "b.example.com", "CNAME", "t2.cfargotunnel.com"));
        var before = api.Config.ToJsonString();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AppManager(api).AddAppAsync("acct", "t1", "b.example.com", "tcp://h:1", Zones, default));

        Assert.Equal(before, api.Config.ToJsonString());
        Assert.Equal(0, api.PutCount);
    }

    [Fact]
    public async Task AddApp_ReusesOwnExistingRecord()
    {
        var api = new FakeApi();
        api.Records.Add(new DnsRecordInfo("r", "b.example.com", "CNAME", "t1.cfargotunnel.com"));

        await new AppManager(api).AddAppAsync("acct", "t1", "b.example.com", "tcp://h:1", Zones, default);

        Assert.Single(api.Records);
        Assert.Contains(IngressEditor.ListApps(api.Config), a => a.Hostname == "b.example.com");
    }

    [Fact]
    public async Task AddApp_RestoresRouteWhenDnsFails()
    {
        var api = new FakeApi { FailDnsCreate = true };
        var before = api.Config.ToJsonString();

        await Assert.ThrowsAsync<CloudflareApiException>(() =>
            new AppManager(api).AddAppAsync("acct", "t1", "b.example.com", "tcp://h:1", Zones, default));

        Assert.Equal(before, api.Config.ToJsonString());
    }

    [Fact]
    public async Task AddApp_RefusesLocallyManagedTunnel()
    {
        var api = new FakeApi { HasRemoteConfig = false };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AppManager(api).AddAppAsync("acct", "t1", "b.example.com", "tcp://h:1", Zones, default));
    }

    [Fact]
    public async Task ConcurrentAdds_KeepBothRoutes()
    {
        var api = new FakeApi { Delay = TimeSpan.FromMilliseconds(20) };
        var manager = new AppManager(api);

        await Task.WhenAll(
            manager.AddAppAsync("acct", "t1", "b.example.com", "tcp://h:1", Zones, default),
            manager.AddAppAsync("acct", "t1", "c.example.com", "tcp://h:2", Zones, default));

        var hosts = IngressEditor.ListApps(api.Config).Select(a => a.Hostname).ToList();
        Assert.Contains("b.example.com", hosts);
        Assert.Contains("c.example.com", hosts);
    }

    [Fact]
    public async Task RemoveApp_DeletesOnlyOwnDnsRecord()
    {
        var api = new FakeApi();
        api.Records.Add(new DnsRecordInfo("own", "a.example.com", "CNAME", "t1.cfargotunnel.com"));
        api.Records.Add(new DnsRecordInfo("foreign", "a.example.com", "CNAME", "t2.cfargotunnel.com"));

        await new AppManager(api).RemoveAppAsync("acct", "t1", new PublishedApp("a.example.com", "http://x"), true, Zones, default);

        var left = Assert.Single(api.Records);
        Assert.Equal("foreign", left.Id);
        Assert.Empty(IngressEditor.ListApps(api.Config));
    }

    [Fact]
    public async Task RemoveApp_KeepsDnsWhenAnotherPathStillRoutesTheHost()
    {
        var api = new FakeApi();
        api.Config = JsonNode.Parse("""
            { "ingress": [
              { "hostname": "a.example.com", "path": "/x", "service": "http://x" },
              { "hostname": "a.example.com", "service": "http://y" },
              { "service": "http_status:404" }
            ] }
            """)!.AsObject();
        api.Records.Add(new DnsRecordInfo("own", "a.example.com", "CNAME", "t1.cfargotunnel.com"));

        await new AppManager(api).RemoveAppAsync("acct", "t1", new PublishedApp("a.example.com", "http://x", "/x"), true, Zones, default);

        Assert.Single(api.Records);
    }

    [Fact]
    public async Task RepairDns_RefusesToOverwriteExistingRecord()
    {
        var api = new FakeApi();
        api.Records.Add(new DnsRecordInfo("r", "a.example.com", "A", "1.2.3.4"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AppManager(api).RepairDnsAsync("t1", "a.example.com", Zones, default));
    }

    private sealed class FakeApi : ICloudflareApi
    {
        public JsonObject Config { get; set; } = JsonNode.Parse("""
            { "ingress": [ { "hostname": "a.example.com", "service": "http://x" }, { "service": "http_status:404" } ] }
            """)!.AsObject();

        public List<DnsRecordInfo> Records { get; } = [];

        public bool FailDnsCreate { get; init; }

        public bool HasRemoteConfig { get; init; } = true;

        public TimeSpan Delay { get; init; }

        public int PutCount { get; private set; }

        public Task<IReadOnlyList<AccountInfo>> GetAccountsAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<ZoneInfo>> GetZonesAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<TunnelInfo>> GetTunnelsAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();

        public async Task<JsonObject?> GetTunnelConfigAsync(string accountId, string tunnelId, CancellationToken ct)
        {
            await Task.Delay(Delay, ct);
            return HasRemoteConfig ? Config.DeepClone().AsObject() : null;
        }

        public async Task PutTunnelConfigAsync(string accountId, string tunnelId, JsonObject config, CancellationToken ct)
        {
            await Task.Delay(Delay, ct);
            PutCount++;
            Config = config.DeepClone().AsObject();
        }

        public Task<IReadOnlyList<DnsRecordInfo>> GetDnsRecordsAsync(string zoneId, string? name, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DnsRecordInfo>>(Records.Where(r => name is null || r.Name == name).ToList());

        public Task CreateTunnelDnsRecordAsync(string zoneId, string hostname, string tunnelId, CancellationToken ct)
        {
            if (FailDnsCreate)
            {
                throw new CloudflareApiException(500, "boom");
            }
            lock (Records)
            {
                Records.Add(new DnsRecordInfo(Guid.NewGuid().ToString(), hostname, "CNAME", DnsEvaluator.TunnelTarget(tunnelId)));
            }
            return Task.CompletedTask;
        }

        public Task DeleteDnsRecordAsync(string zoneId, string recordId, CancellationToken ct)
        {
            Records.RemoveAll(r => r.Id == recordId);
            return Task.CompletedTask;
        }
    }
}
