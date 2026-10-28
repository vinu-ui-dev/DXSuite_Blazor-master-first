using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DXSuite.Director.Services;

/// <summary>
/// Loopback HTTP proxy that applies <see cref="OutboundUrlGuard"/> to every connection the
/// headless capture browser opens. Chromium fetches a page's subresources and follows its
/// redirects inside its own network stack, so nothing we do to the top-level URL constrains where
/// the render actually reads from; routing the browser through a proxy we control is what makes
/// each of those loads a request we can refuse. Because the rendered result is handed back to the
/// tenant admin as a thumbnail, an unfiltered load of an internal endpoint is a read-SSRF with
/// exfiltration, which is why capture stays disabled unless this proxy is running.
///
/// Both request forms Chromium uses are handled: absolute-form for http:// and CONNECT for
/// https:// and wss://. CONNECT is tunnelled rather than intercepted — the guard's decision is
/// about which address may be dialled, so there is no reason to terminate TLS and every reason
/// not to.
///
/// Singleton, started on first capture and shared afterwards. It listens on 127.0.0.1 only; any
/// local process can therefore reach it, but it grants nothing a local process does not already
/// have, since it forwards only to addresses that process could dial directly.
/// </summary>
public sealed class EgressFilteringProxy : IAsyncDisposable
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int RelayBufferBytes = 32 * 1024;

    // Chromium caps itself at 32 sockets per proxy server, so this leaves headroom for a full
    // capture while still bounding what a page that stalls its connections can hold open.
    private const int MaxConcurrentConnections = 64;

    // Backstop for a tunnel whose peer never closes; the capture itself is bounded separately by
    // WebPreview:CaptureTimeoutSeconds.
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

    private static readonly string[] HopByHopHeaders =
    {
        "connection", "proxy-connection", "proxy-authenticate", "proxy-authorization",
        "keep-alive", "te", "trailer", "upgrade"
    };

    private readonly OutboundUrlGuard _guard;
    private readonly ILogger<EgressFilteringProxy> _logger;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly SemaphoreSlim _connectionSlots = new(MaxConcurrentConnections, MaxConcurrentConnections);

    private TcpListener? _listener;
    private CancellationTokenSource? _shutdown;
    private Task? _acceptLoop;
    private IPEndPoint? _endpoint;

    public EgressFilteringProxy(OutboundUrlGuard guard, ILogger<EgressFilteringProxy> logger)
    {
        _guard = guard;
        _logger = logger;
    }

    /// <summary>
    /// Starts the listener if it isn't already running and returns its loopback endpoint, or null
    /// if it could not be started. A null return must fail the capture closed: without the proxy
    /// the browser's loads are unfiltered.
    /// </summary>
    public async Task<IPEndPoint?> EnsureStartedAsync(CancellationToken ct = default)
    {
        if (_endpoint is { } running) return running;

        await _startLock.WaitAsync(ct);
        try
        {
            if (_endpoint is { } started) return started;

            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                _logger.LogError(ex, "Could not start the web-preview egress filter; headless capture is unavailable");
                listener.Dispose();
                return null;
            }

            _shutdown = new CancellationTokenSource();
            _listener = listener;
            _endpoint = (IPEndPoint)listener.LocalEndpoint;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _shutdown.Token), CancellationToken.None);

            _logger.LogInformation("Web-preview egress filter listening on {Endpoint}", _endpoint);
            return _endpoint;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await listener.AcceptSocketAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug(ex, "Egress filter accept failed");
                continue;
            }

            _ = HandleClientAsync(client, ct);
        }
    }

    /// <summary>
    /// Runs detached from the accept loop, so everything it touches — including acquiring the
    /// concurrency slot — has to be inside the guarded region: an escaping exception here would
    /// surface as an unhandled thread-pool exception rather than a failed capture.
    /// </summary>
    private async Task HandleClientAsync(Socket client, CancellationToken ct)
    {
        var slotHeld = false;
        try
        {
            slotHeld = await _connectionSlots.WaitAsync(TimeSpan.Zero, ct);
            if (!slotHeld)
            {
                _logger.LogWarning("Egress filter refused a connection: {Limit} concurrent connections already open", MaxConcurrentConnections);
                return;
            }

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lifetime.CancelAfter(ConnectionLifetime);

            client.NoDelay = true;
            await using var stream = new NetworkStream(client, ownsSocket: false);
            await ServeAsync(stream, lifetime.Token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Egress filter connection ended in error");
        }
        finally
        {
            if (slotHeld) _connectionSlots.Release();
            client.Dispose();
        }
    }

    /// <summary>
    /// Serves one request per connection. Keep-alive is deliberately not offered: tracking where
    /// each response ends would mean parsing content-length and chunked framing from an untrusted
    /// origin, and a capture is short-lived enough that the extra connections cost nothing.
    /// </summary>
    private async Task ServeAsync(Stream client, CancellationToken ct)
    {
        var (header, leftover) = await ReadHeaderAsync(client, ct);
        if (header == null)
        {
            await WriteStatusAsync(client, "400 Bad Request", ct);
            return;
        }

        var lines = header.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 3)
        {
            await WriteStatusAsync(client, "400 Bad Request", ct);
            return;
        }

        var method = requestLine[0];
        var target = requestLine[1];

        if (string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase))
            await TunnelAsync(client, target, ct);
        else
            await ForwardAsync(client, method, target, lines, leftover, ct);
    }

    private async Task TunnelAsync(Stream client, string authority, CancellationToken ct)
    {
        // CONNECT carries a bare authority, so wrap it in a URI to reuse the host/port parsing
        // (including bracketed IPv6 literals) rather than splitting on the last colon by hand.
        if (!Uri.TryCreate($"https://{authority}", UriKind.Absolute, out var uri) || uri.PathAndQuery != "/")
        {
            await WriteStatusAsync(client, "400 Bad Request", ct);
            return;
        }

        var upstream = await DialOrRejectAsync(client, uri.IdnHost, uri.Port, ct);
        if (upstream == null) return;

        await using var upstreamStream = new NetworkStream(upstream, ownsSocket: true);
        await WriteAsciiAsync(client, "HTTP/1.1 200 Connection established\r\n\r\n", ct);
        await RelayAsync(client, upstreamStream, ct);
    }

    private async Task ForwardAsync(Stream client, string method, string target, string[] headerLines, byte[] leftover, CancellationToken ct)
    {
        // Chromium sends absolute-form only for http://; https:// arrives as CONNECT. Anything
        // else (an origin-form path, ws://, ftp://) is not something the capture needs.
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
        {
            await WriteStatusAsync(client, "400 Bad Request", ct);
            return;
        }

        var upstream = await DialOrRejectAsync(client, uri.IdnHost, uri.Port, ct);
        if (upstream == null) return;

        await using var upstreamStream = new NetworkStream(upstream, ownsSocket: true);
        await upstreamStream.WriteAsync(BuildOriginFormRequest(method, uri, headerLines), ct);
        if (leftover.Length > 0) await upstreamStream.WriteAsync(leftover, ct);
        await RelayAsync(client, upstreamStream, ct);
    }

    /// <summary>
    /// Dials through the guard, answering the client itself and returning null when the target is
    /// refused. A blocked address is reported as 403 so a page cannot tell it apart from an origin
    /// that simply declined the request.
    /// </summary>
    private async Task<Socket?> DialOrRejectAsync(Stream client, string host, int port, CancellationToken ct)
    {
        try
        {
            return await _guard.DialAsync(host, port, ct);
        }
        catch (OutboundBlockedException)
        {
            _logger.LogWarning("Egress filter blocked a capture request to {Host}:{Port}", host, port);
            await WriteStatusAsync(client, "403 Forbidden", ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Egress filter could not reach {Host}:{Port}", host, port);
            await WriteStatusAsync(client, "502 Bad Gateway", ct);
            return null;
        }
    }

    /// <summary>
    /// Rewrites the proxy's absolute-form request into the origin-form the upstream server
    /// expects, dropping hop-by-hop headers and pinning the connection to a single exchange.
    /// </summary>
    private static byte[] BuildOriginFormRequest(string method, Uri uri, string[] headerLines)
    {
        var request = new StringBuilder()
            .Append(method).Append(' ').Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");

        var hasHost = false;
        foreach (var line in headerLines.Skip(1))
        {
            if (line.Length == 0) continue;

            // Obsolete line folding: a leading space continues the previous header's value
            if (line[0] is ' ' or '\t')
            {
                request.Append(line).Append("\r\n");
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var name = line[..colon];
            if (HopByHopHeaders.Contains(name.ToLowerInvariant())) continue;
            if (name.Equals("host", StringComparison.OrdinalIgnoreCase)) hasHost = true;

            request.Append(line).Append("\r\n");
        }

        if (!hasHost) request.Append("Host: ").Append(uri.Authority).Append("\r\n");

        request.Append("Connection: close\r\n\r\n");
        return Encoding.Latin1.GetBytes(request.ToString());
    }

    /// <summary>
    /// Reads the request head, returning it without its terminating blank line plus any bytes of
    /// the body that arrived in the same read.
    /// </summary>
    private static async Task<(string? Header, byte[] Leftover)> ReadHeaderAsync(Stream client, CancellationToken ct)
    {
        var buffer = new byte[MaxHeaderBytes];
        var filled = 0;

        while (filled < buffer.Length)
        {
            var read = await client.ReadAsync(buffer.AsMemory(filled), ct);
            if (read == 0) break;
            filled += read;

            var end = buffer.AsSpan(0, filled).IndexOf("\r\n\r\n"u8);
            if (end < 0) continue;

            return (Encoding.Latin1.GetString(buffer, 0, end),
                    buffer.AsSpan(end + 4, filled - end - 4).ToArray());
        }

        return (null, []);
    }

    /// <summary>
    /// Pumps both directions until either end finishes. The client half only completes when the
    /// browser closes the connection, so in practice the upstream half ending — the response is
    /// complete — is what tears the pair down.
    /// </summary>
    private static async Task RelayAsync(Stream client, Stream upstream, CancellationToken ct)
    {
        using var done = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var toUpstream = PumpAsync(client, upstream, done.Token);
        var toClient = PumpAsync(upstream, client, done.Token);

        await Task.WhenAny(toUpstream, toClient);
        await done.CancelAsync();
        await Task.WhenAll(toUpstream, toClient);
    }

    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(RelayBufferBytes);
        try
        {
            while (true)
            {
                var read = await from.ReadAsync(buffer, ct);
                if (read == 0) return;
                await to.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        catch
        {
            // Either peer closing mid-stream ends the exchange; the capture falls back on its own
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Task WriteStatusAsync(Stream client, string status, CancellationToken ct) =>
        WriteAsciiAsync(client, $"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", ct);

    private static async Task WriteAsciiAsync(Stream client, string text, CancellationToken ct)
    {
        try
        {
            await client.WriteAsync(Encoding.ASCII.GetBytes(text), ct);
        }
        catch
        {
            // The browser may already have given up; nothing further to report to it
        }
    }

    /// <summary>
    /// Stops accepting and lets in-flight connections unwind on the cancelled token. The shutdown
    /// source and the two semaphores are deliberately left undisposed: connection handlers run
    /// detached and can still be releasing a slot after this returns, and disposing shared
    /// primitives out from under them turns a recycle into an unhandled thread-pool exception.
    /// Neither semaphore exposes a wait handle, so neither needs disposal to release resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_shutdown == null) return;

        await _shutdown.CancelAsync();
        _listener?.Dispose();

        if (_acceptLoop != null)
        {
            try
            {
                await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Shutdown proceeds whether or not the loop acknowledged the cancellation
            }
        }
    }
}
