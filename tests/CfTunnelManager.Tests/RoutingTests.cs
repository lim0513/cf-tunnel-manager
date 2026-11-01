using System.Text.Json.Nodes;
using CfTunnelManager.Core;

namespace CfTunnelManager.Tests;

public class RoutingTests
{
    private static JsonObject Config() => JsonNode.Parse("""
        {
          "ingress": [
            { "hostname": "a.example.com", "service": "http://localhost:80", "originRequest": { "noTLSVerify": true } },
            { "service": "http_status:404" }
          ],
          "warp-routing": { "enabled": false }
        }
        """)!.AsObject();

    [Fact]
    public void WithApp_InsertsBeforeCatchAllAndKeepsUnknownFields()
    {
        var updated = IngressEditor.WithApp(Config(), "b.example.com", "tcp://10.0.0.1:22");

        var ingress = updated["ingress"]!.AsArray();
        Assert.Equal("b.example.com", ingress[1]!["hostname"]!.GetValue<string>());
        Assert.Equal("http_status:404", ingress[2]!["service"]!.GetValue<string>());
        Assert.True(ingress[0]!["originRequest"]!["noTLSVerify"]!.GetValue<bool>());
        Assert.False(updated["warp-routing"]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void WithApp_DoesNotChangeTheInput()
    {
        var config = Config();
        var before = config.ToJsonString();

        IngressEditor.WithApp(config, "b.example.com", "tcp://10.0.0.1:22");

        Assert.Equal(before, config.ToJsonString());
    }

    [Fact]
    public void WithApp_AddsCatchAllWhenIngressIsMissing()
    {
        var updated = IngressEditor.WithApp(new JsonObject(), "b.example.com", "http://localhost");

        var ingress = updated["ingress"]!.AsArray();
        Assert.Equal(2, ingress.Count);
        Assert.Equal("http_status:404", ingress[1]!["service"]!.GetValue<string>());
    }

    [Fact]
    public void WithApp_RejectsDuplicateHostnameIgnoringCase()
    {
        Assert.Throws<InvalidOperationException>(() => IngressEditor.WithApp(Config(), "A.Example.com", "http://localhost"));
    }

    [Fact]
    public void WithoutApp_RemovesOnlyTheMatchingPath()
    {
        var config = JsonNode.Parse("""
            { "ingress": [
              { "hostname": "a.example.com", "path": "/x", "service": "http://x" },
              { "hostname": "a.example.com", "service": "http://y" },
              { "service": "http_status:404" }
            ] }
            """)!.AsObject();

        var updated = IngressEditor.WithoutApp(config, new PublishedApp("a.example.com", "http://x", "/x"));

        var app = Assert.Single(IngressEditor.ListApps(updated));
        Assert.Equal("http://y", app.Service);
    }

    [Fact]
    public void ListApps_SkipsCatchAll()
    {
        var app = Assert.Single(IngressEditor.ListApps(Config()));
        Assert.Equal("a.example.com", app.Hostname);
    }

    [Theory]
    [InlineData("http://localhost", ServiceKind.Http)]
    [InlineData("HTTPS://nas:5001", ServiceKind.Http)]
    [InlineData("tcp://192.168.1.10:22", ServiceKind.Tcp)]
    [InlineData("rdp://localhost:3389", ServiceKind.Tcp)]
    [InlineData("ssh://localhost:22", ServiceKind.Tcp)]
    [InlineData("smb://nas:445", ServiceKind.Tcp)]
    [InlineData("http_status:404", ServiceKind.Other)]
    [InlineData("unix:/tmp/socket", ServiceKind.Other)]
    public void Classify(string service, ServiceKind expected)
    {
        Assert.Equal(expected, ServiceClassifier.Classify(service));
    }

    [Theory]
    [InlineData("app.example.com", true)]
    [InlineData("a.b.example.com", true)]
    [InlineData("example", false)]
    [InlineData("-bad.example.com", false)]
    [InlineData("has space.example.com", false)]
    [InlineData("https://app.example.com", false)]
    public void HostnameValidation(string hostname, bool expected)
    {
        Assert.Equal(expected, AppInputValidator.IsValidHostname(hostname));
    }

    [Theory]
    [InlineData("http://localhost:8080", true)]
    [InlineData("tcp://192.168.1.10:22", true)]
    [InlineData("localhost:8080", false)]
    [InlineData("http_status:404", false)]
    [InlineData("ftp://host", false)]
    public void ServiceValidation(string service, bool expected)
    {
        Assert.Equal(expected, AppInputValidator.IsValidService(service));
    }

    [Fact]
    public void ZoneMatcher_PrefersLongestSuffixAndRejectsLookalikes()
    {
        var zones = new[] { new ZoneInfo("1", "example.com", "a"), new ZoneInfo("2", "sub.example.com", "a") };

        Assert.Equal("2", ZoneMatcher.Find(zones, "x.sub.example.com")!.Id);
        Assert.Equal("1", ZoneMatcher.Find(zones, "x.example.com")!.Id);
        Assert.Null(ZoneMatcher.Find(zones, "notexample.com"));
    }

    [Fact]
    public void DnsEvaluator_ReportsEachState()
    {
        var ok = new DnsRecordInfo("1", "a.example.com", "CNAME", "T1.cfargotunnel.com");
        var other = new DnsRecordInfo("2", "b.example.com", "CNAME", "t2.cfargotunnel.com");

        Assert.Equal(DnsState.Ok, DnsEvaluator.Evaluate("a.example.com", "t1", [ok, other]));
        Assert.Equal(DnsState.OtherTunnel, DnsEvaluator.Evaluate("b.example.com", "t1", [ok, other]));
        Assert.Equal(DnsState.Missing, DnsEvaluator.Evaluate("c.example.com", "t1", [ok, other]));
    }
}
