using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SiteShield.Services;

public sealed class AllowListProxy : IDisposable
{
    private readonly object _gate = new();
    private HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public const int Port = 8888;

    public void Start(IEnumerable<string> allowedDomains)
    {
        Stop();
        lock (_gate) _allowed = Normalize(allowedDomains);
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start();
        _acceptTask = AcceptLoopAsync(_listener, _cts.Token);
    }

    public void UpdateAllowList(IEnumerable<string> allowedDomains)
    {
        lock (_gate) _allowed = Normalize(allowedDomains);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch { client?.Dispose(); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using var stream = client.GetStream();
        stream.ReadTimeout = 10000;
        stream.WriteTimeout = 10000;

        var request = await ReadHeaderAsync(stream, token);
        if (request.Length == 0) return;

        var firstLine = request.Split("\r\n", 2)[0];
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;

        if (parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            var target = parts[1];
            var colon = target.LastIndexOf(':');
            var host = colon > 0 ? target[..colon] : target;
            var port = colon > 0 && int.TryParse(target[(colon + 1)..], out var parsed) ? parsed : 443;
            if (!IsAllowed(host))
            {
                await SendTextAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", token);
                return;
            }
            await TunnelAsync(stream, host, port, token);
            return;
        }

        Uri? uri = null;
        if (Uri.TryCreate(parts[1], UriKind.Absolute, out var absolute)) uri = absolute;
        else
        {
            var hostHeader = request.Split("\r\n").FirstOrDefault(x => x.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
            if (hostHeader is null) return;
            var host = hostHeader[5..].Trim();
            if (Uri.TryCreate($"http://{host}{parts[1]}", UriKind.Absolute, out var reconstructed)) uri = reconstructed;
        }
        if (uri is null || !IsAllowed(uri.Host))
        {
            await SendTextAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", token);
            return;
        }

        using var upstream = new TcpClient();
        await upstream.ConnectAsync(uri.Host, uri.Port > 0 ? uri.Port : 80, token);
        using var upstreamStream = upstream.GetStream();
        var forwarded = RewriteHttpRequest(request, uri);
        await upstreamStream.WriteAsync(Encoding.ASCII.GetBytes(forwarded), token);
        await RelayAsync(upstreamStream, stream, token);
    }

    private async Task TunnelAsync(NetworkStream client, string host, int port, CancellationToken token)
    {
        using var upstream = new TcpClient();
        await upstream.ConnectAsync(host, port, token);
        using var upstreamStream = upstream.GetStream();
        await SendTextAsync(client, "HTTP/1.1 200 Connection Established\r\n\r\n", token);
        var a = RelayAsync(client, upstreamStream, token);
        var b = RelayAsync(upstreamStream, client, token);
        await Task.WhenAny(a, b);
    }

    private static async Task RelayAsync(Stream from, Stream to, CancellationToken token)
    {
        var buffer = new byte[81920];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await from.ReadAsync(buffer, token);
                if (read == 0) break;
                await to.WriteAsync(buffer.AsMemory(0, read), token);
                await to.FlushAsync(token);
            }
        }
        catch { }
    }

    private static string RewriteHttpRequest(string request, Uri uri)
    {
        var lines = request.Split("\r\n").ToList();
        if (lines.Count > 0)
        {
            var parts = lines[0].Split(' ', 3);
            if (parts.Length == 3) lines[0] = $"{parts[0]} {uri.PathAndQuery} {parts[2]}";
        }
        return string.Join("\r\n", lines);
    }

    private bool IsAllowed(string host)
    {
        var normalized = host.TrimEnd('.').ToLowerInvariant();
        lock (_gate)
        {
            return _allowed.Contains(normalized) || _allowed.Any(x => normalized.EndsWith("." + x, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static HashSet<string> Normalize(IEnumerable<string> domains) => new(
        domains.Select(x => x.Trim().ToLowerInvariant().Replace("https://", "").Replace("http://", "").Split('/')[0].Trim('.'))
            .Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);

    private static async Task<string> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        var bytes = new List<byte>();
        var buffer = new byte[2048];
        while (bytes.Count < 65536)
        {
            var read = await stream.ReadAsync(buffer, token);
            if (read == 0) break;
            bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            var s = Encoding.ASCII.GetString(bytes.ToArray());
            if (s.Contains("\r\n\r\n", StringComparison.Ordinal)) return s;
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static Task SendTextAsync(NetworkStream stream, string text, CancellationToken token) => stream.WriteAsync(Encoding.ASCII.GetBytes(text), token).AsTask();
}
