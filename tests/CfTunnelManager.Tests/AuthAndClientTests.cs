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
