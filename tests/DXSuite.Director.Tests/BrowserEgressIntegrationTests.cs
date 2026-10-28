using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DXSuite.Director.Models;
using DXSuite.Director.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

/// <summary>
/// Launches the real capture browser, because the property that matters here is Chromium's, not
/// ours: by default it bypasses the proxy for loopback and link-local targets, which would hand a
/// page direct access to exactly the addresses the filter exists to deny.
/// <c>--proxy-bypass-list=&lt;-loopback&gt;</c> turns that off, and nothing in a socket-level test
/// can tell whether that flag still works after a Chromium upgrade.
///
/// Skipped (as a vacuous pass) where no installed browser can capture at all — a host that cannot
/// run the feature must not fail its test. Candidates are tried in turn rather than taking the
/// first that exists, because an install can be present and still headless-disabled by policy.
/// </summary>
public class BrowserEgressIntegrationTests
{
    private static readonly string[] BrowserCandidatePaths =
    {
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    };

    [Fact]
    public async Task Browser_routes_page_subresources_through_the_egress_filter()
    {
        // The internal host the page reaches for over loopback. It stays at zero hits only if
        // Chromium hands the load to the proxy instead of dialling it directly.
        using var internalHost = new LoopbackOrigin(_ =>
            "HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using var page = new LoopbackOrigin(_ => HtmlResponse(
            "<html><body><h1>capture</h1>" +
            $"<img src=\"http://127.0.0.1:{internalHost.Port}/leak.png\">" +
            "<img src=\"http://169.254.169.254/latest/meta-data/\">" +
            "<iframe src=\"http://10.0.0.5/admin\"></iframe>" +
            "</body></html>"));

        // Host allow-list rather than CIDR: "localhost" serves the page while the 127.0.0.1
        // literal it embeds stays blocked, so a proxied loopback load is distinguishable from a
        // bypassed one. A CIDR entry would permit both and prove nothing.
        var guard = new OutboundUrlGuard(
            Options.Create(new WebPreviewOptions { OutboundAllowList = ["localhost"] }),
            NullLogger<OutboundUrlGuard>.Instance,
            new StubTenantStore());
        var proxyLog = new RecordingLogger();
        await using var proxy = new EgressFilteringProxy(guard, proxyLog);

        var endpoint = await proxy.EnsureStartedAsync();
        Assert.NotNull(endpoint);

        var captured = false;
        foreach (var browserPath in BrowserCandidatePaths.Where(File.Exists))
        {
            captured = await TryCaptureAsync(browserPath, endpoint, page.Port);
            if (captured) break;
        }

        if (!captured) return;

        var blocked = string.Join("\n", proxyLog.Messages);
        Assert.True(page.Hits > 0, $"the browser captured without loading the page; filter log:\n{blocked}");

        // Removing --proxy-bypass-list=<-loopback> makes exactly this assertion fail
        Assert.Equal(0, internalHost.Hits);

        Assert.Contains("127.0.0.1", blocked);
        Assert.Contains("169.254.169.254", blocked);
        Assert.Contains("10.0.0.5", blocked);
    }

    /// <summary>Returns whether this browser rendered the page into a capture.</summary>
    private static async Task<bool> TryCaptureAsync(string browserPath, IPEndPoint proxy, int pagePort)
    {
        var screenshotPath = Path.Combine(Path.GetTempPath(), $"dx-egress-test-{Guid.NewGuid():N}.png");
        var profileDir = Path.Combine(Path.GetTempPath(), $"dx-egress-test-profile-{Guid.NewGuid():N}");
        try
        {
            await RunBrowserAsync(browserPath, proxy, pagePort, screenshotPath, profileDir);
            return File.Exists(screenshotPath);
        }
        finally
        {
            try
            {
                if (File.Exists(screenshotPath)) File.Delete(screenshotPath);
                if (Directory.Exists(profileDir)) Directory.Delete(profileDir, recursive: true);
            }
            catch
            {
                // Best effort; stray temp files are harmless
            }
        }
    }

    /// <summary>Mirrors the argument list WebPreviewService.TryCaptureWithBrowserAsync builds.</summary>
    private static async Task RunBrowserAsync(string browserPath, IPEndPoint proxy, int pagePort, string screenshotPath, string profileDir)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = browserPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
        {
            "--headless", "--disable-gpu", "--no-sandbox", "--hide-scrollbars", "--mute-audio",
            "--no-first-run", "--disable-extensions",
            $"--proxy-server=http://{proxy}",
            "--proxy-bypass-list=<-loopback>",
            "--force-webrtc-ip-handling-policy=disable_non_proxied_udp",
            "--disable-background-networking",
            "--ignore-certificate-errors",
            $"--user-data-dir={profileDir}",
            "--window-size=1280,720",
            "--virtual-time-budget=10000",
            $"--screenshot={screenshotPath}",
            $"http://localhost:{pagePort}/page"
        })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static string HtmlResponse(string html) =>
        $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {html.Length}\r\nConnection: close\r\n\r\n{html}";

    private sealed class RecordingLogger : ILogger<EgressFilteringProxy>
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages
        {
            get { lock (_messages) return _messages.ToArray(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages) _messages.Add(formatter(state, exception));
        }
    }

    /// <summary>
    /// Minimal origin server. Counts requests so a test can assert an address was never reached,
    /// which is the only way to distinguish "the proxy refused it" from "the browser skipped it".
    /// </summary>
    private sealed class LoopbackOrigin : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private int _hits;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public int Hits => Volatile.Read(ref _hits);

        public LoopbackOrigin(Func<string, string> respond)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptSocketAsync(_cts.Token);
                        _ = Task.Run(() => ServeAsync(client, respond), _cts.Token);
                    }
                }
                catch
                {
                    // Listener torn down with the test
                }
            });
        }

        private async Task ServeAsync(Socket client, Func<string, string> respond)
        {
            try
            {
                using (client)
                await using (var stream = new NetworkStream(client, ownsSocket: false))
                {
                    var buffer = new byte[8192];
                    var read = await stream.ReadAsync(buffer, _cts.Token);
                    Interlocked.Increment(ref _hits);
                    var request = Encoding.ASCII.GetString(buffer, 0, read);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(respond(request)), _cts.Token);
                }
            }
            catch
            {
                // A peer disconnecting mid-exchange is not a test failure
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Dispose();
            _cts.Dispose();
        }
    }
}
