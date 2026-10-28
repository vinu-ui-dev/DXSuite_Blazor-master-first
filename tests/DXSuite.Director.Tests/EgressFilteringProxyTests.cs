using System.Net;
using System.Net.Sockets;
using System.Text;
using DXSuite.Director.Models;
using DXSuite.Director.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

/// <summary>
/// The capture browser resolves and fetches inside its own network stack, so these tests drive the
/// proxy over real loopback sockets the way Chromium would: absolute-form requests for http://,
/// CONNECT for https://. Each of those is an independent request through the filter, which is what
/// makes a page's subresources and its redirect hops enforceable rather than merely preflighted.
/// </summary>
public class EgressFilteringProxyTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static EgressFilteringProxy CreateProxy(params string[] allowList) =>
        new(new OutboundUrlGuard(
                Options.Create(new WebPreviewOptions { OutboundAllowList = allowList }),
                NullLogger<OutboundUrlGuard>.Instance,
                new StubTenantStore()),
            NullLogger<EgressFilteringProxy>.Instance);

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/iam/security-credentials/")]
    [InlineData("http://10.0.0.5/admin")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[fd00::1]/")]
    public async Task Blocks_subresource_load_of_an_internal_address(string url)
    {
        await using var proxy = CreateProxy();
        var endpoint = await proxy.EnsureStartedAsync();
        Assert.NotNull(endpoint);

        var response = await SendRequestAsync(endpoint, $"GET {url} HTTP/1.1\r\nHost: {new Uri(url).Authority}\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403", response);
    }

    [Fact]
    public async Task Blocks_a_hostname_that_resolves_into_a_blocked_range()
    {
        await using var proxy = CreateProxy();
        var endpoint = await proxy.EnsureStartedAsync();
        Assert.NotNull(endpoint);

        var response = await SendRequestAsync(endpoint, "GET http://localhost:9/ HTTP/1.1\r\nHost: localhost:9\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403", response);
    }

    [Fact]
    public async Task Blocks_loopback_even_though_the_filter_itself_listens_on_loopback()
    {
        await using var proxy = CreateProxy();
        var endpoint = await proxy.EnsureStartedAsync();
        Assert.NotNull(endpoint);

        // The browser is launched with --proxy-bypass-list=<-loopback>, so loopback targets reach
        // the filter rather than being dialled directly, and must be refused here
        var response = await SendRequestAsync(endpoint,
            $"GET http://127.0.0.1:{endpoint.Port}/ HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403", response);
    }

    [Fact]
    public async Task Blocks_a_connect_tunnel_to_an_internal_address()
    {
        await using var proxy = CreateProxy();
        var endpoint = await proxy.EnsureStartedAsync();
        Assert.NotNull(endpoint);

        var response = await SendRequestAsync(endpoint, "CONNECT 169.254.169.254:443 HTTP/1.1\r\nHost: 169.254.169.254:443\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403", response);
    }

    /// <summary>
    /// A redirect is followed by the browser, not by the proxy, so it arrives as a second request.
    /// The permitted origin's 302 relays untouched; the hop it points at is judged on its own.
    /// </summary>
    [Fact]
    public async Task Blocks_a_redirect_hop_out_of_a_permitted_origin_into_an_internal_address()
    {
        await using var origin = LoopbackOrigin.Responding(
            "HTTP/1.1 302 Found\r\nLocation: http://169.254.169.254/latest/meta-data/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var proxy = CreateProxy("127.0.0.0/8");
        var endpoint = await proxy.EnsureStartedAsync();
        Assert.NotNull(endpoint);

        var first = await SendRequestAsync(endpoint,
            $"GET http://127.0.0.1:{origin.Port}/page HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 302", first);
        Assert.Contains("Location: http://169.254.169.254/latest/meta-data/", first);

        var followed = await SendRequestAsync(endpoint,
            "GET http://169.254.169.254/latest/meta-data/ HTTP/1.1\r\nHost: 169.254.169.254\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 403", followed);
    }

    [Fact]
    public async Task Forwards_a_permitted_request_as_origin_form_and_relays_the_response()
    {
        await using var origin = LoopbackOrigin.Responding(
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello");
        await using var proxy = CreateProxy("127.0.0.0/8");
        var endpoint = await proxy.EnsureStartedAsync();
        Assert.NotNull(endpoint);

        var response = await SendRequestAsync(endpoint,
            $"GET http://127.0.0.1:{origin.Port}/page?q=1 HTTP/1.1\r\n" +
            $"Host: 127.0.0.1:{origin.Port}\r\nProxy-Connection: keep-alive\r\nAccept: text/html\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 200", response);
        Assert.EndsWith("hello", response);

        var forwarded = origin.LastRequest!;
        Assert.StartsWith("GET /page?q=1 HTTP/1.1\r\n", forwarded);
        Assert.Contains("Accept: text/html\r\n", forwarded);
        Assert.DoesNotContain("Proxy-Connection", forwarded);
    }

    [Fact]
    public async Task Tunnels_connect_to_a_permitted_origin()
    {
        await using var origin = LoopbackOrigin.Echoing();
        await using var proxy = CreateProxy("127.0.0.0/8");
        var endpoint = await proxy.EnsureStartedAsync();
        Assert.NotNull(endpoint);

        using var cts = new CancellationTokenSource(TestTimeout);
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint, cts.Token);
        await using var stream = client.GetStream();

        await WriteAsync(stream, $"CONNECT 127.0.0.1:{origin.Port} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n", cts.Token);
        var established = await ReadAsync(stream, cts.Token);
        Assert.StartsWith("HTTP/1.1 200", established);

        // The tunnel is opaque: bytes written after the 200 reach the origin untouched
        await WriteAsync(stream, "ping", cts.Token);
        Assert.Equal("ping", await ReadAsync(stream, cts.Token));
    }

    [Fact]
    public async Task Restarting_is_idempotent_and_keeps_the_same_endpoint()
    {
        await using var proxy = CreateProxy();
        var first = await proxy.EnsureStartedAsync();
        var second = await proxy.EnsureStartedAsync();

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    private static async Task<string> SendRequestAsync(IPEndPoint proxy, string request)
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        using var client = new TcpClient();
        await client.ConnectAsync(proxy, cts.Token);
        await using var stream = client.GetStream();

        await WriteAsync(stream, request, cts.Token);
        return await ReadToEndAsync(stream, cts.Token);
    }

    private static Task WriteAsync(Stream stream, string text, CancellationToken ct) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text), ct).AsTask();

    private static async Task<string> ReadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer, ct);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    private static async Task<string> ReadToEndAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Stand-in for the page's real origin. It has to be a live socket rather than a stub because
    /// the proxy's job is byte relay over a connection the guard chose to permit.
    /// </summary>
    private sealed class LoopbackOrigin : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>The request head the proxy actually forwarded, for asserting the rewrite.</summary>
        public string? LastRequest { get; private set; }

        private LoopbackOrigin(Func<LoopbackOrigin, NetworkStream, CancellationToken, Task> handler)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptSocketAsync(_cts.Token);
                        await using var stream = new NetworkStream(client, ownsSocket: false);
                        await handler(this, stream, _cts.Token);
                    }
                }
                catch
                {
                    // Listener torn down with the test
                }
            });
        }

        public static LoopbackOrigin Responding(string response) => new(async (origin, stream, ct) =>
        {
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer, ct);
            origin.LastRequest = Encoding.ASCII.GetString(buffer, 0, read);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
        });

        public static LoopbackOrigin Echoing() => new(async (_, stream, ct) =>
        {
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0) return;
                await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        });

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Dispose();
            _cts.Dispose();
        }
    }
}
