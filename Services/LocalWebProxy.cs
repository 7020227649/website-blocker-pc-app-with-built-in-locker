using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SiteShield.Services;

public sealed class LocalWebProxy : IDisposable
{
    public const int Port = 8888;
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 8 * 1024 * 1024;

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
        using (var stream = client.GetStream())
        {
            stream.ReadTimeout = 10000;
            stream.WriteTimeout = 10000;

            try
            {
                var request = await ReadHeaderAsync(stream, token);
                if (request.Header.Length == 0) return;

                var text = Encoding.ASCII.GetString(request.Header);
                var lines = text.Split("\r\n", StringSplitOptions.None);
                if (lines.Length == 0) return;

                var requestLine = lines[0];
                var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return;

                if (parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseConnectTarget(parts[1], out var host, out var port) ||
                        port != 443 ||
                        !IsAllowed(host))
                    {
                        await WriteResponseAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", token);
                        return;
                    }

                    await TunnelAsync(client, stream, host, port, request.ExtraBytes, token);
                    return;
                }

                if (!IsSupportedHttpMethod(parts[0])) return;

                if (!Uri.TryCreate(parts[1], UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                    !IsAllowed(uri.Host))
                {
                    await WriteResponseAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", token);
                    return;
                }

                await ForwardHttpAsync(stream, uri, lines, request.ExtraBytes, token);
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch { }
        }
    }

    private async Task TunnelAsync(
        TcpClient client,
        NetworkStream clientStream,
        string host,
        int port,
        byte[] initialBytes,
        CancellationToken token)
    {
        using var upstream = new TcpClient();
        await upstream.ConnectAsync(host, port, token);
        using var upstreamStream = upstream.GetStream();

        await WriteResponseAsync(clientStream, "HTTP/1.1 200 Connection Established\r\nProxy-Agent: SiteShield\r\n\r\n", token);

        if (initialBytes.Length > 0)
            await upstreamStream.WriteAsync(initialBytes, token);

        var clientToServer = clientStream.CopyToAsync(upstreamStream, token);
        var serverToClient = upstreamStream.CopyToAsync(clientStream, token);
        await Task.WhenAny(clientToServer, serverToClient);
    }

    private async Task ForwardHttpAsync(
        NetworkStream clientStream,
        Uri uri,
        string[] lines,
        byte[] initialBodyBytes,
        CancellationToken token)
    {
        var contentLength = GetContentLength(lines);
        if (contentLength < 0 || contentLength > MaxBodyBytes)
        {
            await WriteResponseAsync(clientStream, "HTTP/1.1 413 Payload Too Large\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", token);
            return;
        }

        using var upstream = new TcpClient();
        await upstream.ConnectAsync(uri.Host, uri.Port > 0 ? uri.Port : 80, token);
        using var upstreamStream = upstream.GetStream();

        var output = new StringBuilder();
        var first = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (first.Length < 2) return;

        first[1] = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
        output.Append(first[0])
            .Append(' ')
            .Append(first[1])
            .Append(' ')
            .Append(first.Length > 2 ? first[2] : "HTTP/1.1")
            .Append("\r\n");

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) break;
            if (lines[i].StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase)) continue;
            if (lines[i].StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase)) continue;
            output.Append(lines[i]).Append("\r\n");
        }

        output.Append("Connection: close\r\n\r\n");
        await upstreamStream.WriteAsync(Encoding.ASCII.GetBytes(output.ToString()), token);

        var alreadyRead = Math.Min(initialBodyBytes.Length, contentLength);
        if (alreadyRead > 0)
            await upstreamStream.WriteAsync(initialBodyBytes.AsMemory(0, alreadyRead), token);

        var remaining = contentLength - alreadyRead;
        if (remaining > 0)
        {
            var body = await ReadExactlyAsync(clientStream, remaining, token);
            await upstreamStream.WriteAsync(body, token);
        }

        await upstreamStream.CopyToAsync(clientStream, token);
    }

    private static int GetContentLength(string[] lines)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line["Content-Length:".Length..].Trim();
            return int.TryParse(value, out var length) && length >= 0 ? length : -1;
        }
        return 0;
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length, CancellationToken token)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), token);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return buffer;
    }

    private static bool TryParseConnectTarget(string target, out string host, out int port)
    {
        host = string.Empty;
        port = 443;

        var close = target.IndexOf(']');
        if (target.StartsWith("[", StringComparison.Ordinal) && close > 0)
        {
            host = target[1..close];
            if (close + 1 < target.Length && target[close + 1] == ':')
                return int.TryParse(target[(close + 2)..], out port);
            return true;
        }

        var colon = target.LastIndexOf(':');
        if (colon > 0 && int.TryParse(target[(colon + 1)..], out var parsedPort))
        {
            host = target[..colon];
            port = parsedPort;
            return true;
        }

        host = target;
        return host.Length > 0;
    }

    private static bool IsSupportedHttpMethod(string method) =>
        method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase);

    private static async Task<(byte[] Header, byte[] ExtraBytes)> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[2048];
        while (buffer.Length < MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(chunk, token);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);

            var data = buffer.ToArray();
            for (var i = 3; i < data.Length; i++)
            {
                if (data[i - 3] != 13 || data[i - 2] != 10 || data[i - 1] != 13 || data[i] != 10)
                    continue;

                var headerLength = i + 1;
                var header = new byte[headerLength];
                Buffer.BlockCopy(data, 0, header, 0, headerLength);
                var extraLength = data.Length - headerLength;
                var extra = new byte[extraLength];
                if (extraLength > 0) Buffer.BlockCopy(data, headerLength, extra, 0, extraLength);
                return (header, extra);
            }
        }

        return (buffer.ToArray(), Array.Empty<byte>());
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
