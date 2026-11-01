using System.IO;

namespace CfTunnelManager.Services;

internal static class AppData
{
    public static string Directory { get; } = CreateDirectory();

    public static void WriteAtomically(string path, byte[] content)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CfTunnelManager");
        System.IO.Directory.CreateDirectory(path);
        return path;
    }
}
