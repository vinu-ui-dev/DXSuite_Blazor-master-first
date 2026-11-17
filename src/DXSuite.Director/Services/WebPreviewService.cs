using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using DXSuite.Director.Models;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// Generates preview thumbnails for web assets, stored per tenant as
/// App_Data/WebThumbnails/{tenantSubdomain}/{assetId}.png — outside wwwroot so the anonymous
/// static-files middleware can never serve them; WebThumbnailController is the only read path.
/// Scoped service: paths are keyed by the request's resolved <see cref="ITenantContext"/>.
/// Strategy: full-page headless-browser capture when the WebPreview:CaptureEnabled feature is on
/// (system Edge or Chrome by default), falling back to the page's og:image or largest declared
/// icon fetched over plain HTTP. All outbound fetches pass <see cref="OutboundUrlGuard"/> — the
/// metadata path through its ConnectCallback, the browser through <see cref="EgressFilteringProxy"/>.
/// All output is normalized through <see cref="ThumbnailService"/>.
/// </summary>
public class WebPreviewService
{
    /// <summary>Named HttpClient whose connections are validated by <see cref="OutboundUrlGuard"/>.</summary>
    public const string OutboundHttpClientName = "WebPreviewOutbound";

    // Server installs don't reliably have Edge (e.g. IIS boxes provisioned with Chrome only),
    // so auto-detection tries both before requiring an explicit WebPreview:BrowserPath.
    private static readonly string[] BrowserCandidatePaths =
    {
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    };

    private const int MaxHtmlBytes = 2 * 1024 * 1024;

    private readonly WebPreviewOptions _options;
    private readonly ThumbnailService _thumbnailService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ITenantContext _tenant;
    private readonly OutboundUrlGuard _urlGuard;
    private readonly EgressFilteringProxy _egressProxy;
    private readonly ILogger<WebPreviewService> _logger;

    public WebPreviewService(
        IOptions<WebPreviewOptions> options,
        ThumbnailService thumbnailService,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment env,
        ITenantContext tenant,
        OutboundUrlGuard urlGuard,
        EgressFilteringProxy egressProxy,
        ILogger<WebPreviewService> logger)
    {
        _options = options.Value;
        _thumbnailService = thumbnailService;
        _httpClientFactory = httpClientFactory;
        _env = env;
        _tenant = tenant;
        _urlGuard = urlGuard;
        _egressProxy = egressProxy;
        _logger = logger;
    }

    public string GetThumbnailLocalPath(int assetId) =>
        Path.Combine(_env.ContentRootPath, "App_Data", "WebThumbnails", SafeTenantSegment(_tenant.Subdomain), $"{assetId}.png");

    /// <summary>
    /// Tenant folder name. Subdomains come from provisioning (config key or platform DB), never
    /// from the request, but this keeps tenant isolation a property of the code rather than of
    /// provisioning hygiene: separators can't escape the tree, and case-folding stops two records
    /// differing only in case from silently sharing a folder on a case-insensitive filesystem.
    /// </summary>
    private static string SafeTenantSegment(string subdomain)
    {
        var normalized = subdomain.ToLowerInvariant();
        if (normalized.Length == 0 || normalized is "." or ".." ||
            normalized.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')))
        {
            throw new InvalidOperationException(
                $"Tenant subdomain '{subdomain}' is not usable as a storage folder name.");
        }
        return normalized;
    }

    /// <summary>
    /// Deletes the pre-tenant-scoping thumbnail folder. It lived under wwwroot where the static
    /// middleware served it without authentication, and all tenants shared one {assetId} keyspace,
    /// so surviving files may hold another tenant's captures. Thumbnails are a regenerable cache —
    /// the startup backfill recreates them in the tenant-scoped location. Called during startup
    /// configuration so the folder is gone before the server accepts its first request.
    /// </summary>
    public static void RemoveLegacyThumbnails(IWebHostEnvironment env, ILogger logger)
    {
        var legacyRoot = Path.Combine(env.WebRootPath, "WebThumbnails");
        try
        {
            if (Directory.Exists(legacyRoot))
            {
                Directory.Delete(legacyRoot, recursive: true);
                logger.LogInformation("Removed legacy web thumbnail folder {Path}", legacyRoot);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove legacy web thumbnail folder {Path}", legacyRoot);
        }
    }

    /// <summary>
    /// Stable URL served by WebThumbnailController, which maps {assetId} into the caller's
    /// resolved tenant folder — the tenant is deliberately absent from the URL so one tenant
    /// can never address another's captures.
    /// </summary>
    public static string GetThumbnailWebPath(int assetId) => $"/WebThumbnails/{assetId}.png";

    /// <summary>
    /// Web path with a version query derived from the file's last write time, so browsers
    /// refetch after a regeneration instead of serving a cached copy of the stable URL.
    /// </summary>
    public string GetVersionedThumbnailWebPath(int assetId)
    {
        // Circuit scopes populate tenant identity before components render, but don't hard-fail
        // a render if it's unavailable — the unversioned URL still resolves via the controller.
        if (!_tenant.IsPopulated) return GetThumbnailWebPath(assetId);

        var localPath = GetThumbnailLocalPath(assetId);
        if (!File.Exists(localPath)) return GetThumbnailWebPath(assetId);
        return $"{GetThumbnailWebPath(assetId)}?v={File.GetLastWriteTimeUtc(localPath).Ticks}";
    }

    /// <summary>
    /// Removes a web asset's captured thumbnail, if one exists. Returns true only when a file was
    /// actually deleted. Captures are a regenerable cache, so a failure is logged and swallowed
    /// rather than failing whatever operation prompted the cleanup.
    /// </summary>
    public bool DeleteThumbnail(int assetId)
    {
        try
        {
            var localPath = GetThumbnailLocalPath(assetId);
            if (!File.Exists(localPath)) return false;

            File.Delete(localPath);
            _logger.LogInformation("Deleted web thumbnail for asset {AssetId}", assetId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete the web thumbnail for asset {AssetId}", assetId);
            return false;
        }
    }

    /// <summary>
    /// Generates (or regenerates) the thumbnail for a web asset. Returns true when a thumbnail
    /// was written. Never throws; failures are logged and leave any previous thumbnail in place.
    /// targetWidth/targetHeight set the capture viewport shape (typically the asset's stored
    /// AssetWidth/AssetHeight, derived from the display it was created for); values &lt;= 0 fall
    /// back to the configured default viewport.
    /// </summary>
    public async Task<bool> GenerateWebThumbnailAsync(int assetId, string url, double targetWidth = 0, double targetHeight = 0)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;

            var (urlAllowed, hostResolverRule) = await _urlGuard.EvaluateForBrowserCaptureAsync(uri);
            if (!urlAllowed)
            {
                _logger.LogWarning(
                    "Web thumbnail generation blocked for asset {AssetId}: {Url} resolves to a disallowed address " +
                    "(see WebPreview:AllowPrivateNetworks / WebPreview:OutboundAllowList)", assetId, url);
                return false;
            }

            var outputPath = GetThumbnailLocalPath(assetId);

            if (_options.CaptureEnabled && await _urlGuard.IsBrowserCaptureAllowedAsync() &&
                await TryCaptureWithBrowserAsync(uri, outputPath, targetWidth, targetHeight, hostResolverRule))
                return true;

            return await TryCaptureFromMetadataAsync(uri, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web thumbnail generation failed for asset {AssetId} ({Url})", assetId, url);
            return false;
        }
    }

    /// <summary>
    /// Viewport matching the target shape, capped to a 1920px long edge so oversized display
    /// dimensions (e.g. video walls) don't inflate headless render cost.
    /// </summary>
    private (int Width, int Height) ResolveViewport(double targetWidth, double targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
            return (_options.ViewportWidth, _options.ViewportHeight);

        var scale = Math.Min(1.0, 1920.0 / Math.Max(targetWidth, targetHeight));
        return (Math.Max(1, (int)Math.Round(targetWidth * scale)),
                Math.Max(1, (int)Math.Round(targetHeight * scale)));
    }

    private async Task<bool> TryCaptureWithBrowserAsync(Uri uri, string outputPath, double targetWidth, double targetHeight, string? hostResolverRule)
    {
        var browserPath = _options.BrowserPath ?? BrowserCandidatePaths.FirstOrDefault(File.Exists);
        if (browserPath == null || !File.Exists(browserPath))
        {
            _logger.LogWarning("Web capture enabled but no browser found; set WebPreview:BrowserPath. Falling back to metadata fetch");
            return false;
        }

        // Nothing else constrains where the render reads from, so a browser we cannot point at the
        // egress filter must not be launched at all
        var egressEndpoint = await _egressProxy.EnsureStartedAsync();
        if (egressEndpoint == null)
        {
            _logger.LogError("Web capture skipped for {Url}: the egress filter is unavailable. Falling back to metadata fetch", uri);
            return false;
        }

        // Chrome's built-in screenshot mode writes the PNG and exits on its own. This avoids
        // driving the browser over a DevTools connection, whose startup handshake proved
        // unreliable under non-interactive service sessions (IIS app pools).
        var (viewportWidth, viewportHeight) = ResolveViewport(targetWidth, targetHeight);
        var capturePath = Path.Combine(Path.GetTempPath(), $"dx-webcapture-{Guid.NewGuid():N}.png");
        var profileDir = Path.Combine(Path.GetTempPath(), $"dx-webcapture-profile-{Guid.NewGuid():N}");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = browserPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--headless");
            startInfo.ArgumentList.Add("--disable-gpu");
            // Sandbox setup fails in non-interactive service sessions; URLs are admin-entered
            startInfo.ArgumentList.Add("--no-sandbox");
            startInfo.ArgumentList.Add("--hide-scrollbars");
            startInfo.ArgumentList.Add("--mute-audio");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--disable-extensions");
            // Every load — the page, its subresources, and each redirect hop — goes through the
            // filter, which is the only thing holding the render to the outbound address policy.
            startInfo.ArgumentList.Add($"--proxy-server=http://{egressEndpoint}");
            // Chromium bypasses proxies for loopback and link-local targets by default, which
            // would hand a page direct access to exactly the addresses the filter exists to deny
            startInfo.ArgumentList.Add("--proxy-bypass-list=<-loopback>");
            // WebRTC opens UDP sockets outside the proxy; a page could otherwise probe internal
            // hosts over data channels and render the answers into the thumbnail
            startInfo.ArgumentList.Add("--force-webrtc-ip-handling-policy=disable_non_proxied_udp");
            // Keeps component-updater and telemetry traffic off the filter
            startInfo.ArgumentList.Add("--disable-background-networking");
            // Internal signage hosts frequently use self-signed certificates
            startInfo.ArgumentList.Add("--ignore-certificate-errors");
            // Throwaway profile: the service account has no usable default profile, and
            // concurrent captures must not contend for the same one
            startInfo.ArgumentList.Add($"--user-data-dir={profileDir}");
            startInfo.ArgumentList.Add($"--window-size={viewportWidth},{viewportHeight}");
            // Fast-forwards timers so scripted pages finish their initial render before capture
            startInfo.ArgumentList.Add("--virtual-time-budget=10000");
            // Redundant while the proxy resolves on the browser's behalf, but it keeps the
            // top-level host pinned to its validated address for any load that reaches the
            // browser's own resolver
            if (hostResolverRule != null)
                startInfo.ArgumentList.Add($"--host-resolver-rules={hostResolverRule}");
            startInfo.ArgumentList.Add($"--screenshot={capturePath}");
            startInfo.ArgumentList.Add(uri.ToString());

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("Browser process failed to start for {Url}; falling back to metadata fetch", uri);
                return false;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.CaptureTimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                _logger.LogWarning("Browser capture timed out after {Seconds}s for {Url}; falling back to metadata fetch",
                    _options.CaptureTimeoutSeconds, uri);
                return false;
            }

            if (!File.Exists(capturePath))
            {
                _logger.LogWarning("Browser capture produced no image for {Url} (exit code {ExitCode}); falling back to metadata fetch",
                    uri, process.ExitCode);
                return false;
            }

            var screenshot = await File.ReadAllBytesAsync(capturePath);
            return _thumbnailService.GenerateFromImageBytes(screenshot, outputPath) != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Browser capture failed for {Url}; falling back to metadata fetch", uri);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(capturePath)) File.Delete(capturePath);
                if (Directory.Exists(profileDir)) Directory.Delete(profileDir, recursive: true);
            }
            catch
            {
                // Best effort; stray temp files are harmless
            }
        }
    }

    private async Task<bool> TryCaptureFromMetadataAsync(Uri uri, string outputPath)
    {
        // Named client dials through OutboundUrlGuard, covering redirect hops and the
        // page-controlled og:image/icon URLs downloaded below
        using var http = _httpClientFactory.CreateClient(OutboundHttpClientName);
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (DXSuite web preview)");

        string html;
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            var buffer = new char[MaxHtmlBytes];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            html = new string(buffer, 0, read);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata fetch failed for {Url}", uri);
            return false;
        }

        foreach (var imageUrl in ExtractCandidateImageUrls(html, uri))
        {
            try
            {
                var bytes = await DownloadImageBytesAsync(http, imageUrl);
                if (bytes != null && _thumbnailService.GenerateFromImageBytes(bytes, outputPath) != null)
                    return true;
            }
            catch
            {
                // Try the next candidate
            }
        }

        return false;
    }

    private const int MaxImageBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Downloads a candidate preview image, refusing anything over <see cref="MaxImageBytes"/> —
    /// the response comes from an untrusted remote page and would otherwise buffer wholesale.
    /// </summary>
    private static async Task<byte[]?> DownloadImageBytesAsync(HttpClient http, string url)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength > MaxImageBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0) return buffer.Length > 0 ? buffer.ToArray() : null;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxImageBytes) return null;
        }
    }

    /// <summary>
    /// Candidate preview images in preference order: og:image, twitter:image, apple-touch-icon,
    /// declared icons, then conventional /favicon.ico.
    /// </summary>
    private static IEnumerable<string> ExtractCandidateImageUrls(string html, Uri baseUri)
    {
        var candidates = new List<string>();

        void AddMatch(string pattern, int group = 1)
        {
            // The HTML is untrusted remote content; a match timeout bounds backtracking so a
            // crafted page can't pin a core (patterns run against up to MaxHtmlBytes of input).
            try
            {
                var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline,
                    TimeSpan.FromSeconds(2));
                if (m.Success) candidates.Add(m.Groups[group].Value);
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip this pattern; other candidates (or /favicon.ico) may still work
            }
        }

        AddMatch("<meta[^>]+property\\s*=\\s*[\"']og:image[\"'][^>]+content\\s*=\\s*[\"']([^\"']+)[\"']");
        AddMatch("<meta[^>]+content\\s*=\\s*[\"']([^\"']+)[\"'][^>]+property\\s*=\\s*[\"']og:image[\"']");
        AddMatch("<meta[^>]+name\\s*=\\s*[\"']twitter:image[\"'][^>]+content\\s*=\\s*[\"']([^\"']+)[\"']");
        AddMatch("<link[^>]+rel\\s*=\\s*[\"']apple-touch-icon[^\"']*[\"'][^>]+href\\s*=\\s*[\"']([^\"']+)[\"']");
        AddMatch("<link[^>]+rel\\s*=\\s*[\"'](?:shortcut )?icon[\"'][^>]+href\\s*=\\s*[\"']([^\"']+)[\"']");

        candidates.Add("/favicon.ico");

        foreach (var candidate in candidates)
        {
            if (Uri.TryCreate(baseUri, candidate, out var resolved))
                yield return resolved.ToString();
        }
    }
}
