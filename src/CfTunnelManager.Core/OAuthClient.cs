using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CfTunnelManager.Core;

public sealed class OAuthClient(HttpClient http, string clientId, TimeProvider? clock = null)
{
    public const string RedirectUri = "http://127.0.0.1:8976/callback";
    public const int RedirectPort = 8976;
    public const string Scopes = "offline_access account-settings.read dns.write zone.read argotunnel.write";

    private const string AuthorizeUrl = "https://dash.cloudflare.com/oauth2/auth";
    private const string TokenUrl = "https://dash.cloudflare.com/oauth2/token";
    private const string RevokeUrl = "https://dash.cloudflare.com/oauth2/revoke";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string ClientId { get; set; } = clientId;

    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string CreateChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public string CreateAuthorizationUrl(string challenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = Scopes,
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        return AuthorizeUrl + "?" + string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    public Task<OAuthTokens> ExchangeCodeAsync(string code, string verifier, CancellationToken ct) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier,
        }, ct);

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
        RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
        }, ct);

    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = refreshToken,
            ["client_id"] = ClientId,
        });
        using var response = await http.PostAsync(RevokeUrl, content, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<OAuthTokens> RequestTokensAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(TokenUrl, content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if ((int)response.StatusCode is 400 or 401)
        {
            throw new ReauthenticationRequiredException();
        }
        response.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(text)!;
        var refreshToken = json["refresh_token"]?.GetValue<string>();
        if (refreshToken is null && form.TryGetValue("refresh_token", out var previous))
        {
            refreshToken = previous;
        }
        return new OAuthTokens(
            json["access_token"]!.GetValue<string>(),
            refreshToken ?? throw new InvalidOperationException("Token response has no refresh token"),
            _clock.GetUtcNow().AddSeconds(json["expires_in"]!.GetValue<int>()));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class ReauthenticationRequiredException() : Exception("登录已失效，请重新登录。");
