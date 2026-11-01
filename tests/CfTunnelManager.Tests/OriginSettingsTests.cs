using System.Text.Json.Nodes;
using CfTunnelManager.Core;

namespace CfTunnelManager.Tests;

public class OriginSettingsTests
{
    private static JsonObject Config() => JsonNode.Parse("""
        {
          "ingress": [
            {
              "hostname": "a.example.com",
              "service": "https://origin:5001",
              "originRequest": {
                "noTLSVerify": true,
                "connectTimeout": 30,
                "bastionMode": true,
                "access": { "required": true, "teamName": "team", "audTag": ["aud1", "aud2"], "future": 1 }
              }
            },
            { "hostname": "b.example.com", "service": "http://b" },
            { "service": "http_status:404" }
          ],
          "warp-routing": { "enabled": false }
        }
        """)!.AsObject();

    private static PublishedApp App(string host) => IngressEditor.ListApps(Config()).Single(a => a.Hostname == host);

    [Fact]
    public void Read_ParsesFieldsFromTheRule()
    {
        var settings = OriginSettings.Read(App("a.example.com").OriginRequest);

        Assert.True(settings.NoTlsVerify);
        Assert.Equal(30, settings.ConnectTimeout);
        Assert.True(settings.AccessEnabled);
        Assert.True(settings.AccessRequired);
        Assert.Equal("team", settings.AccessTeamName);
        Assert.Equal(["aud1", "aud2"], settings.AccessAudTags);
    }

    [Fact]
    public void Read_AcceptsFractionalNumbers()
    {
        var origin = JsonNode.Parse("""{ "connectTimeout": 30.0 }""")!.AsObject();

        Assert.Equal(30, OriginSettings.Read(origin).ConnectTimeout);
    }

    [Fact]
    public void ReadThenWrite_KeepsEverythingUnchanged()
    {
        var app = App("a.example.com");

        var updated = IngressEditor.WithOriginSettings(Config(), app, OriginSettings.Read(app.OriginRequest));

        Assert.True(JsonNode.DeepEquals(Config(), updated));
    }

    [Fact]
    public void ClearingKnownFields_KeepsUnknownOnes()
    {
        var app = App("a.example.com");

        var updated = IngressEditor.WithOriginSettings(Config(), app, new OriginSettings());

        var origin = IngressEditor.ListApps(updated)[0].OriginRequest!;
        Assert.True(origin["bastionMode"]!.GetValue<bool>());
        Assert.Single(origin);
    }

    [Fact]
    public void EditingAccess_KeepsItsUnknownFields()
    {
        var app = App("a.example.com");
        var settings = OriginSettings.Read(app.OriginRequest) with { AccessTeamName = "other" };

        var updated = IngressEditor.WithOriginSettings(Config(), app, settings);

        var access = IngressEditor.ListApps(updated)[0].OriginRequest!["access"]!;
        Assert.Equal("other", access["teamName"]!.GetValue<string>());
        Assert.Equal(1, access["future"]!.GetValue<int>());
    }

    [Fact]
    public void EmptySettings_RemoveTheOriginRequestKey()
    {
        var config = Config();
        var app = new PublishedApp("b.example.com", "http://b");

        var updated = IngressEditor.WithOriginSettings(config, app, new OriginSettings());

        Assert.False(updated["ingress"]![1]!.AsObject().ContainsKey("originRequest"));
    }

    [Fact]
    public void Settings_OnlyChangeTheMatchingApp()
    {
        var app = new PublishedApp("b.example.com", "http://b");

        var updated = IngressEditor.WithOriginSettings(Config(), app, new OriginSettings { HttpHostHeader = "x" });

        var apps = IngressEditor.ListApps(updated);
        Assert.Equal("x", apps[1].OriginRequest!["httpHostHeader"]!.GetValue<string>());
        Assert.True(apps[0].OriginRequest!["noTLSVerify"]!.GetValue<bool>());
        Assert.True(updated["warp-routing"] is JsonObject);
    }

    [Fact]
    public void MissingApp_IsRejected()
    {
        var app = new PublishedApp("gone.example.com", "http://x");

        Assert.Throws<InvalidOperationException>(() => IngressEditor.WithOriginSettings(Config(), app, new OriginSettings()));
    }

    [Fact]
    public void SectionCounts_MatchTheDashboardGroups()
    {
        var settings = new OriginSettings
        {
            HttpHostHeader = "x",
            TlsTimeout = 12,
            Http2Origin = true,
            ConnectTimeout = 45,
            KeepAliveTimeout = 77,
            KeepAliveConnections = 55,
            TcpKeepAlive = 33,
            ProxyType = "socks",
            NoHappyEyeballs = true,
            AccessEnabled = true,
            AccessRequired = true,
            AccessTeamName = "team",
            AccessAudTags = ["aud"],
        };

        Assert.Equal(1, settings.CustomHttpCount);
        Assert.Equal(2, settings.CustomTlsCount);
        Assert.Equal(6, settings.CustomConnectionCount);
        Assert.Equal(3, settings.CustomAccessCount);
        Assert.Equal(0, new OriginSettings().CustomConnectionCount);
    }

    [Fact]
    public void AccessCount_IsAtLeastOneWhenEnabled()
    {
        Assert.Equal(1, new OriginSettings { AccessEnabled = true }.CustomAccessCount);
    }
}
