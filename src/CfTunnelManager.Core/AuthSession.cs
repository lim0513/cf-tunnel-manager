namespace CfTunnelManager.Core;

public interface ITokenStore
{
    OAuthTokens? Load();

    void Save(OAuthTokens tokens);

    void Clear();
}

public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
}

/// <summary>
/// Owns the stored tokens. Cloudflare rotates refresh tokens, so refreshes run one at a time
/// and the replacement is persisted before anyone can use it.
/// </summary>
public sealed class AuthSession(OAuthClient oauth, ITokenStore store, TimeProvider? clock = null) : IAccessTokenProvider
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OAuthTokens? _tokens = store.Load();

    public bool IsSignedIn => _tokens is not null;

    public async Task SignInAsync(string code, string verifier, CancellationToken ct)
    {
        var tokens = await oauth.ExchangeCodeAsync(code, verifier, ct);
        await _gate.WaitAsync(ct);
        try
        {
            store.Save(tokens);
            _tokens = tokens;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var current = _tokens ?? throw new ReauthenticationRequiredException();
            if (current.ExpiresAt - _clock.GetUtcNow() > ExpiryMargin)
            {
                return current.AccessToken;
            }

            try
            {
                var refreshed = await oauth.RefreshAsync(current.RefreshToken, ct);
                store.Save(refreshed);
                _tokens = refreshed;
                return refreshed.AccessToken;
            }
            catch (ReauthenticationRequiredException)
            {
                store.Clear();
                _tokens = null;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var current = _tokens;
            store.Clear();
            _tokens = null;
            if (current is not null)
            {
                await oauth.RevokeAsync(current.RefreshToken, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
