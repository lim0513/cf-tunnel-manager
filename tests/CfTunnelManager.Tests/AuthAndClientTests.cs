using System.Net;
using System.Net.Http;
using System.Text;
using CfTunnelManager.Core;
using Microsoft.Extensions.Time.Testing;

namespace CfTunnelManager.Tests;

public class AuthAndClientTests
{
    private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    [Fact]
    public void Challenge_MatchesRfc7636Example()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            OAuthClient.CreateChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void AuthorizationUrl_UsesTheCurrentClientId()
    {
        var oauth = new OAuthClient(new HttpClient(), "first") { ClientId = "second" };

        Assert.Contains("client_id=second", oauth.CreateAuthorizationUrl("challenge", "state"));
    }

    [Fact]
    public async Task ExpiredToken_RefreshesOnceAndPersistsRotatedTokenFirst()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var refreshCalls = 0;
        var handler = new StubHandler(async request =>
        {
            Interlocked.Increment(ref refreshCalls);
            await Task.Delay(20);
            return Json("""{"access_token":"new","refresh_token":"r2","expires_in":3600}""");
        });
        var store = new MemoryStore(new OAuthTokens("old", "r1", clock.GetUtcNow()));
        var session = new AuthSession(new OAuthClient(new HttpClient(handler), "id", clock), store, clock);

        var tokens = await Task.WhenAll(
            session.GetAccessTokenAsync(default),
            session.GetAccessTokenAsync(default),
            session.GetAccessTokenAsync(default));

        Assert.All(tokens, t => Assert.Equal("new", t));
        Assert.Equal(1, refreshCalls);
        Assert.Equal("r2", store.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task RejectedRefresh_ClearsStoredTokens()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var handler = new StubHandler(_ => Task.FromResult(Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest)));
        var store = new MemoryStore(new OAuthTokens("old", "r1", clock.GetUtcNow()));
        var session = new AuthSession(new OAuthClient(new HttpClient(handler), "id", clock), store, clock);

        await Assert.ThrowsAsync<ReauthenticationRequiredException>(() => session.GetAccessTokenAsync(default));

        Assert.Null(store.Tokens);
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public async Task ServerErrorDuringRefresh_KeepsStoredTokens()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var handler = new StubHandler(_ => Task.FromResult(Json("{}", HttpStatusCode.BadGateway)));
        var store = new MemoryStore(new OAuthTokens("old", "r1", clock.GetUtcNow()));
        var session = new AuthSession(new OAuthClient(new HttpClient(handler), "id", clock), store, clock);

        await Assert.ThrowsAsync<HttpRequestException>(() => session.GetAccessTokenAsync(default));

        Assert.NotNull(store.Tokens);
    }

    [Fact]
    public async Task GetTunnels_ReadsEveryPage()
    {
        var pages = new List<string>();
        var handler = new StubHandler(request =>
        {
            var page = request.RequestUri!.Query.Contains("page=2") ? 2 : 1;
            pages.Add(request.RequestUri.Query);
            var tunnel = $$"""{"id":"t{{page}}","name":"n{{page}}","status":"healthy"}""";
            return Task.FromResult(Json($$$"""
                {"success":true,"result":[{{{tunnel}}}],"result_info":{"page":{{{page}}},"total_pages":2}}
                """));
        });
        var client = new CloudflareClient(new HttpClient(handler), new FixedToken());

        var tunnels = await client.GetTunnelsAsync("acct", default);

        Assert.Equal(["t1", "t2"], tunnels.Select(t => t.Id));
        Assert.Equal(2, pages.Count);
    }

    [Fact]
    public async Task GetTunnels_ReadsDistinctConnectorVersions()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("""
            {"success":true,"result_info":{"total_pages":1},"result":[
              {"id":"a","name":"a","status":"healthy","connections":[{"client_version":"2026.1.0"},{"client_version":"2026.1.0"},{"client_version":"2026.2.0"}]},
              {"id":"b","name":"b","status":"down","connections":[]}
            ]}
            """)));
        var client = new CloudflareClient(new HttpClient(handler), new FixedToken());

        var tunnels = await client.GetTunnelsAsync("acct", default);

        Assert.Equal("2026.1.0、2026.2.0", tunnels[0].ConnectorVersion);
        Assert.Null(tunnels[1].ConnectorVersion);
    }

    [Fact]
    public async Task GetTunnels_CountsConnectorsNotConnections()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("""
            {"success":true,"result_info":{"total_pages":1},"result":[
              {"id":"a","name":"a","status":"healthy","connections":[
                {"client_id":"one","client_version":"2026.1.0"},
                {"client_id":"one","client_version":"2026.1.0"},
                {"client_id":"two","client_version":"2026.2.0"}]},
              {"id":"b","name":"b","status":"healthy","connections":[{"client_id":"one","client_version":"2026.1.0"},{"client_id":"one","client_version":"2026.1.0"}]},
              {"id":"c","name":"c","status":"down","connections":[]}
            ]}
            """)));
        var client = new CloudflareClient(new HttpClient(handler), new FixedToken());

        var tunnels = await client.GetTunnelsAsync("acct", default);

        Assert.Equal([2, 1, 0], tunnels.Select(t => t.ConnectorCount));
    }

    [Fact]
    public async Task GetConnectors_ReadsEachConnector()
    {
        var handler = new StubHandler(request =>
        {
            Assert.EndsWith("/accounts/acct/cfd_tunnel/t1/connections", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json("""
                {"success":true,"result":[
                  {"id":"0cf14fd0-838d-465c","arch":"linux_amd64","version":"2026.6.1","run_at":"2026-11-01T06:00:00Z",
                   "conns":[{"colo_name":"lax13","origin_ip":"203.0.113.5"},{"colo_name":"lax08","origin_ip":"203.0.113.5"},{"colo_name":"lax13","origin_ip":"203.0.113.5"}]},
                  {"id":"b1cf809f","hostname":"win3","version":"2026.10.5","conns":[]}
                ]}
                """));
        });
        var client = new CloudflareClient(new HttpClient(handler), new FixedToken());

        var connectors = await client.GetConnectorsAsync("acct", "t1", default);

        Assert.Equal("0cf14fd0", connectors[0].DisplayName);
        Assert.Equal("203.0.113.5", connectors[0].OriginIp);
        Assert.Equal(["lax13", "lax08"], connectors[0].Locations);
        Assert.Equal("linux_amd64", connectors[0].Architecture);
        Assert.Equal("win3", connectors[1].DisplayName);
        Assert.Null(connectors[1].OriginIp);
    }

    [Fact]
    public async Task GetConnectors_MeasuresUptimeFromTheOldestOpenConnection()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("""
            {"success":true,"result":[
              {"id":"a","run_at":"2026-10-01T00:00:00Z","conns":[
                {"opened_at":"2026-11-01T06:30:00Z"},{"opened_at":"2026-11-01T06:10:00Z"}]},
              {"id":"b","run_at":"2026-10-01T00:00:00Z","conns":[]}
            ]}
            """)));
        var client = new CloudflareClient(new HttpClient(handler), new FixedToken());

        var connectors = await client.GetConnectorsAsync("acct", "t1", default);

        Assert.Equal(DateTimeOffset.Parse("2026-11-01T06:10:00Z"), connectors[0].StartedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z"), connectors[1].StartedAt);
    }

    [Theory]
    [InlineData(30, "不到 1 分钟")]
    [InlineData(1320, "22 分钟")]
    [InlineData(3700, "1 小时")]
    [InlineData(200000, "2 天")]
    public void UptimeText_UsesTheLargestUnit(int seconds, string expected)
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var connector = new ConnectorInfo("id", null, null, [], null, null, now.AddSeconds(-seconds));

        Assert.Equal(expected, connector.UptimeText(now));
    }

    [Fact]
    public void ShareOneAddress_RequiresEveryKnownAddressToMatch()
    {
        ConnectorInfo Connector(string? ip) => new("id", null, ip, [], null, null, null);

        Assert.True(ConnectorInfo.ShareOneAddress([Connector("1.1.1.1"), Connector("1.1.1.1")]));
        Assert.False(ConnectorInfo.ShareOneAddress([Connector("1.1.1.1"), Connector("2.2.2.2")]));
        Assert.False(ConnectorInfo.ShareOneAddress([Connector("1.1.1.1"), Connector(null)]));
        Assert.False(ConnectorInfo.ShareOneAddress([Connector("1.1.1.1")]));
    }

    [Fact]
    public async Task ApiError_SurfacesCloudflareMessage()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(
            """{"success":false,"errors":[{"code":1,"message":"no access"}],"result":null}""",
            HttpStatusCode.Forbidden)));
        var client = new CloudflareClient(new HttpClient(handler), new FixedToken());

        var error = await Assert.ThrowsAsync<CloudflareApiException>(() => client.GetZonesAsync(default));

        Assert.Equal(403, error.StatusCode);
        Assert.Contains("no access", error.Message);
    }

    private sealed class FixedToken : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult("token");
    }

    private sealed class MemoryStore(OAuthTokens? tokens) : ITokenStore
    {
        public OAuthTokens? Tokens { get; private set; } = tokens;

        public OAuthTokens? Load() => Tokens;

        public void Save(OAuthTokens value) => Tokens = value;

        public void Clear() => Tokens = null;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request);
    }
}
