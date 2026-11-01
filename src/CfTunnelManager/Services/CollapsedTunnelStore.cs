using System.IO;
using System.Text.Json;

namespace CfTunnelManager.Services;

/// <summary>Remembers which tunnel groups the user collapsed.</summary>
internal sealed class CollapsedTunnelStore
{
    private readonly string _path = Path.Combine(AppData.Directory, "collapsed.json");
    private readonly HashSet<string> _tunnelIds;

    public CollapsedTunnelStore()
    {
        _tunnelIds = Load();
    }

    public bool IsCollapsed(string tunnelId) => _tunnelIds.Contains(tunnelId);

    public void Set(string tunnelId, bool collapsed)
    {
        var changed = collapsed ? _tunnelIds.Add(tunnelId) : _tunnelIds.Remove(tunnelId);
        if (changed)
        {
            AppData.WriteAtomically(_path, JsonSerializer.SerializeToUtf8Bytes(_tunnelIds));
        }
    }

    private HashSet<string> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllBytes(_path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
