using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SiteShield.Services;

public sealed class LocalWebProxy : IDisposable
{
    public const int Port = 8888;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private HashSet<string> _allowed = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning => _listener is not null;

    public void Start(IEnumerable<string> allowedDomains)
    {
        lock (_gate)
        {
            _allowed = NormalizeDomains(allowedDomains);
            if (_listener is not null) return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _ = AcceptLoopAsync(_listener, _cts.Token);
        }
    }

    public void UpdateAllowedDomains(IEnumerable<string> allowedDomains)
    {
        lock (_gate) _allowed = NormalizeDomains(allowedDomains);
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            try { _listener?.Stop(); } catch { }
            _listener = null;
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(token);
                _ = HandleClientAsync(client, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            using var stream = client.GetStream();
            stream.ReadTimeout = 10000;
            stream.WriteTimeout = 10000;

            try
            {
                var header = await ReadHeaderAsync(stream, token);
                if (header.Length == 0) return;

                var text = Encoding.ASCII.GetString(header);
                var lines = text.Split("\r\n", StringSplitOptions.None);
                if (lines.Length == 0) return;

                var requestLine = lines[0];
                var parts = requestLine.Split(' ', 3);
                if (parts.Length < 2) return;

                if (parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    var target = parts[1];
                    var host = target;
                    var port = 443;
                    var colon = target.LastIndexOf(':');
                    if (colon > 0 && int.TryParse(target[(colon + 1)..], out var parsedPort))
                    {
                        host = target[..colon];
                        port = parsedPort;
                    }

                    if (port != 443 || !IsAllowed(host))
                    {
                        await WriteResponseAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", token);
                        return;
                    }

                    await TunnelAsync(client, stream, host, port, token);
                    return;
                }

                if (parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Uri.TryCreate(parts[1], UriKind.Absolute, out var uri) ||
                        !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                        !IsAllowed(uri.Host))
                    {
                        await WriteResponseAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", token);
                        return;
                    }

                    await ForwardHttpAsync(stream, uri, text, token);
                }
            }
            catch { }
        }
    }

    private async Task TunnelAsync(TcpClient client, NetworkStream clientStream, string host, int port, CancellationToken token)
    {
        using var upstream = new TcpClient();
        await upstream.ConnectAsync(host, port, token);
        using var upstreamStream = upstream.GetStream();

        await WriteResponseAsync(clientStream, "HTTP/1.1 200 Connection Established\r\nProxy-Agent: SiteShield\r\n\r\n", token);
        var a = clientStream.CopyToAsync(upstreamStream, token);
        var b = upstreamStream.CopyToAsync(clientStream, token);
        await Task.WhenAny(a, b);
    }

    private async Task ForwardHttpAsync(NetworkStream clientStream, Uri uri, string originalHeader, CancellationToken token)
    {
        using var upstream = new TcpClient();
        await upstream.ConnectAsync(uri.Host, 80, token);
        using var upstreamStream = upstream.GetStream();

        var lines = originalHeader.Split("\r\n", StringSplitOptions.None);
        var output = new StringBuilder();
        var first = lines[0].Split(' ', 3);
        first[1] = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        output.Append(first[0]).Append(' ').Append(first[1]).Append(' ').Append(first.Length > 2 ? first[2] : "HTTP/1.1").Append("\r\n");
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) break;
            if (lines[i].StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase)) continue;
            output.Append(lines[i]).Append("\r\n");
        }
        output.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(output.ToString());
        await upstreamStream.WriteAsync(bytes, token);
        await upstreamStream.CopyToAsync(clientStream, token);
    }

    private static async Task<byte[]> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[2048];
        while (buffer.Length < 64 * 1024)
        {
            var read = await stream.ReadAsync(chunk, token);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
            if (buffer.Length >= 4)
            {
                var data = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
                for (var i = 3; i < data.Length; i++)
                {
                    if (data[i - 3] == 13 && data[i - 2] == 10 && data[i - 1] == 13 && data[i] == 10)
                        return buffer.ToArray();
                }
            }
        }
        return buffer.ToArray();
    }

    private static Task WriteResponseAsync(NetworkStream stream, string response, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(response), token).AsTask();

    private bool IsAllowed(string host)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        lock (_gate)
        {
            return _allowed.Contains(host) || _allowed.Any(x => host.EndsWith("." + x, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static HashSet<string> NormalizeDomains(IEnumerable<string> domains) =>
        domains.Select(NormalizeDomain).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeDomain(string domain)
    {
        var value = domain.Trim().ToLowerInvariant();
        value = value.Replace("https://", string.Empty).Replace("http://", string.Empty);
        value = value.Split('/')[0].Trim('.');
        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) value = value[4..];
        return value;
    }

    public void Dispose() => Stop();
}
