using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace CfTunnelManager.Services;

/// <summary>Runs one <c>cloudflared access tcp</c> process per connected hostname.</summary>
internal sealed class AccessConnectionManager : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);

    private readonly PortStore _ports = new();
    private readonly ProcessJob _job = new();
    private readonly Dictionary<string, Connection> _connections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised on a worker thread when a connection's process ends on its own.</summary>
    public event Action<string>? ConnectionLost;

    public int? GetPort(string hostname) => _connections.TryGetValue(hostname, out var c) ? c.Port : null;

    public int? GetSavedPort(string hostname) => _ports.Find(hostname);

    public bool IsPortUsedByAnother(string hostname, int port) => _ports.IsUsedByAnother(hostname, port);

    public void SetSavedPort(string hostname, int? port) => _ports.Set(hostname, port);

    public async Task<int> ConnectAsync(string hostname, CancellationToken ct)
    {
        if (GetPort(hostname) is { } existing)
        {
            return existing;
        }

        var executable = FindCloudflared()
            ?? throw new InvalidOperationException("没有找到 cloudflared，请先安装。");
        var port = _ports.GetOrAssign(hostname);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                ArgumentList = { "access", "tcp", "--hostname", hostname, "--url", $"127.0.0.1:{port}" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            },
            EnableRaisingEvents = true,
        };

        string? lastLine = null;
        process.ErrorDataReceived += (_, e) => lastLine = e.Data ?? lastLine;
        process.OutputDataReceived += (_, e) => lastLine = e.Data ?? lastLine;

        process.Start();
        _job.Add(process);
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        var connection = new Connection(process, port);
        try
        {
            await WaitUntilListeningAsync(process, port, ct);
        }
        catch (TimeoutException)
        {
            Stop(connection);
            throw new InvalidOperationException($"cloudflared 启动超时：{lastLine}");
        }
        catch (ProcessExitedException)
        {
            process.Dispose();
            throw new InvalidOperationException($"cloudflared 启动失败：{lastLine}");
        }
        catch
        {
            Stop(connection);
            throw;
        }

        _connections[hostname] = connection;
        process.Exited += (_, _) =>
        {
            if (!connection.StoppedByUser)
            {
                ConnectionLost?.Invoke(hostname);
            }
        };
        if (process.HasExited && !connection.StoppedByUser)
        {
            ConnectionLost?.Invoke(hostname);
        }
        return port;
    }

    public void Disconnect(string hostname)
    {
        if (_connections.Remove(hostname, out var connection))
        {
            Stop(connection);
        }
    }

    public void Forget(string hostname)
    {
        if (_connections.Remove(hostname, out var connection))
        {
            connection.Process.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var connection in _connections.Values)
        {
            Stop(connection);
        }
        _connections.Clear();
        _job.Dispose();
    }

    private static void Stop(Connection connection)
    {
        connection.StoppedByUser = true;
        try
        {
            connection.Process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        connection.Process.Dispose();
    }

    private static async Task WaitUntilListeningAsync(Process process, int port, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < StartTimeout)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new ProcessExitedException();
            }

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, ct);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(150, ct);
            }
        }
        throw new TimeoutException();
    }

    private static string? FindCloudflared()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var candidates = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), "cloudflared.exe"))
            .Concat(
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "cloudflared", "cloudflared.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared", "cloudflared.exe"),
            ]);
        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class Connection(Process process, int port)
    {
        public Process Process { get; } = process;

        public int Port { get; } = port;

        public bool StoppedByUser { get; set; }
    }

    private sealed class ProcessExitedException : Exception;
}
