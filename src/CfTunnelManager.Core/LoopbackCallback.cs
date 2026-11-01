using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace CfTunnelManager.Core;

/// <summary>Receives the OAuth redirect on the loopback address.</summary>
public sealed class LoopbackCallback : IDisposable
{
    private const string Page = "<html><meta charset=\"utf-8\"><body>登录完成，可以关闭此页面。</body></html>";

    private readonly TcpListener _listener;

    public LoopbackCallback(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
    }

    public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken ct)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(ct);
            string? header;
            do
            {
                header = await reader.ReadLineAsync(ct);
            } while (!string.IsNullOrEmpty(header));

            var target = requestLine?.Split(' ').ElementAtOrDefault(1);
            if (target is null || !target.StartsWith("/callback", StringComparison.Ordinal))
            {
                await RespondAsync(stream, "404 Not Found", ct);
                continue;
            }

            await RespondAsync(stream, "200 OK", ct);
            var query = HttpUtility.ParseQueryString(target[(target.IndexOf('?') + 1)..]);
            if (query["error"] is { } error)
            {
                throw new InvalidOperationException($"授权失败：{error}");
            }
            if (query["state"] != expectedState)
            {
                throw new InvalidOperationException("授权回调的 state 不匹配。");
            }
            return query["code"] ?? throw new InvalidOperationException("授权回调缺少 code。");
        }
    }

    public void Dispose() => _listener.Stop();

    private static async Task RespondAsync(NetworkStream stream, string status, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(Page);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(body, ct);
    }
}
