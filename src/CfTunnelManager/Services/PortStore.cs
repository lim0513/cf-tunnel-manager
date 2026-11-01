using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CfTunnelManager.Services;

/// <summary>Remembers each hostname's local port so saved RDP and SSH profiles keep working.</summary>
internal sealed class PortStore
{
    private const int FirstPort = 15000;
    private const int LastPort = 15999;

    private readonly string _path = Path.Combine(AppData.Directory, "ports.json");
    private readonly Dictionary<string, int> _ports;

    public PortStore()
    {
        _ports = new Dictionary<string, int>(Load(), StringComparer.OrdinalIgnoreCase);
    }

    public int? Find(string hostname) => _ports.TryGetValue(hostname, out var port) ? port : null;

    public int GetOrAssign(string hostname)
    {
        if (Find(hostname) is { } saved)
        {
            if (!IsFree(saved))
            {
                throw new InvalidOperationException($"本地端口 {saved} 已被其他程序占用。");
            }
            return saved;
        }

        var taken = _ports.Values.ToHashSet();
        for (var port = FirstPort; port <= LastPort; port++)
        {
            if (!taken.Contains(port) && IsFree(port))
            {
                _ports[hostname] = port;
                AppData.WriteAtomically(_path, JsonSerializer.SerializeToUtf8Bytes(_ports));
                return port;
            }
        }
        throw new InvalidOperationException("没有可用的本地端口。");
    }

    private Dictionary<string, int> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllBytes(_path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
