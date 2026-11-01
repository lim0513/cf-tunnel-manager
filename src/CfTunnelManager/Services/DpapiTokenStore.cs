using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using CfTunnelManager.Core;

namespace CfTunnelManager.Services;

/// <summary>Stores tokens encrypted for the current Windows user.</summary>
internal sealed class DpapiTokenStore : ITokenStore
{
    private readonly string _path = Path.Combine(AppData.Directory, "tokens.bin");

    public OAuthTokens? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<OAuthTokens>(plain);
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public void Save(OAuthTokens tokens)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(tokens);
        AppData.WriteAtomically(_path, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
    }

    public void Clear() => File.Delete(_path);
}
