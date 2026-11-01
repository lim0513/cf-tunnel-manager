using System.IO;
using System.Text;

namespace CfTunnelManager.Services;

/// <summary>Keeps the user's own OAuth client ID. A client ID is not a secret.</summary>
internal sealed class ClientIdStore
{
    private readonly string _path = Path.Combine(AppData.Directory, "client-id.txt");

    public string? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }
        var value = File.ReadAllText(_path).Trim();
        return value.Length > 0 ? value : null;
    }

    public void Save(string clientId) => AppData.WriteAtomically(_path, Encoding.UTF8.GetBytes(clientId));

    public void Clear() => File.Delete(_path);
}
