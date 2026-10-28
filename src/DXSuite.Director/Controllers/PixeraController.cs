using System.Reflection.Metadata;
using System.Text.Json;
using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Services;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Controllers;

// Every route is registered under both /videowall (canonical) and /pixera (legacy
// alias). Deployed players carry hand-entered /pixera URLs in their Pixera project
// files / Moxie zone configs, which Director cannot update remotely, so the legacy
// alias must keep working indefinitely.
[ApiController]
[AllowAnonymous]
public class PixeraController : ControllerBase
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Used for reading JSON stored in database columns (e.g. ExtendedData), which may use any casing.
    private static readonly JsonSerializerOptions _dbReadOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPixeraAllocationService _allocationService;
    private readonly IServiceScopeFactory     _scopeFactory;

    public PixeraController(IPixeraAllocationService allocationService, IServiceScopeFactory scopeFactory)
    {
        _allocationService = allocationService;
        _scopeFactory      = scopeFactory;
    }

    private const string CanonicalPagePrefix = "/videowall";
    private const string CanonicalApiPrefix  = "/api/videowall";
    private const string LegacyPagePrefix    = "/pixera";
    private const string LegacyApiPrefix     = "/api/pixera";

    // The generated shells embed absolute URLs (API fetches, counter-content iframe srcs)
    // that must match the prefix the shell itself was served under: those URLs are cached
    // in the player's localStorage, so a shell must never reference the other alias.
    private (string PageBase, string ApiBase) ServingPrefixes()
    {
        var path = Request.Path.Value ?? string.Empty;
        bool legacy = path.StartsWith(LegacyPagePrefix, StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith(LegacyApiPrefix, StringComparison.OrdinalIgnoreCase);
        return legacy
            ? (LegacyPagePrefix, LegacyApiPrefix)
            : (CanonicalPagePrefix, CanonicalApiPrefix);
    }

    /// <summary>
    /// Returns the current display-cluster allocation as JSON, polled by the layer
    /// shells. Wrap-mode shells pass their strip geometry (stripw/striph/outh/left);
    /// when a pre-wrapped background rendition exists for the current zone set, the
    /// video zones are replaced by a single prewrapped zone covering this output so
    /// the shell plays one decode instead of slicing a canvas-wide video.
    /// </summary>
    /// <summary>
    /// Shared time base for output sync: wall pages phase-lock their looping videos to this
    /// clock so every output renders the same frame position regardless of when it loaded.
    /// </summary>
    [HttpGet("/api/videowall/servertime")]
    [HttpGet("/api/pixera/servertime")]
    public IActionResult GetServerTime() =>
        Ok(new { unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

    [HttpGet("/api/videowall/allocation/{locationId:int}")]
    [HttpGet("/api/pixera/allocation/{locationId:int}")]
    public async Task<IActionResult> GetAllocation(
        int locationId,
        [FromQuery] int stripw = 0,
        [FromQuery] int striph = 0,
        [FromQuery] int outh = 2160,
        [FromQuery] int left = 0,
        [FromServices] IBackgroundRenditionService? renditions = null)
    {
        var result = await _allocationService.GetCurrentAllocationAsync(locationId, DateTime.Now);

        if (stripw > 0 && striph > 0 && left >= 0 && renditions != null)
        {
            int strips = Math.Max(1, outh / striph);
            int span   = strips * stripw;
            int outputIndex = span > 0 ? left / span : 0;

            if (result.BackgroundZones is { Count: > 0 })
            {
                var rendition = renditions.TryGetRendition(
                    locationId, result.BackgroundZones, stripw, striph, outh, outputIndex);
                if (rendition.HasValue)
                {
                    // Non-video zones (image/HTML) still slice cheaply client-side and stay.
                    var kept = result.BackgroundZones
                        .Where(z => string.IsNullOrEmpty(z.ContentPath) ||
                                    GetMediaType(z.ContentPath) != "video")
                        .ToList();
                    int minZ = result.BackgroundZones.Min(z => z.ZIndex);
                    kept.Add(new BackgroundZone(
                        LeftPx: left, WidthPx: span, ZIndex: minZ, LoopCount: 1,
                        ContentPath: rendition.Value.Path, ContentVersion: rendition.Value.Version,
                        Prewrapped: true));
                    result = result with { BackgroundZones = kept };
                }
            }

            // Airline cluster background segments get the same pre-baked treatment: the
            // rendition (VP9 alpha, transparent where no cluster covers the canvas)
            // arrives as a prewrapped zone at the airline-background z (10), and the
            // per-cluster segments are cleared so the shell plays one decode instead
            // of one per segment repeat.
            var clusterRendition = renditions.TryGetClusterBackgroundRendition(
                locationId, result.Clusters, stripw, striph, outh, outputIndex);
            if (clusterRendition.HasValue)
            {
                var zones = (result.BackgroundZones ?? Array.Empty<BackgroundZone>()).ToList();
                zones.Add(new BackgroundZone(
                    LeftPx: left, WidthPx: span, ZIndex: 10, LoopCount: 1,
                    ContentPath: clusterRendition.Value.Path, ContentVersion: clusterRendition.Value.Version,
                    Prewrapped: true));
                result = result with
                {
                    BackgroundZones = zones,
                    Clusters = result.Clusters
                        .Select(c => c.BackgroundSegments is { Count: > 0 }
                            ? c with { BackgroundSegments = null } : c)
                        .ToList()
                };
            }
        }

        return new JsonResult(result, _jsonOpts);
    }

    /// <summary>
    /// Returns a self-contained HTML shell loaded by Pixera as a CEF web source.
    /// The shell fetches content from the allocation / counter-items API on load,
    /// caches it in localStorage, and polls for changes — re-rendering in-place without
    /// a page reload. localStorage lets the shell resume with last-known content even if
    /// Director was temporarily unreachable when the page first loaded or was refreshed.
    /// stale-if-error allows the browser's own HTTP cache to serve the shell under outage.
    /// </summary>
    [HttpGet("/videowall/layer")]
    [HttpGet("/pixera/layer")]
    public IActionResult GetLayer(
        [FromQuery] int    locationId,
        [FromQuery] string type = "mask",
        [FromQuery] string? layers = null,
        [FromQuery] int    left = 0,
        [FromQuery] int    debug = 0,
        [FromQuery] string? wrap = null,
        [FromQuery] int    outh = 2160,
        [FromQuery] int    refresh = 0,
        [FromQuery] string? id = null)
    {
        // Monitor id for URL heartbeat detection: threaded into the shell's polling
        // fetches so every poll carries it. Whitelisted before reaching embedded JS.
        var monitorId = id != null && UrlHeartbeatService.MonitorIdPattern.IsMatch(id) ? id : null;

        // layers= consolidates multiple layers into one page — one Chromium instance
        // per output instead of one per layer, for constrained players (e.g. Moxie).
        // type= remains the single-layer alias so existing Pixera URLs keep working.
        // Both are whitelisted to prevent injection into the embedded JS constant.
        string[] allowed = ["background", "mask", "counter", "logo"];
        var requested = (layers ?? type)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(allowed.Contains)
            .Distinct()
            .ToArray();
        if (requested.Length == 0) requested = ["mask"];

        // Clamp left to non-negative to prevent JS injection via a negative literal
        if (left < 0) left = 0;

        // wrap=WxH activates strip-wrapped placement for fixed-output players (e.g.
        // Moxie 4K outputs): the canvas row wraps into horizontal strips of W×H
        // stacked within each output, floor(outh/H) strips per output. Parsed to
        // ints so only numeric literals reach the embedded JS.
        int wrapW = 0, wrapH = 0;
        if (!string.IsNullOrEmpty(wrap))
        {
            var m = System.Text.RegularExpressions.Regex.Match(wrap, @"^(\d{3,5})x(\d{2,4})$");
            if (m.Success)
            {
                wrapW = int.Parse(m.Groups[1].Value);
                wrapH = int.Parse(m.Groups[2].Value);
            }
        }
        outh = Math.Clamp(outh, 100, 10000);

        // refresh=N reloads the page every N seconds (min 5 minutes) — a scheduled
        // maintenance reload for embedded players whose renderers degrade over long
        // uptimes. 0 disables.
        if (refresh > 0) refresh = Math.Clamp(refresh, 300, 604800);
        else refresh = 0;

        // These pages are loaded directly inside Pixera's CEF browser as webzone sources;
        // the global X-Frame-Options: deny header must be cleared so they render correctly.
        Response.Headers.Remove("X-Frame-Options");

        // Allow the browser (Pixera's CEF) to serve this shell from its HTTP cache if
        // Director is temporarily unreachable — same offline-resilience principle as the
        // DirectorClient localStorage strategy. Short max-age keeps content fresh when
        // Director is running; stale-if-error provides recovery during outages.
        Response.Headers["Cache-Control"] = "public, max-age=30, stale-if-error=86400";

        var (pageBase, apiBase) = ServingPrefixes();

        // Legacy single-layer counter URLs keep the dedicated counter shell; the
        // consolidated shell renders the same items when counter is one of layers=.
        if (layers == null && requested is ["counter"])
            return Content(BuildCounterLayerShellHtml(locationId, left, pageBase, apiBase, refresh, monitorId), "text/html");

        var layersJson = JsonSerializer.Serialize(requested);
        return Content(BuildLayerHtml(layersJson, locationId, left, pageBase, apiBase, debug != 0, wrapW, wrapH, outh, refresh, monitorId), "text/html");
    }

    /// <summary>
    /// Serves HTML mask/background files uploaded via Ticket Counter Scheduler.
    /// Bypasses static-file middleware to guarantee clean response headers — no
    /// Content-Encoding and no X-Frame-Options, both required for Pixera CEF iframes.
    /// Restricted to files under MediaAssets/AirlineContent/ by filename only.
    /// </summary>
    [HttpGet("/videowall/html-asset")]
    [HttpGet("/pixera/html-asset")]
    public IActionResult GetHtmlAsset(
        [FromQuery] string           path,
        [FromServices] ITenantMediaPathResolver mediaPaths)
    {
        // Accept only the filename component — prevents directory traversal
        var fileName = Path.GetFileName(path ?? "");
        if (string.IsNullOrEmpty(fileName) ||
            !fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest();
        }

        var filePath = Path.Combine(
            mediaPaths.GetRoot(), "MediaAssets", "AirlineContent", fileName);

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        Response.Headers.Remove("X-Frame-Options");
        // Short max-age keeps content fresh; stale-if-error allows Pixera's CEF to serve the
        // cached copy during a Director outage. The ?v= version token on the iframe src URL
        // (set by the allocation shell) ensures re-uploaded files bust this cache immediately
        // regardless of the max-age, so the two mechanisms complement each other.
        Response.Headers["Cache-Control"] = "public, max-age=60, stale-if-error=86400";

        var content = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        return Content(content, "text/html; charset=utf-8");
    }

    /// <summary>
    /// Serves the offline-resilience service worker registered by the wall shells.
    /// Served under both prefixes because a worker's scope is bound to its script URL
    /// directory; the script itself is prefix-neutral (it only inspects request URLs).
    /// no-cache so the browser's update check picks up new versions on each navigation.
    /// </summary>
    [HttpGet("/videowall/sw.js")]
    [HttpGet("/pixera/sw.js")]
    public IActionResult GetServiceWorker()
    {
        Response.Headers["Cache-Control"] = "no-cache";
        return Content(ServiceWorkerJs, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Offline-resilience worker for the wall shells. Strategy per request class:
    /// pages/iframes/APIs are network-first (TTFB timeout) with cache fallback, so
    /// normal operation is byte-identical to no-worker behavior; /servertime is
    /// never cached (a stale clock would skew video phase-lock); media is precached
    /// in full from manifests the shells post after each successful poll, and served
    /// offline with Range slicing because video elements request byte ranges.
    /// Mid-run outages were already survivable (shells tolerate poll failures +
    /// localStorage data cache); this closes the reboot-during-outage window where
    /// the initial navigation itself fails and CEF composites an error page.
    /// </summary>
    internal const string ServiceWorkerJs = """
// DXSuite video wall offline-resilience service worker (see PixeraController).
var VERSION = 'dxw-v1';
var PAGES = VERSION + '-pages';   // shell HTML, html-asset iframes, API JSON
var MEDIA = VERSION + '-media';   // manifest-precached content files
var META  = VERSION + '-meta';    // per-page asset manifests (eviction bookkeeping)
var TTFB_TIMEOUT_MS = 8000;

self.addEventListener('install', function () { self.skipWaiting(); });

self.addEventListener('activate', function (e) {
  e.waitUntil(
    caches.keys().then(function (keys) {
      return Promise.all(keys
        .filter(function (k) { return k.indexOf('dxw-') === 0 && k.indexOf(VERSION + '-') !== 0; })
        .map(function (k) { return caches.delete(k); }));
    }).then(function () { return self.clients.claim(); })
  );
});

// Timeout covers time-to-first-byte only: an unresponsive (hung, not down)
// server must not stall a wall render for the TCP timeout. Body streaming
// continues normally after headers arrive.
function fetchWithTimeout(requestOrUrl) {
  if (typeof AbortController === 'undefined') return fetch(requestOrUrl);
  var ctrl = new AbortController();
  var timer = setTimeout(function () { ctrl.abort(); }, TTFB_TIMEOUT_MS);
  var p = fetch(requestOrUrl, { signal: ctrl.signal });
  p.then(function () { clearTimeout(timer); }, function () { clearTimeout(timer); });
  return p;
}

function cacheFallback(url) {
  return caches.open(PAGES).then(function (pages) {
    return pages.match(url).then(function (hit) {
      if (hit) return hit;
      // html-asset iframes may be precached (they appear in asset manifests)
      // without ever having navigated while controlled.
      return caches.open(MEDIA).then(function (media) { return media.match(url); });
    });
  });
}

function networkFirst(request) {
  // fetch(navigationRequest, init) is rejected by Chromium; use the bare URL.
  var attempt = request.mode === 'navigate'
    ? fetchWithTimeout(request.url)
    : fetchWithTimeout(request);
  return attempt.then(function (resp) {
    if (resp.ok) {
      var clone = resp.clone();
      caches.open(PAGES).then(function (c) { c.put(request.url, clone); });
      return resp;
    }
    // Reachable but failing (app pool down, maintenance 503): prefer last-good.
    // 4xx responses are returned as-is — a deleted wall must not play forever.
    if (resp.status >= 500) {
      return cacheFallback(request.url).then(function (hit) { return hit || resp; });
    }
    return resp;
  }).catch(function (err) {
    return cacheFallback(request.url).then(function (hit) {
      if (hit) return hit;
      throw err;
    });
  });
}

function parseRange(header, total) {
  var m = /^bytes=(\d*)-(\d*)$/.exec(header || '');
  if (!m || (m[1] === '' && m[2] === '')) return null;
  var start, end;
  if (m[1] === '') {           // suffix form: last N bytes
    start = Math.max(0, total - parseInt(m[2], 10));
    end = total - 1;
  } else {
    start = parseInt(m[1], 10);
    end = m[2] === '' ? total - 1 : Math.min(parseInt(m[2], 10), total - 1);
  }
  if (isNaN(start) || isNaN(end) || start > end || start >= total) return null;
  return { start: start, end: end };
}

// Cached media is stored as full 200 responses; video elements request ranges,
// so offline playback slices the cached body into 206 responses on demand.
function cachedMediaResponse(request) {
  return caches.open(MEDIA).then(function (cache) {
    return cache.match(request.url).then(function (hit) {
      if (!hit) return null;
      var rangeHeader = request.headers.get('range');
      if (!rangeHeader) return hit;
      return hit.arrayBuffer().then(function (buf) {
        var r = parseRange(rangeHeader, buf.byteLength);
        if (!r) return new Response(buf, { status: 200, headers: { 'Content-Type': hit.headers.get('Content-Type') || 'application/octet-stream' } });
        return new Response(buf.slice(r.start, r.end + 1), {
          status: 206,
          headers: {
            'Content-Type': hit.headers.get('Content-Type') || 'application/octet-stream',
            'Content-Range': 'bytes ' + r.start + '-' + r.end + '/' + buf.byteLength,
            'Content-Length': String(r.end - r.start + 1),
            'Accept-Ranges': 'bytes'
          }
        });
      });
    });
  });
}

self.addEventListener('fetch', function (e) {
  var request = e.request;
  if (request.method !== 'GET') return;
  var url = new URL(request.url);
  if (url.origin !== self.location.origin) return;
  // Server time drives video phase-lock; a cached copy is wrong by definition.
  // Let it fail fast offline — the shells fall back to the client clock.
  if (url.pathname.slice(-11) === '/servertime') return;

  var isPageOrApi =
    request.mode === 'navigate' ||
    request.destination === 'document' ||
    request.destination === 'iframe' ||
    url.pathname.indexOf('/api/') === 0;

  if (isPageOrApi) {
    e.respondWith(networkFirst(request));
    return;
  }

  // Content files: pass through when the server responds; precached copy when not.
  // Successful live responses are not cached here — video traffic is partial
  // (206) responses, so full offline copies come only from the manifest precache.
  e.respondWith(
    fetchWithTimeout(request).then(function (resp) {
      if (resp.status >= 500) {
        return cachedMediaResponse(request).then(function (hit) { return hit || resp; });
      }
      return resp;
    }).catch(function (err) {
      return cachedMediaResponse(request).then(function (hit) {
        if (hit) return hit;
        throw err;
      });
    })
  );
});

// Shells post their current asset list after every successful poll. Downloads are
// chained sequentially so multi-hundred-MB videos never compete with live playback,
// and anything no longer referenced by any page's manifest is evicted.
var precacheChain = Promise.resolve();

self.addEventListener('message', function (e) {
  var data = e.data;
  if (!data || data.type !== 'dx-precache' || !Array.isArray(data.urls)) return;
  var pageKey = (e.source && e.source.url) || 'unknown';
  var urls = data.urls.filter(function (u) { return typeof u === 'string' && u.charAt(0) === '/'; });
  precacheChain = precacheChain
    .then(function () { return ensurePageCached(pageKey); })
    .then(function () { return updateMediaCache(pageKey, urls); })
    .catch(function () {});
  if (e.waitUntil) e.waitUntil(precacheChain);
});

// The navigation that installs this worker completes before the worker controls
// the page, so that first page load is never intercepted and never cached. A
// player that boots once and then runs for weeks without reloading would still
// have no cached shell when it reboots into an outage — the exact case this
// worker exists for. Fetching the client's own URL here closes that window on
// the first poll after install. Controlled navigations refresh it afterwards,
// so a stale copy self-heals on the next successful reload.
function ensurePageCached(pageUrl) {
  if (!pageUrl || pageUrl.indexOf(self.location.origin) !== 0) return Promise.resolve();
  return caches.open(PAGES).then(function (pages) {
    return pages.match(pageUrl).then(function (hit) {
      if (hit) return null;
      return fetch(pageUrl).then(function (resp) {
        return resp.status === 200 ? pages.put(pageUrl, resp) : null;
      }).catch(function () { return null; });
    });
  });
}

function updateMediaCache(pageKey, urls) {
  return Promise.all([caches.open(MEDIA), caches.open(META)]).then(function (opened) {
    var media = opened[0], meta = opened[1];
    return meta.put('/__dxw-manifest__?page=' + encodeURIComponent(pageKey),
                    new Response(JSON.stringify(urls)))
      .then(function () { return downloadMissing(media, urls); })
      .then(function () { return evictUnreferenced(media, meta); });
  });
}

function downloadMissing(media, urls) {
  var chain = Promise.resolve();
  urls.forEach(function (u) {
    chain = chain.then(function () {
      return media.match(u).then(function (hit) {
        if (hit) return null;
        return fetch(u).then(function (resp) {
          if (resp.status === 200) return media.put(u, resp);
          return null;
        }).catch(function () { return null; });
      });
    });
  });
  return chain;
}

function evictUnreferenced(media, meta) {
  return meta.keys().then(function (keys) {
    return Promise.all(keys.map(function (k) {
      return meta.match(k)
        .then(function (r) { return r.json(); })
        .catch(function () { return []; });
    }));
  }).then(function (lists) {
    var referenced = {};
    lists.forEach(function (l) {
      (l || []).forEach(function (u) { referenced[new URL(u, self.location.origin).href] = true; });
    });
    return media.keys().then(function (reqs) {
      return Promise.all(reqs.map(function (req) {
        return referenced[req.url] ? null : media.delete(req);
      }));
    });
  });
}
""";

    /// <summary>
    /// Returns the current counter-cluster allocation for a video wall location together
    /// with the resolved content assets for each cluster. Intended for external integrations
    /// that need to mirror or react to airline counter assignments.
    ///
    /// Authentication: X-API-KEY header (AdminApiKey scheme), scope signage:read.
    /// If the API key is scoped to a specific location it must match the requested locationId.
    ///
    /// Asset resolution per cluster (same priority as /videowall/counter-content):
    ///   1. Direct assets on the cluster's transient location
    ///   2. Assets in the airline's DefaultConstraintId library (TblUserGroupLocationDefault)
    ///   3. Non-layer assets uploaded directly to the wall location (ConstraintId = null)
    ///
    /// _mask sibling detection: assets whose name matches {primaryBasename}_mask.{ext}
    /// in the same content pool are folded into the primary asset's maskAssetPath field
    /// and excluded from the top-level asset list.
    /// </summary>
    [HttpGet("/api/videowall/wall-assignments")]
    [HttpGet("/api/pixera/wall-assignments")]
    [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
    public async Task<IActionResult> GetWallAssignments([FromQuery] int locationId)
    {
        if (this.CheckScope(ApiKeyScopes.SignageRead) is { } denied) return denied;

        // Enforce per-key location restriction set at key creation time.
        var keyLocationId = HttpContext.Items["ApiKeyLocationId"] as int?;
        if (keyLocationId.HasValue && keyLocationId.Value != locationId)
            return StatusCode(403, new { message = "API key is not authorized for this location." });

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var location = await db.TblLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == locationId);
        if (location == null)
            return NotFound(new { message = $"Location {locationId} not found." });

        var allocation = await _allocationService.GetCurrentAllocationAsync(locationId, DateTime.Now);

        // Resolve assets per cluster once and cache by index so the per-display list can
        // share the same resolved asset DTOs without re-querying the database.
        var clusterAssets = new IReadOnlyList<WallAssignmentAsset>[allocation.Clusters.Count];
        var resultClusters = new List<WallAssignmentCluster>();
        for (int ci = 0; ci < allocation.Clusters.Count; ci++)
        {
            var cluster   = allocation.Clusters[ci];
            var assets    = await ResolveClusterAssetsAsync(db, cluster, locationId);
            var assetDtos = BuildWallAssignmentAssets(assets);
            clusterAssets[ci] = assetDtos;

            resultClusters.Add(new WallAssignmentCluster(
                GroupId:             cluster.GroupId,
                GroupName:           cluster.GroupName,
                DisplayNames:        cluster.DisplayNames,
                LeftPx:              cluster.LeftPx,
                WidthPx:             cluster.WidthPx,
                TransientLocationId: cluster.TransientLocationId,
                MaskPath:            cluster.MaskPath,
                BackgroundPath:      cluster.BackgroundPath,
                Assets:              assetDtos));
        }

        // Expand into one entry per physical counter display with individual canvas positions.
        var resultDisplays = await BuildDisplayListAsync(db, location, allocation, clusterAssets);

        var response = new WallAssignmentsResponse(
            LocationId:   locationId,
            LocationName: location.LocationName ?? "",
            AsOf:         DateTime.Now,
            Clusters:     resultClusters,
            Displays:     resultDisplays);

        return new JsonResult(response, _jsonOpts);
    }

    /// <summary>
    /// Expands the cluster allocation into one WallAssignmentDisplay per individual counter
    /// display. Canvas positions are computed with ComputeAccumulatedLeft (same approach as
    /// BuildCounterItemsAsync) so leftPx/widthPx values are consistent with the counter layer shell.
    /// All physical counter displays are included regardless of whether they have active content;
    /// displays belonging to unallocated (empty) clusters emit an empty asset list.
    /// </summary>
    private async Task<IReadOnlyList<WallAssignmentDisplay>> BuildDisplayListAsync(
        IDirectorDBContext db,
        TblLocation location,
        AllocationResult allocation,
        IReadOnlyList<WallAssignmentAsset>[] clusterAssets)
    {
        VideoWallConfiguration? config = null;
        if (!string.IsNullOrEmpty(location.ExtendedData))
            try { config = JsonSerializer.Deserialize<VideoWallConfiguration>(location.ExtendedData, _dbReadOpts); }
            catch { }

        var counterGroups = config?.Layout?.Groups
            .Where(g => g.AllowAirlineAllocation).ToList() ?? [];
        if (counterGroups.Count == 0) return [];

        var rawDisplayIds = counterGroups.SelectMany(g => g.DisplayIds).Distinct().ToList();
        var displays = await db.TblDisplays.AsNoTracking()
            .Where(d => rawDisplayIds.Contains(d.DisplayId))
            .ToListAsync();
        var displayById = displays.ToDictionary(d => d.DisplayId);

        var orderedDisplayIds = SortDisplaysByPosition(counterGroups, displayById);
        if (orderedDisplayIds.Count == 0) return [];

        // Map each position in the ordered list to the cluster that owns it.
        // Cluster.StartIndex + Cluster.Count define a contiguous slice of orderedDisplayIds.
        var clusterIndexByPosition = new int?[orderedDisplayIds.Count];
        for (int ci = 0; ci < allocation.Clusters.Count; ci++)
        {
            var cluster = allocation.Clusters[ci];
            for (int di = cluster.StartIndex;
                 di < cluster.StartIndex + cluster.Count && di < orderedDisplayIds.Count;
                 di++)
            {
                clusterIndexByPosition[di] = ci;
            }
        }

        // Compute per-display canvas positions and widths using ComputeAccumulatedLeft, which
        // correctly incorporates each group's PositionLeft and uses within-group display indices
        // for the gap pattern rather than global orderedDisplayIds indices.
        var leftByDisplayId  = new Dictionary<int, double>();
        var widthByDisplayId = new Dictionary<int, double>();
        foreach (var grp in counterGroups)
        {
            double scale = grp.DisplayScale > 0 ? grp.DisplayScale : 1.0;
            var scaledWidths = grp.DisplayIds
                .Select(id => displayById.TryGetValue(id, out var d) && d.DisplayWidth > 0
                              ? d.DisplayWidth * scale : 1920 * scale)
                .ToList<double>();
            for (int wi = 0; wi < grp.DisplayIds.Count; wi++)
            {
                var id = grp.DisplayIds[wi];
                leftByDisplayId[id]  = grp.ComputeAccumulatedLeft(wi, scaledWidths);
                widthByDisplayId[id] = scaledWidths[wi];
            }
        }

        // Resolve the currently active/selected asset per display using the same priority
        // chain as BuildCounterItemsAsync: OverrideAssetId → active event DefaultAssetId /
        // ContentAssignment → DefaultContent event.
        var now = DateTime.Now;
        var allEvents = await db.TblScheduleEvents.AsNoTracking()
            .Where(e => orderedDisplayIds.Contains(e.DisplayId))
            .ToListAsync();
        var eventsByDisplay = allEvents
            .GroupBy(e => e.DisplayId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var activeAssetIdByDisplay = new Dictionary<int, int>();
        foreach (var display in displays)
        {
            if (display.OverrideAssetId.HasValue)
            {
                activeAssetIdByDisplay[display.DisplayId] = display.OverrideAssetId.Value;
                continue;
            }
            var evs = eventsByDisplay.GetValueOrDefault(display.DisplayId, []);
            var id  = ResolveScheduledAssetId(evs, now);
            if (id.HasValue) activeAssetIdByDisplay[display.DisplayId] = id.Value;
        }

        // Load asset records for all active asset IDs so we can build the ActiveAsset DTO.
        var activeAssetIds = activeAssetIdByDisplay.Values.Distinct().ToList();
        var activeAssetMap = activeAssetIds.Count > 0
            ? (await db.TblAssets.AsNoTracking()
                .Where(a => activeAssetIds.Contains(a.AssetId))
                .ToListAsync()).ToDictionary(a => a.AssetId)
            : new Dictionary<int, TblAsset>();

        var result = new List<WallAssignmentDisplay>(orderedDisplayIds.Count);
        for (int gi = 0; gi < orderedDisplayIds.Count; gi++)
        {
            var displayId = orderedDisplayIds[gi];
            if (!displayById.TryGetValue(displayId, out var display)) continue;

            double leftPx  = leftByDisplayId.GetValueOrDefault(displayId, 0);
            double widthPx = widthByDisplayId.GetValueOrDefault(displayId, 1920);

            int?   groupId             = null;
            string groupName           = "";
            int?   transientLocationId = null;
            IReadOnlyList<WallAssignmentAsset> assets = [];

            if (clusterIndexByPosition[gi].HasValue)
            {
                var ci      = clusterIndexByPosition[gi]!.Value;
                var cluster = allocation.Clusters[ci];
                groupId             = cluster.GroupId;
                groupName           = cluster.GroupName;
                transientLocationId = cluster.TransientLocationId;
                assets              = clusterAssets[ci];
            }

            // Build the per-display active asset DTO — null when no status is selected.
            WallAssignmentAsset? activeAsset = null;
            if (activeAssetIdByDisplay.TryGetValue(displayId, out var activeId)
                && activeAssetMap.TryGetValue(activeId, out var activeRaw)
                && !IsMaskAsset(activeRaw.AssetName))
            {
                activeAsset = new WallAssignmentAsset(
                    AssetId:       activeRaw.AssetId,
                    AssetName:     activeRaw.AssetName,
                    AssetPath:     activeRaw.AssetPath,
                    MaskAssetPath: activeRaw.VideoMaskPath,
                    AssetTypeId:   activeRaw.AssetTypeId,
                    Duration:      activeRaw.Duration,
                    SortOrder:     activeRaw.SortOrder);
            }

            result.Add(new WallAssignmentDisplay(
                DisplayId:           displayId,
                DisplayName:         display.DisplayName ?? $"Display {displayId}",
                LeftPx:              leftPx,
                WidthPx:             widthPx,
                GroupId:             groupId,
                GroupName:           groupName,
                TransientLocationId: transientLocationId,
                ActiveAsset:         activeAsset,
                Assets:              assets));
        }

        return result;
    }

    /// <summary>
    /// Returns the per-display render items for the counter layer as JSON.
    /// Called by the counter layer shell every 30 seconds; content is also cached in
    /// localStorage so the shell can render immediately on next load if Director is down.
    /// </summary>
    [HttpGet("/api/videowall/counter-items")]
    [HttpGet("/api/pixera/counter-items")]
    public async Task<IActionResult> GetCounterItems([FromQuery] int locationId)
    {
        var items = await BuildCounterItemsAsync(locationId, ServingPrefixes().PageBase);
        return new JsonResult(items, _jsonOpts);
    }

    /// <summary>
    /// Returns a self-contained HTML slideshow for a single counter cluster, loaded inside
    /// an iframe by the counter layer page. Content priority:
    ///   1. Assets belonging to the airline's transient cluster location (locationId param)
    ///   2. Assets from the airline's DefaultConstraintId (set in TblUserGroupLocationDefault)
    ///   3. Assets belonging to the parent VideoWall location (wallLocationId param)
    ///   4. Black screen (no content configured)
    /// The wall's GlobalConstraintId is intentionally excluded — it is a generic library that
    /// should not bleed into per-counter airline content slots.
    /// </summary>
    [HttpGet("/videowall/counter-content")]
    [HttpGet("/pixera/counter-content")]
    public async Task<IActionResult> GetCounterContent(
        [FromQuery] int  locationId,
        [FromQuery] int? wallLocationId,
        [FromQuery] int? defaultAssetId = null)
    {
        // Loaded inside an iframe within the counter layer page; framing must be permitted.
        Response.Headers.Remove("X-Frame-Options");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // When the active schedule block carries a pinned default asset, serve only that asset.
        // This ensures the scheduled default content plays at block start regardless of the full
        // constraint library contents. Falls through to the resolution chain if not set.
        if (defaultAssetId.HasValue)
        {
            var pinned = await db.TblAssets.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AssetId == defaultAssetId.Value && !a.IsDeleted);
            var pinnedList = pinned != null ? new List<TblAsset> { pinned } : new List<TblAsset>();
            return Content(BuildSlideshowHtml(pinnedList), "text/html");
        }

        // Resolve whether this is an airline transient cluster so we know which fallback rules apply.
        // Loaded upfront because it drives both the Step 1 filter and Step 2's constraint lookup.
        var primaryLocation = await db.TblLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == locationId);
        bool isTransient = primaryLocation?.IsTransientCluster == true;

        // Step 1: Direct assets on the primary location.
        // When the location is a transient cluster, all its assets are counter-specific by definition.
        // When the location is the wall itself (called without a wallLocationId, or as a fallback),
        // exclude constraint-bound assets — those belong to display layers (background/mask), not
        // counter display slots. Only assets uploaded directly to the wall with no layer constraint
        // are eligible as counter fallback content.
        var assets = await db.TblAssets.AsNoTracking()
            .Where(a => a.LocationId == locationId && !a.IsDeleted
                        && (isTransient || a.ConstraintId == null))
            .OrderBy(a => a.SortOrder)
            .ToListAsync();

        if (assets.Count == 0 && wallLocationId.HasValue && wallLocationId.Value != locationId)
        {
            // Step 2: Airline's DefaultConstraintId from TblUserGroupLocationDefault.
            // Transient cluster locations don't carry direct assets — airlines configure their
            // counter content via a shared constraint (asset library) set on this default record.
            if (isTransient && primaryLocation!.OwningUserGroupId.HasValue)
            {
                var def = await db.TblUserGroupLocationDefaults.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.LocationId == wallLocationId.Value
                                           && d.UserGroupId == primaryLocation.OwningUserGroupId.Value);
                if (def?.DefaultConstraintId.HasValue == true)
                {
                    assets = await db.TblAssets.AsNoTracking()
                        .Where(a => a.ConstraintId == def.DefaultConstraintId.Value && !a.IsDeleted)
                        .OrderBy(a => a.SortOrder)
                        .ToListAsync();
                }
            }

            // Step 3: Explicitly-uploaded fallback content on the wall location.
            // Constraint-bound assets belong to display layers (background/mask), not counter slots —
            // only assets uploaded directly to the wall with no layer constraint are served here.
            if (assets.Count == 0)
            {
                assets = await db.TblAssets.AsNoTracking()
                    .Where(a => a.LocationId == wallLocationId.Value && !a.IsDeleted && a.ConstraintId == null)
                    .OrderBy(a => a.SortOrder)
                    .ToListAsync();
            }
        }

        // Mask sibling assets (named {basename}_mask.{ext}) are exclusively for the Moxie
        // datapipe path and must never appear in the counter display slideshow.
        assets = assets.Where(a => !IsMaskAsset(a.AssetName)).ToList();

        return Content(BuildSlideshowHtml(assets), "text/html");
    }

    private static bool IsMaskAsset(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var dot = name.LastIndexOf('.');
        return dot > 5 && name[..dot].EndsWith("_mask", StringComparison.OrdinalIgnoreCase);
    }

    // DTO for individual display render items returned by GetCounterItems.
    // DisplayId is the counter shell's reconciliation key: a stable identity lets a
    // single counter's content change rebuild only that counter's element.
    private sealed record CounterDisplayItem(
        int DisplayId, double Left, double Top, double Width, double Height,
        string Src, string SrcType);

    /// <summary>
    /// Returns the minimal counter-layer shell HTML.
    /// No content is baked in — the shell fetches from /api/videowall/counter-items on load,
    /// caches the result in localStorage, and re-renders in-place on every poll cycle.
    /// </summary>
    internal static string BuildCounterLayerShellHtml(int locationId, int left, string pageBase, string apiBase, int refresh = 0, string? monitorId = null) => $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8' />
<style>
html, body {{ margin: 0; padding: 0; background: transparent; overflow: hidden; }}
.disp {{ position: absolute; overflow: hidden; }}
.disp img, .disp video {{ width: 100%; height: 100%; object-fit: cover; display: block; }}
.disp iframe {{ width: 100%; height: 100%; border: none; display: block; }}
</style>
</head>
<body>
<div id='cntr'></div>
<script>
var LOCATION  = {locationId};
var LEFT      = {left};
var PAGE_BASE = '{pageBase}';
var API_BASE  = '{apiBase}';
var MONITOR   = '{monitorId ?? ""}';
var POLL_MS   = 30000;
var CACHE_KEY = 'px-c-' + LOCATION + '-' + LEFT;
var REFRESH   = {refresh};

// Scheduled maintenance reload (refresh=N seconds, jittered ±5%) — see the layer shell.
if (REFRESH > 0) {{
  setTimeout(function() {{ location.reload(); }},
    REFRESH * 1000 * (0.95 + Math.random() * 0.1));
}}

// Offline resilience — see the layer shell.
if ('serviceWorker' in navigator) {{
  try {{ navigator.serviceWorker.register(PAGE_BASE + '/sw.js'); }} catch (e) {{}}
}}

// Keyed reconciliation (same pattern as the layer shell): only items whose own
// signature changed are rebuilt, so one counter's status change reloads exactly
// that counter — every other video/slideshow keeps playing untouched.
function reconcile(container, desired) {{
  var existing = {{}};
  for (var i = 0; i < container.children.length; i++) {{
    var ch = container.children[i];
    existing[ch.getAttribute('data-key')] = ch;
  }}
  var seen = {{}};
  desired.forEach(function(d) {{
    seen[d.key] = true;
    var el = existing[d.key];
    if (el && el.getAttribute('data-sig') === d.sig) return;
    var fresh = d.build();
    fresh.setAttribute('data-key', d.key);
    fresh.setAttribute('data-sig', d.sig);
    if (el) container.replaceChild(fresh, el);
    else container.appendChild(fresh);
  }});
  Object.keys(existing).forEach(function(k) {{
    if (!seen[k]) container.removeChild(existing[k]);
  }});
}}

// Last-resort self-heal (same rationale as the layer shell): rate-limited page
// reload rebuilds the media pipeline when element-level recovery cannot.
function reloadPage(reason) {{
  try {{ console.log('[pixera] page reload requested: ' + reason); }} catch(e) {{}}
  try {{
    var last = parseInt(localStorage.getItem('px-reload-at') || '0', 10);
    if (Date.now() - last < 600000) return;
    localStorage.setItem('px-reload-at', String(Date.now()));
  }} catch(e) {{}}
  location.reload();
}}

// Always-on stall recovery (same design as the layer shell's hardenVideo):
// watches currentTime advancement and — where supported — presented frames via
// requestVideoFrameCallback, which catches compositor stalls where decode keeps
// running but the screen freezes. Nudge → reattach source → reload page.
function hardenVideo(v, tag) {{
  var bornAt = Date.now();
  var lastFrameAt = 0;
  var lastTime = -1, stuck = 0;

  if (typeof v.requestVideoFrameCallback === 'function') {{
    lastFrameAt = Date.now();
    var onFrame = function() {{
      lastFrameAt = Date.now();
      v.requestVideoFrameCallback(onFrame);
    }};
    v.requestVideoFrameCallback(onFrame);
  }}

  v.addEventListener('ended', function() {{
    try {{ v.currentTime = 0; }} catch(e) {{}}
    var p = v.play(); if (p && p.catch) p.catch(function() {{}});
  }});
  v.addEventListener('error', function() {{
    var src = v.src;
    setTimeout(function() {{
      v.src = src; v.load();
      var p = v.play(); if (p && p.catch) p.catch(function() {{}});
    }}, 2000);
  }});

  var iv = setInterval(function() {{
    if (!document.body.contains(v)) {{ clearInterval(iv); return; }}
    if (v.paused) {{
      var p = v.play(); if (p && p.catch) p.catch(function() {{}});
      return;
    }}
    if (v.readyState < 2 && Date.now() - bornAt < 60000) return;

    var timeStalled  = v.currentTime === lastTime;
    var frameStalled = lastFrameAt > 0 && Date.now() - lastFrameAt > 15000;
    if (!timeStalled && !frameStalled) {{ lastTime = v.currentTime; stuck = 0; return; }}

    stuck++;
    if (stuck === 2) {{
      try {{ console.log('[pixera] video ' + tag + ' stalled — nudging'); }} catch(e) {{}}
      try {{ v.currentTime = 0; }} catch(e) {{}}
      var p2 = v.play(); if (p2 && p2.catch) p2.catch(function() {{}});
    }} else if (stuck === 4) {{
      try {{ console.log('[pixera] video ' + tag + ' still stalled — reattaching'); }} catch(e) {{}}
      var s = v.src;
      v.src = s; v.load();
      var p3 = v.play(); if (p3 && p3.catch) p3.catch(function() {{}});
    }} else if (stuck >= 6) {{
      reloadPage('video ' + tag + ' unrecoverable stall');
      stuck = 4;
    }}
  }}, 10000);
}}

function buildItem(d) {{
    var el = document.createElement('div');
    el.className    = 'disp';
    el.style.left   = (d.left - LEFT) + 'px';
    el.style.top    = d.top    + 'px';
    el.style.width  = d.width  + 'px';
    el.style.height = d.height + 'px';
    if (d.src && d.srcType === 'image') {{
      var img = document.createElement('img');
      img.src = d.src;
      el.appendChild(img);
    }} else if (d.src && d.srcType === 'video') {{
      var vid = document.createElement('video');
      // Attribute form required by some embedded Chromium builds for muted autoplay.
      vid.setAttribute('muted', '');
      vid.setAttribute('playsinline', '');
      vid.setAttribute('autoplay', '');
      vid.setAttribute('loop', '');
      vid.muted   = true;
      vid.loop    = true;
      vid.preload = 'auto';
      hardenVideo(vid, 'd' + (d.displayId != null ? d.displayId : ''));
      vid.src     = d.src;
      el.appendChild(vid);
      var p = vid.play();
      if (p && p.catch) p.catch(function() {{}});
    }} else if (d.src && d.srcType === 'iframe') {{
      // Airline has no per-display pinned asset; show the full constraint slideshow
      // served by counter-content for this cluster's transient location.
      var ifr = document.createElement('iframe');
      ifr.src = d.src;
      ifr.setAttribute('sandbox', 'allow-scripts allow-same-origin');
      ifr.scrolling = 'no';
      el.appendChild(ifr);
    }}
    return el;
}}

function applyItems(items) {{
  var desired = [];
  (items || []).forEach(function(d, i) {{
    // Skip items whose right edge falls entirely to the left of this tile's viewport.
    if (d.left + d.width <= LEFT) return;
    desired.push({{
      key: 'd:' + (d.displayId != null ? d.displayId : i),
      sig: [d.left, d.top, d.width, d.height, d.src, d.srcType].join(':'),
      build: function() {{ return buildItem(d); }}
    }});
  }});
  reconcile(document.getElementById('cntr'), desired);
  // Persist so the next page load can resume immediately if Director is unreachable.
  try {{ localStorage.setItem(CACHE_KEY, JSON.stringify(items)); }} catch(e) {{}}
  // Keep the worker's offline media copies current with what this tile renders.
  try {{
    if (navigator.serviceWorker && navigator.serviceWorker.controller) {{
      var urls = [];
      (items || []).forEach(function(d) {{
        if ((d.srcType === 'image' || d.srcType === 'video') && d.src &&
            d.left + d.width > LEFT && !/^https?:\/\//i.test(d.src)) urls.push(d.src);
      }});
      navigator.serviceWorker.controller.postMessage({{ type: 'dx-precache', urls: urls }});
    }}
  }} catch(e) {{}}
}}

function fetchItems() {{
  fetch(API_BASE + '/counter-items?locationId=' + LOCATION + (MONITOR ? '&id=' + MONITOR : ''))
    .then(function(r) {{ return r.json(); }})
    .then(applyItems)
    .catch(function() {{ /* server unreachable — hold current; localStorage remains valid */ }});
}}

// Cold-start recovery: show last-known content from localStorage before the first
// network fetch completes.  If Director was down when this page loaded (or Pixera
// refreshed it during an outage) this prevents a blank canvas from persisting.
try {{
  var cached = JSON.parse(localStorage.getItem(CACHE_KEY) || 'null');
  if (cached) applyItems(cached);
}} catch(e) {{}}

// Fetch fresh content immediately, then poll for changes without a full page reload.
fetchItems();
setInterval(fetchItems, POLL_MS);
</script>
</body>
</html>";

    /// <summary>
    /// Computes the per-display render items for the counter layer.
    /// Extracted so the same data is available via GetCounterItems for client-side fetching.
    /// Content priority per display:
    ///   1. TblDisplay.OverrideAssetId (real-time gate-agent status selector)
    ///   2. Active timed TblScheduleEvent.ContentAssignment[0].AssetId
    ///   3. DefaultContent TblScheduleEvent.ContentAssignment[0].AssetId
    ///   Displays with no explicit assignment return an empty-src item (renders blank).
    /// </summary>
    private async Task<List<CounterDisplayItem>> BuildCounterItemsAsync(int locationId, string pageBase)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var location = await db.TblLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        VideoWallConfiguration? config = null;
        if (!string.IsNullOrEmpty(location?.ExtendedData))
            try { config = JsonSerializer.Deserialize<VideoWallConfiguration>(location.ExtendedData, _dbReadOpts); }
            catch { }

        // Collect all counter groups; flatten display IDs in order across all groups.
        var counterGroups = config?.Layout?.Groups
            .Where(g => g.AllowAirlineAllocation).ToList() ?? [];
        if (counterGroups.Count == 0 || counterGroups.All(g => g.DisplayIds.Count == 0))
            return [];

        var rawDisplayIds = counterGroups.SelectMany(g => g.DisplayIds).Distinct().ToList();
        var now = DateTime.Now;

        var displays = await db.TblDisplays.AsNoTracking()
            .Where(d => rawDisplayIds.Contains(d.DisplayId))
            .ToListAsync();
        var displayById = displays.ToDictionary(d => d.DisplayId);

        // Sort by absolute canvas X position for consistent left-to-right ordering across groups.
        var orderedDisplayIds = SortDisplaysByPosition(counterGroups, displayById);

        var allEvents = await db.TblScheduleEvents.AsNoTracking()
            .Where(e => orderedDisplayIds.Contains(e.DisplayId))
            .ToListAsync();
        var eventsByDisplay = allEvents
            .GroupBy(e => e.DisplayId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Timed events beat default-content events; airline allocation events carry "{}" and
        // are skipped by ParseFirstAssetId, so they never contribute a content asset.
        var scheduledAssetIdByDisplay = new Dictionary<int, int>();
        foreach (var display in displays)
        {
            var evs = eventsByDisplay.GetValueOrDefault(display.DisplayId, []);
            var id  = ResolveScheduledAssetId(evs, now);
            if (id.HasValue) scheduledAssetIdByDisplay[display.DisplayId] = id.Value;
        }

        // For displays that have no pinned asset in their schedule, find the active airline group
        // so we can fall back to the airline's transient-cluster location content.
        var activeGroupIdByDisplay = new Dictionary<int, int?>();
        // Displays where the active event explicitly selected "None" (no content). The airline is
        // allocated but wants a blank slot — do not fall through to the DefaultConstraintId library.
        var explicitNoneDisplayIds = new HashSet<int>();
        foreach (var display in displays)
        {
            if (scheduledAssetIdByDisplay.ContainsKey(display.DisplayId)) continue;
            var evs = eventsByDisplay.GetValueOrDefault(display.DisplayId, []);
            foreach (var ev in evs.Where(e => !e.DefaultContent && e.UserGroupId.HasValue))
            {
                if (IsScheduleTimeActive(ev, now) && IsScheduleDayActive(ev, now))
                {
                    activeGroupIdByDisplay[display.DisplayId] = ev.UserGroupId;
                    // Explicit "None": no DefaultAssetId, no OverrideConstraintId, and no asset in
                    // ContentAssignment. This is a valid "I'm allocated but want nothing to play"
                    // state — it must not fall back to the DefaultConstraintId library.
                    if (ev.DefaultAssetId == null && ev.OverrideConstraintId == null
                        && ParseFirstAssetId(ev.ContentAssignment) == null)
                        explicitNoneDisplayIds.Add(display.DisplayId);
                    break;
                }
            }
        }

        // Load the transient cluster locations owned by each active airline at this wall.
        var activeGroupIds = activeGroupIdByDisplay.Values
            .Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
        var transientLocIdByGroup = new Dictionary<int, int>();
        var assetCountByTransientLoc = new Dictionary<int, int>();
        if (activeGroupIds.Count > 0)
        {
            var transientLocs = await db.TblLocations.AsNoTracking()
                .Where(l => l.IsTransientCluster
                         && l.ParentVideoWallLocationId == locationId
                         && l.OwningUserGroupId.HasValue
                         && activeGroupIds.Contains(l.OwningUserGroupId!.Value))
                .ToListAsync();

            transientLocIdByGroup = transientLocs
                .GroupBy(l => l.OwningUserGroupId!.Value)
                .ToDictionary(g => g.Key, g => g.First().LocationId);

            // Asset count per transient location is embedded in the counter-content URL as
            // ?v= so sig() detects changes when the airline uploads or removes content.
            var transLocIds = transientLocIdByGroup.Values.ToList();
            assetCountByTransientLoc = (await db.TblAssets.AsNoTracking()
                .Where(a => transLocIds.Contains(a.LocationId) && !a.IsDeleted)
                .GroupBy(a => a.LocationId)
                .Select(g => new { LocationId = g.Key, Count = g.Count() })
                .ToListAsync())
                .ToDictionary(x => x.LocationId, x => x.Count);
        }

        var neededIds = displays
            .Where(d => d.OverrideAssetId.HasValue)
            .Select(d => d.OverrideAssetId!.Value)
            .Union(scheduledAssetIdByDisplay.Values)
            .Distinct().ToList();
        var assetMap = neededIds.Count > 0
            ? (await db.TblAssets.AsNoTracking()
                .Where(a => neededIds.Contains(a.AssetId))
                .ToListAsync()).ToDictionary(a => a.AssetId)
            : new Dictionary<int, TblAsset>();

        // Map each display ID to its owning group for scale and direction lookups in the items loop below.
        var displayGroupMap = counterGroups
            .SelectMany(g => g.DisplayIds.Select(id => (id, group: g)))
            .GroupBy(x => x.id)
            .ToDictionary(g => g.Key, g => g.First().group);

        // Compute per-display canvas left positions using ComputeAccumulatedLeft, which correctly
        // incorporates each group's PositionLeft and uses within-group display indices for the gap
        // pattern rather than global orderedDisplayIds indices.
        var leftByDisplayId = new Dictionary<int, double>();
        foreach (var grp in counterGroups)
        {
            double scale = grp.DisplayScale > 0 ? grp.DisplayScale : 1.0;
            var scaledWidths = grp.DisplayIds
                .Select(id => displayById.TryGetValue(id, out var d) && d.DisplayWidth > 0
                              ? d.DisplayWidth * scale : 1920 * scale)
                .ToList<double>();
            for (int wi = 0; wi < grp.DisplayIds.Count; wi++)
                leftByDisplayId[grp.DisplayIds[wi]] = grp.ComputeAccumulatedLeft(wi, scaledWidths);
        }

        var items = new List<CounterDisplayItem>();
        for (int globalIdx = 0; globalIdx < orderedDisplayIds.Count; globalIdx++)
        {
            var displayId = orderedDisplayIds[globalIdx];
            if (!displayById.TryGetValue(displayId, out var display)) continue;

            var grp   = displayGroupMap.GetValueOrDefault(displayId, counterGroups[0]);
            double scale = grp.DisplayScale > 0 ? grp.DisplayScale : 1.0;
            bool   isHoriz = !string.Equals(grp.Direction, "vertical", StringComparison.OrdinalIgnoreCase);

            double dw   = display.DisplayWidth  > 0 ? display.DisplayWidth  * scale : 1920 * scale;
            double dh   = display.DisplayHeight > 0 ? display.DisplayHeight * scale : 1080 * scale;
            double left = leftByDisplayId.GetValueOrDefault(displayId, 0);
            double top  = 0;  // intentional: Pixera positions the webzone vertically; the HTML is full-viewport

            TblAsset? asset = null;
            if (display.OverrideAssetId.HasValue)
                assetMap.TryGetValue(display.OverrideAssetId.Value, out asset);
            if (asset == null && scheduledAssetIdByDisplay.TryGetValue(displayId, out var sid))
                assetMap.TryGetValue(sid, out asset);

            if (asset != null)
            {
                // A specific asset is pinned for this display via DefaultAssetId or ContentAssignment.
                items.Add(new CounterDisplayItem(
                    displayId, left, top, dw, dh,
                    NormalizePath(asset.AssetPath),
                    GetMediaType(asset.AssetPath)));
            }
            else if (activeGroupIdByDisplay.TryGetValue(displayId, out var gid) && gid.HasValue
                     && transientLocIdByGroup.TryGetValue(gid.Value, out var transLocId)
                     && !explicitNoneDisplayIds.Contains(displayId))
            {
                // No per-display asset pinned; serve the airline's transient-location content through
                // GetCounterContent, which resolves via the full constraint fallback chain.
                // ?v= embeds the current asset count so sig() forces a reload when content changes.
                var v = assetCountByTransientLoc.GetValueOrDefault(transLocId, 0);
                var url = $"{pageBase}/counter-content?locationId={transLocId}&wallLocationId={locationId}&v={v}";
                items.Add(new CounterDisplayItem(displayId, left, top, dw, dh, url, "iframe"));
            }
            else
            {
                // No airline scheduled on this display, or airline scheduled with explicit "None" — render blank.
                items.Add(new CounterDisplayItem(displayId, left, top, dw, dh, "", "none"));
            }
        }

        return items;
    }

    // Single-quote HTML attributes so the verbatim interpolated string needs no "" escaping in markup.
    // JS string literals also use single quotes; CSS braces are escaped as {{ }}.
    // No allocation data is baked in — the shell fetches from the API on load and caches the
    // result in localStorage so it can resume immediately on next load if Director is unreachable.
    internal static string BuildLayerHtml(
        string layersJson, int locationId, int left, string pageBase, string apiBase, bool debug = false,
        int wrapW = 0, int wrapH = 0, int outh = 2160, int refresh = 0, string? monitorId = null) =>
        $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8' />
<style>
html, body {{ margin: 0; padding: 0; background: transparent; overflow: hidden; }}
#canvas {{ position: fixed; inset: 0; }}
/* All element styling is inline (placeRect + builders). Consolidated z-order:
   background zones use their layout zIndex (typically 1) < airline background
   regions (10) < masks (15) < logo zones (layout zIndex, typically 20) <
   counter items (30). */
</style>
</head>
<body>
<div id='canvas'></div>
<script>
var LAYERS    = {layersJson};
var LOCATION  = {locationId};
var LEFT      = {left};
var PAGE_BASE = '{pageBase}';
var API_BASE  = '{apiBase}';
var MONITOR   = '{monitorId ?? ""}';
var POLL_MS   = 30000;
var DEBUG     = {(debug ? "true" : "false")};
// Wrap mode: the canvas row folds into horizontal strips of WRAP.w × WRAP.h
// stacked within this output; this output covers SPAN canvas px starting at LEFT.
var WRAP      = {(wrapW > 0 && wrapH > 0 ? $"{{w:{wrapW},h:{wrapH}}}" : "null")};
var OUTH      = {outh};
var STRIPS    = WRAP ? Math.max(1, Math.floor(OUTH / WRAP.h)) : 0;
var SPAN      = WRAP ? STRIPS * WRAP.w : 0;
var CACHE_KEY = 'px-l-' + LOCATION + '-' + LEFT + '-' + LAYERS.join('.') +
                (WRAP ? '-w' + WRAP.w + 'x' + WRAP.h : '');
var REFRESH   = {refresh};

// Scheduled maintenance reload (refresh=N seconds): blunt but effective self-heal
// for embedded players whose renderers degrade over long uptimes. Jittered ±5% so
// multiple webzones sharing the URL pattern don't all reload in the same instant.
if (REFRESH > 0) {{
  setTimeout(function() {{ location.reload(); }},
    REFRESH * 1000 * (0.95 + Math.random() * 0.1));
}}

// Offline resilience: the worker serves this page, its html-asset iframes, and
// precached media from CacheStorage when the server is unreachable, so a player
// that (re)boots during an outage renders last-known content instead of a
// browser error page. Requires a secure origin (https or localhost) and a
// service-worker-capable engine; elsewhere this is a harmless no-op and the page
// behaves exactly as it did before. &debug=1 reports which case applies, since
// players like Pixera CEF, Moxie, Ink and Engage offer no devtools.
if ('serviceWorker' in navigator) {{
  try {{
    navigator.serviceWorker.register(PAGE_BASE + '/sw.js')
      .then(function() {{ dbg('sw registered — offline cache active'); }})
      .catch(function(err) {{ dbg('sw register failed: ' + err); }});
  }} catch (e) {{ dbg('sw register threw: ' + e); }}
}} else {{
  dbg('sw unsupported' + (window.isSecureContext ? '' : ' (insecure origin — https required)') +
      '; offline cache disabled');
}}

function has(layer) {{ return LAYERS.indexOf(layer) >= 0; }}

// An element is renderable when any part of it falls within this output's span.
function visible(x, w) {{
  if (x + w <= LEFT) return false;
  if (WRAP && x >= LEFT + SPAN) return false;
  return true;
}}

// Inner content is sized to the full logical rect and shifted left by the slice
// offset; the enclosing clip container shows only the slice.
function mediaCss(off, w) {{
  return 'position:absolute;top:0;height:100%;left:' + (-off) + 'px;width:' + w + 'px;';
}}

// Places a canvas-space horizontal rect into the DOM and returns its element.
// Flat mode: one clip container at (canvasX - LEFT), full viewport height.
// Wrap mode: the rect is intersected with each strip of this output; every
// intersection becomes a clip container at (x within strip, strip * stripH)
// whose inner content — built by makeContent(offsetPx, sliceIdx), sized to the
// full rect — is shifted left so the correct slice shows through.
function placeRect(canvasX, canvasW, z, makeContent) {{
  if (!WRAP) {{
    var el = document.createElement('div');
    el.style.cssText = 'position:absolute;top:0;bottom:0;overflow:hidden;' +
      'left:' + (canvasX - LEFT) + 'px;width:' + canvasW + 'px;z-index:' + z + ';';
    el.appendChild(makeContent(0, 0));
    return el;
  }}
  var host = document.createElement('div');
  host.style.cssText = 'position:absolute;inset:0;z-index:' + z + ';';
  var n = 0;
  for (var s = 0; s < STRIPS; s++) {{
    var stripStart = LEFT + s * WRAP.w;
    var a = Math.max(canvasX, stripStart);
    var b = Math.min(canvasX + canvasW, stripStart + WRAP.w);
    if (b <= a) continue;
    var clip = document.createElement('div');
    clip.style.cssText = 'position:absolute;overflow:hidden;' +
      'left:' + (a - stripStart) + 'px;top:' + (s * WRAP.h) + 'px;' +
      'width:' + (b - a) + 'px;height:' + WRAP.h + 'px;';
    clip.appendChild(makeContent(a - canvasX, n));
    host.appendChild(clip);
    n++;
  }}
  if (n > 1) host.setAttribute('data-slices', n);
  return host;
}}

// On-screen diagnostic overlay (&debug=1) for players with no devtools access
// (e.g. Moxie/Pixera webzones): distinguishes codec, autoplay, decode, and
// network failures directly on the player.
function dbg(msg) {{
  if (!DEBUG) return;
  var el = document.getElementById('dbg');
  if (!el) {{
    el = document.createElement('div');
    el.id = 'dbg';
    el.style.cssText = 'position:fixed;left:0;top:0;z-index:9999;background:rgba(0,0,0,0.75);color:#0f0;font:12px/1.4 monospace;padding:6px;max-width:60%;max-height:100%;overflow:hidden;pointer-events:none;white-space:pre-wrap;';
    document.body.appendChild(el);
  }}
  el.textContent += msg + '\n';
}}

// Last-resort self-heal: reload the entire page, rebuilding the media pipeline.
// Element-level recovery cannot fix a stalled compositor (embedded CEF can stop
// presenting frames while decode keeps running). Rate-limited via localStorage so
// a permanently-broken source cannot put the player into a reload loop.
function reloadPage(reason) {{
  dbg('page reload requested: ' + reason);
  try {{
    var last = parseInt(localStorage.getItem('px-reload-at') || '0', 10);
    if (Date.now() - last < 600000) return;
    localStorage.setItem('px-reload-at', String(Date.now()));
  }} catch(e) {{}}
  location.reload();
}}

// Always-on stall recovery for long-running signage videos, watching two signals:
// currentTime not advancing (decode stalled: loop-boundary refetch, network,
// decoder hiccups) and — where supported — no frames presented for compositing
// via requestVideoFrameCallback. The latter catches GPU/compositor stalls in
// embedded CEF where decode keeps running but the screen freezes; currentTime
// alone looks healthy in that failure mode. Escalation ladder: nudge (seek 0 +
// play) → reattach source → reload the page. 'ended' despite loop and media
// errors get their own recovery.
function hardenVideo(v, tag) {{
  var bornAt = Date.now();
  var lastFrameAt = 0;   // 0 = no presentation signal available
  var lastTime = -1, stuck = 0;

  if (typeof v.requestVideoFrameCallback === 'function') {{
    lastFrameAt = Date.now();
    var onFrame = function() {{
      lastFrameAt = Date.now();
      v.requestVideoFrameCallback(onFrame);
    }};
    v.requestVideoFrameCallback(onFrame);
  }}

  v.addEventListener('ended', function() {{
    dbg('vid ' + tag + ' ended despite loop — restarting');
    try {{ v.currentTime = 0; }} catch(e) {{}}
    var p = v.play(); if (p && p.catch) p.catch(function() {{}});
  }});
  v.addEventListener('error', function() {{
    dbg('vid ' + tag + ' media error — reloading source');
    var src = v.src;
    setTimeout(function() {{
      v.src = src; v.load();
      var p = v.play(); if (p && p.catch) p.catch(function() {{}});
    }}, 2000);
  }});

  var iv = setInterval(function() {{
    if (!document.body.contains(v)) {{ clearInterval(iv); return; }}
    if (v.paused) {{
      // Never intentionally paused — resume (covers interrupted autoplay too).
      var p = v.play(); if (p && p.catch) p.catch(function() {{}});
      return;
    }}
    // Give a freshly-created element time to buffer before judging it stalled.
    if (v.readyState < 2 && Date.now() - bornAt < 60000) return;

    var timeStalled  = v.currentTime === lastTime;
    var frameStalled = lastFrameAt > 0 && Date.now() - lastFrameAt > 15000;
    if (!timeStalled && !frameStalled) {{ lastTime = v.currentTime; stuck = 0; return; }}

    stuck++;
    if (stuck === 2) {{
      dbg('vid ' + tag + ' stalled (' + (frameStalled ? 'no frames presented' : 'time frozen') + ') — nudging');
      try {{ v.currentTime = 0; }} catch(e) {{}}
      var p2 = v.play(); if (p2 && p2.catch) p2.catch(function() {{}});
    }} else if (stuck === 4) {{
      dbg('vid ' + tag + ' still stalled — reattaching source');
      var s = v.src;
      v.src = s; v.load();
      var p3 = v.play(); if (p3 && p3.catch) p3.catch(function() {{}});
    }} else if (stuck >= 6) {{
      reloadPage('video ' + tag + ' unrecoverable stall');
      stuck = 4; // if the reload was rate-limited, keep retrying the reattach cadence
    }}
  }}, 10000);
}}

function watchVideo(v, tag) {{
  if (!DEBUG) return;
  ['loadedmetadata','canplay','playing','pause','stalled','suspend','error'].forEach(function(evt) {{
    v.addEventListener(evt, function() {{
      var m = 'vid ' + tag + ' ' + evt;
      if (evt === 'loadedmetadata') m += ' ' + v.videoWidth + 'x' + v.videoHeight;
      // error codes: 1=aborted 2=network 3=decode 4=src-not-supported
      if (evt === 'error' && v.error) m += ' code=' + v.error.code + ' ' + (v.error.message || '');
      dbg(m);
    }});
  }});
  setTimeout(function() {{
    dbg('vid ' + tag + ' +4s readyState=' + v.readyState + ' paused=' + v.paused + ' t=' + v.currentTime.toFixed(1));
  }}, 4000);
}}

// ── Output sync: phase-lock looping videos to the server clock ────────────────
// Every output loads this page independently, so without a shared time base the
// same video free-runs at a different phase per output — and stall recovery
// re-desyncs single outputs over time. Each looping video is held at
// (serverNow % duration): small drift is slewed via playbackRate (invisible),
// large drift snaps currentTime. A recovered or late-booting output rejoins at
// the correct phase automatically. ±100ms is the realistic browser bound.
var CLOCK_SYNC = null;  // {{ off: serverMs - clientMs, rtt: sampleRttMs }}
function estimateClockOffset(attempt) {{
  attempt = attempt || 0;
  var t0 = Date.now();
  fetch(API_BASE + '/servertime', {{ cache: 'no-store' }})
    .then(function(r) {{ return r.json(); }})
    .then(function(j) {{
      var t1 = Date.now();
      var rtt = t1 - t0;
      // Keep the lowest-RTT sample of three — least network noise in the estimate
      if (CLOCK_SYNC === null || rtt < CLOCK_SYNC.rtt)
        CLOCK_SYNC = {{ off: j.unixMs + rtt / 2 - t1, rtt: rtt }};
      if (attempt < 2) setTimeout(function() {{ estimateClockOffset(attempt + 1); }}, 400);
      else dbg('clock offset ' + CLOCK_SYNC.off.toFixed(0) + 'ms (rtt ' + CLOCK_SYNC.rtt + 'ms)');
    }})
    .catch(function() {{
      if (attempt < 2) setTimeout(function() {{ estimateClockOffset(attempt + 1); }}, 1500);
    }});
}}
estimateClockOffset();

function serverNowSec() {{
  return (Date.now() + (CLOCK_SYNC ? CLOCK_SYNC.off : 0)) / 1000;
}}

function syncVideo(v, tag) {{
  function align(forceSnap) {{
    if (!v.loop || !v.duration || !isFinite(v.duration) || v.paused) return;
    var expected = serverNowSec() % v.duration;
    var drift = v.currentTime - expected;
    // Minimal circular difference so loop boundaries don't read as huge drift
    var half = v.duration / 2;
    if (drift >  half) drift -= v.duration;
    if (drift < -half) drift += v.duration;
    if (forceSnap || Math.abs(drift) > 0.75) {{
      v.currentTime = expected;
      v.playbackRate = 1;
      if (!forceSnap) dbg('sync ' + tag + ' snap drift=' + drift.toFixed(2));
    }} else if (Math.abs(drift) > 0.06) {{
      v.playbackRate = drift > 0 ? 0.97 : 1.03;
    }} else if (v.playbackRate !== 1) {{
      v.playbackRate = 1;
    }}
  }}
  // Initial phase seek once duration is known; also re-fires after stall
  // recovery reattaches the source, so recovered videos rejoin in phase
  v.addEventListener('loadedmetadata', function() {{ align(true); }});
  var iv = setInterval(function() {{
    // Reconciliation may replace elements; stop correcting detached ones
    if (!document.body.contains(v)) {{ clearInterval(iv); return; }}
    align(false);
  }}, 2000);
}}

if (DEBUG) {{
  dbg('layers=' + LAYERS.join(',') + ' loc=' + LOCATION + ' left=' + LEFT + ' vw=' + window.innerWidth + ' vh=' + window.innerHeight);
  dbg('ua: ' + navigator.userAgent);
  var probe = document.createElement('video');
  dbg('canPlay h264 base: '     + (probe.canPlayType('video/mp4; codecs=""avc1.42E01E""') || 'NO'));
  dbg('canPlay h264 high@4.0: ' + (probe.canPlayType('video/mp4; codecs=""avc1.640028""') || 'NO'));
  dbg('canPlay h264 high@6.0: ' + (probe.canPlayType('video/mp4; codecs=""avc1.64003C""') || 'NO'));
  dbg('canPlay vp9: '           + (probe.canPlayType('video/webm; codecs=""vp9""') || 'NO'));
}}

// Wrap mode renders at the output's native pixel size (stripW × outh). Embedded
// CEF viewports don't always report/render that size (a Moxie 4K zone once
// reported 1258×638), so when the actual viewport differs the whole canvas is
// scaled uniformly from the top-left rather than trusting the viewport.
if (WRAP) {{
  var cv = document.getElementById('canvas');
  cv.style.width  = WRAP.w + 'px';
  cv.style.height = OUTH + 'px';
  var iw = window.innerWidth || document.documentElement.clientWidth;
  if (iw && Math.abs(iw - WRAP.w) > 2) {{
    var sc = iw / WRAP.w;
    cv.style.transformOrigin = '0 0';
    cv.style.transform = 'scale(' + sc.toFixed(6) + ')';
    dbg('viewport ' + iw + 'px != stripW ' + WRAP.w + 'px; scaling x' + sc.toFixed(4));
  }}
  dbg('wrap ' + WRAP.w + 'x' + WRAP.h + ' strips=' + STRIPS + ' span=' + SPAN);
}}

// HTML mask files are served through a dedicated endpoint that guarantees clean
// response headers (no Content-Encoding, no X-Frame-Options); its URL already
// carries a query string, so the version token appends with '&'. All other paths
// (images, videos, external URLs) are used as-is and the token starts the query.
function assetUrl(path, version) {{
  if (path && /\.html$/i.test(path))
    return PAGE_BASE + '/html-asset?path=' + encodeURIComponent(path) +
           (version ? '&v=' + version : '');
  return path + (version ? '?v=' + version : '');
}}

// Keyed reconciliation: each element carries data-key (stable identity) and
// data-sig (content signature). Unchanged elements are left untouched — videos
// keep playing, iframes keep state — changed ones are rebuilt in place, removed
// ones deleted. Signatures contain only fields the element actually renders, so
// (e.g.) a mask re-upload never invalidates a background page's elements.
function reconcile(container, desired) {{
  var existing = {{}};
  for (var i = 0; i < container.children.length; i++) {{
    var ch = container.children[i];
    existing[ch.getAttribute('data-key')] = ch;
  }}
  var seen = {{}};
  desired.forEach(function(d) {{
    seen[d.key] = true;
    var el = existing[d.key];
    if (el && el.getAttribute('data-sig') === d.sig) return;
    var fresh = d.build();
    fresh.setAttribute('data-key', d.key);
    fresh.setAttribute('data-sig', d.sig);
    if (el) container.replaceChild(fresh, el);
    else container.appendChild(fresh);
  }});
  Object.keys(existing).forEach(function(k) {{
    if (!seen[k]) container.removeChild(existing[k]);
  }});
}}

// Zone rect within the canvas row: TopPx/HeightPx come from the layout editor
// (the admin previews always honored them; the wall now matches), so roles like
// accent strips can sit between counter logos. Zero height = legacy full-height.
function logoRectCss(off, z) {{
  var css = 'position:absolute;left:' + (-off) + 'px;width:' + z.widthPx + 'px;';
  return css + (z.heightPx > 0
    ? 'top:' + (z.topPx || 0) + 'px;height:' + z.heightPx + 'px;'
    : 'top:0;height:100%;');
}}

// Logo media is sized to the zone rect; the role's render mode maps content into it.
function logoFitCss(mode) {{
  if (mode === 'native') return 'object-fit:none;object-position:top left;';
  if (mode === 'fill-width' || mode === 'fill-height') return 'object-fit:cover;';
  return 'object-fit:contain;';
}}

function buildLogoEl(z, i) {{
  return placeRect(z.leftPx, z.widthPx, z.zIndex || 0, function(off, si) {{
    if (/\.html$/i.test(z.logoPath)) {{
      var iframe = document.createElement('iframe');
      iframe.src = assetUrl(z.logoPath, z.logoVersion);
      iframe.setAttribute('sandbox', 'allow-scripts allow-same-origin');
      iframe.scrolling = 'no';
      iframe.style.cssText = logoRectCss(off, z) + 'border:none;';
      return iframe;
    }}
    if (/\.(mp4|webm|mov|m4v)$/i.test(z.logoPath)) {{
      var vid = document.createElement('video');
      // Attribute form required by some embedded Chromium builds for muted autoplay.
      vid.setAttribute('muted', '');
      vid.setAttribute('playsinline', '');
      vid.setAttribute('autoplay', '');
      vid.setAttribute('loop', '');
      vid.muted = true; vid.loop = true;
      vid.style.cssText = logoRectCss(off, z) + logoFitCss(z.contentMode);
      watchVideo(vid, 'lg' + i + 's' + si);
      hardenVideo(vid, 'lg' + i + 's' + si);
      syncVideo(vid, 'lg' + i + 's' + si);
      vid.src = assetUrl(z.logoPath, z.logoVersion);
      var p = vid.play();
      if (p && p.catch) p.catch(function() {{}});
      return vid;
    }}
    var img = document.createElement('img');
    img.src = assetUrl(z.logoPath, z.logoVersion);
    img.style.cssText = logoRectCss(off, z) + logoFitCss(z.contentMode) + 'display:block;';
    return img;
  }});
}}

// One wall-level background zone tile, rendered beneath the airline cluster
// regions (which carry a higher inline z-index) so airline backgrounds paint over
// the wall background wherever assigned — fallback by stacking, not data merging.
function buildZoneEl(z, zi, t, x) {{
  // Pre-wrapped per-output rendition: the strips are already baked into the file —
  // render once at full output size, no slicing, one decode.
  if (z.prewrapped && WRAP) {{
    var host = document.createElement('div');
    host.style.cssText = 'position:absolute;left:0;top:0;overflow:hidden;' +
      'width:' + WRAP.w + 'px;height:' + (STRIPS * WRAP.h) + 'px;z-index:' + (z.zIndex || 0) + ';';
    var pvid = document.createElement('video');
    pvid.setAttribute('muted', '');
    pvid.setAttribute('playsinline', '');
    pvid.setAttribute('autoplay', '');
    pvid.setAttribute('loop', '');
    pvid.muted = true; pvid.loop = true;
    pvid.preload = 'auto';
    pvid.style.cssText = 'display:block;width:100%;height:100%;';
    watchVideo(pvid, 'pw' + zi);
    hardenVideo(pvid, 'pw' + zi);
    syncVideo(pvid, 'pw' + zi);
    pvid.src = assetUrl(z.contentPath, z.contentVersion);
    host.appendChild(pvid);
    var pp = pvid.play();
    if (pp && pp.catch) pp.catch(function() {{}});
    return host;
  }}
  return placeRect(x, z.widthPx, z.zIndex || 0, function(off, si) {{
    if (/\.(mp4|webm|mov|m4v)$/i.test(z.contentPath)) {{
      // Each wrap slice of a video is a separate full decode — zone videos that
      // span multiple strips should use pre-wrapped per-output renditions instead.
      if (si > 0) dbg('WARN video sliced (use a pre-wrapped rendition): ' + z.contentPath);
      var vid = document.createElement('video');
      // muted/playsinline are set as content attributes, not just IDL properties:
      // some embedded Chromium builds (CEF webzones) only honor the attribute form
      // when deciding whether muted autoplay is allowed.
      vid.setAttribute('muted', '');
      vid.setAttribute('playsinline', '');
      vid.setAttribute('autoplay', '');
      vid.setAttribute('loop', '');
      vid.muted = true; vid.loop = true;
      vid.style.cssText = mediaCss(off, z.widthPx) + 'object-fit:cover;';
      watchVideo(vid, 'z' + zi + 't' + t + 's' + si);
      hardenVideo(vid, 'z' + zi + 't' + t + 's' + si);
      syncVideo(vid, 'z' + zi + 't' + t + 's' + si);
      vid.src = assetUrl(z.contentPath, z.contentVersion);
      var p = vid.play();
      if (p && p.catch) p.catch(function(err) {{ dbg('vid z' + zi + 't' + t + ' play() rejected: ' + err); }});
      return vid;
    }}
    if (/\.html$/i.test(z.contentPath)) {{
      var ifr = document.createElement('iframe');
      ifr.src = assetUrl(z.contentPath, z.contentVersion);
      ifr.setAttribute('sandbox', 'allow-scripts allow-same-origin');
      ifr.scrolling = 'no';
      ifr.style.cssText = mediaCss(off, z.widthPx) + 'border:none;';
      return ifr;
    }}
    var img = document.createElement('img');
    img.src = assetUrl(z.contentPath, z.contentVersion);
    img.style.cssText = mediaCss(off, z.widthPx) + 'object-fit:cover;display:block;';
    return img;
  }});
}}

// The rightmost cluster extends to the output's right edge so the mask fills any
// trailing canvas space beyond the last physical display. Flat mode uses the
// viewport width (explicit pixels are more reliable than CSS right:0 in CEF);
// wrap mode uses this output's canvas span.
function clusterWidth(c) {{
  if (!c.isRightmost) return c.widthPx;
  var edge = WRAP ? (LEFT + SPAN)
                  : LEFT + (window.innerWidth || document.documentElement.clientWidth);
  return Math.max(c.widthPx, edge - c.leftPx);
}}

// Layer opacity (0-100) as a CSS declaration; empty at full opacity.
function opacityCss(pct) {{
  return (pct == null || pct >= 100) ? '' : 'opacity:' + (Math.max(0, pct) / 100) + ';';
}}

// Sizes region media per the airline's Render Mode. Default (and tile, until
// spatial video tiling exists) is pixel-exact: natural size anchored top-left,
// overflow clipped by the region container — matching the ffmpeg rendition
// baker's crop semantics. Explicit modes opt into scaling.
function regionMediaCss(off, w, mode) {{
  var base = 'position:absolute;top:0;left:' + (-off) + 'px;display:block;';
  if (mode === 'fill-width')  return base + 'width:' + w + 'px;height:auto;';
  if (mode === 'fill-height') return base + 'height:100%;width:auto;';
  if (mode === 'fit')         return base + 'width:' + w + 'px;height:100%;object-fit:contain;';
  return base + 'width:auto;height:auto;';
}}

// Airline cluster region (mask or background layer) filling the cluster rect.
// HTML renders in a sandboxed iframe (served with clean headers via html-asset);
// video and image content render as native elements, so framing headers never
// apply, and are sized by the airline's Render Mode (maskMode/backgroundMode).
// opacity (0-100) applies to whichever element type renders.
function buildRegionEl(c, path, version, z, mode, opacity) {{
  var w = clusterWidth(c);
  return placeRect(c.leftPx, w, z, function(off, si) {{
    if (/\.html$/i.test(path)) {{
      var iframe = document.createElement('iframe');
      iframe.src = assetUrl(path, version);
      iframe.setAttribute('sandbox', 'allow-scripts allow-same-origin');
      iframe.scrolling = 'no';
      iframe.style.cssText = mediaCss(off, w) + 'border:none;background:transparent;' + opacityCss(opacity);
      return iframe;
    }}
    if (/\.(mp4|webm|mov|m4v)$/i.test(path)) {{
      var vid = document.createElement('video');
      // Attribute form required by some embedded Chromium builds for muted autoplay.
      vid.setAttribute('muted', '');
      vid.setAttribute('playsinline', '');
      vid.setAttribute('autoplay', '');
      vid.setAttribute('loop', '');
      vid.muted = true; vid.loop = true;
      vid.style.cssText = regionMediaCss(off, w, mode) + opacityCss(opacity);
      watchVideo(vid, 'r' + z + 'c' + c.startIndex + 's' + si);
      hardenVideo(vid, 'r' + z + 'c' + c.startIndex + 's' + si);
      syncVideo(vid, 'r' + z + 'c' + c.startIndex + 's' + si);
      vid.src = assetUrl(path, version);
      var p = vid.play();
      if (p && p.catch) p.catch(function() {{}});
      return vid;
    }}
    if (mode === 'tile') {{
      // An <img> cannot repeat — tile via CSS background at natural size.
      var tiled = document.createElement('div');
      tiled.style.cssText = mediaCss(off, w) +
        'background-image:url(\'' + assetUrl(path, version) + '\');' +
        'background-size:auto;background-position:top left;background-repeat:repeat;' +
        opacityCss(opacity);
      return tiled;
    }}
    var img = document.createElement('img');
    img.src = assetUrl(path, version);
    img.style.cssText = regionMediaCss(off, w, mode) + opacityCss(opacity);
    return img;
  }});
}}

// One side-by-side layer segment placed at canvas rect [x, x+w). Media renders
// pixel-exact (native size, top-left) inside the clipped rect; videos get the
// full stall-recovery and phase-lock treatment so repeats stay frame-aligned.
function buildSegmentEl(seg, x, w, z, tag, opacity) {{
  return placeRect(x, w, z, function(off, si) {{
    var css = 'position:absolute;top:0;left:' + (-off) + 'px;display:block;width:auto;height:auto;' +
              opacityCss(opacity);
    if (/\.(mp4|webm|mov|m4v)$/i.test(seg.path)) {{
      var vid = document.createElement('video');
      // Attribute form required by some embedded Chromium builds for muted autoplay.
      vid.setAttribute('muted', '');
      vid.setAttribute('playsinline', '');
      vid.setAttribute('autoplay', '');
      vid.setAttribute('loop', '');
      vid.muted = true; vid.loop = true;
      vid.style.cssText = css;
      watchVideo(vid, tag + 's' + si);
      hardenVideo(vid, tag + 's' + si);
      syncVideo(vid, tag + 's' + si);
      vid.src = assetUrl(seg.path, seg.version);
      var p = vid.play();
      if (p && p.catch) p.catch(function() {{}});
      return vid;
    }}
    var img = document.createElement('img');
    img.src = assetUrl(seg.path, seg.version);
    img.style.cssText = css;
    return img;
  }});
}}

// Ordered side-by-side segments for a cluster layer: each renders at its native
// width, the next continues at the previous one's right edge, and the sequence
// wraps back to the first until the cluster is covered — so a single segment
// repeats itself across the width. A zero/unknown width fills the remaining
// cluster space and ends the sequence. Each repeat is a separate decode, same
// trade-off as background-zone LoopCount.
function pushRegionSegments(out, c, segs, z, keyPrefix, opacity) {{
  var end = c.leftPx + clusterWidth(c);
  var x = c.leftPx;
  var idx = 0;
  var guard = 128;
  while (x < end && guard-- > 0) {{
    (function(seg, sx, sidx) {{
      var w = seg.widthPx > 0 ? Math.min(seg.widthPx, end - sx) : end - sx;
      if (w > 0 && visible(sx, w)) {{
        out.push({{
          key: keyPrefix + ':' + c.startIndex + ':' + sidx,
          sig: seg.path + ':' + (seg.version || 0) + ':' + Math.round(sx) + ':' +
               Math.round(w) + ':' + z + ':' + (opacity == null ? 100 : opacity),
          build: function() {{ return buildSegmentEl(seg, sx, w, z, keyPrefix + c.startIndex + 'i' + sidx, opacity); }}
        }});
      }}
    }})(segs[idx % segs.length], x, idx);
    var sw = segs[idx % segs.length].widthPx;
    if (!(sw > 0)) break;
    x += sw; idx++;
  }}
}}

// One counter display item (same content resolution as the dedicated counter
// shell): the per-display resolved content from the counter-items API.
// Content carries its own top/height within the row (rows can be shorter than
// the strip); the horizontal rect is placed/sliced by placeRect.
function buildCounterItemEl(d) {{
  return placeRect(d.left, d.width, 30, function(off, si) {{
    var css = 'position:absolute;left:' + (-off) + 'px;top:' + d.top + 'px;' +
              'width:' + d.width + 'px;height:' + d.height + 'px;';
    if (d.src && d.srcType === 'image') {{
      var img = document.createElement('img');
      img.src = d.src;
      img.style.cssText = css + 'object-fit:cover;display:block;';
      return img;
    }}
    if (d.src && d.srcType === 'video') {{
      // A counter video crossing a strip boundary renders one decode per slice;
      // counters are small so this is cheap, but the copies can drift over time.
      if (si > 0) dbg('note: counter video spans a strip boundary: ctr' + d.displayId);
      var vid = document.createElement('video');
      // Attribute form required by some embedded Chromium builds for muted autoplay.
      vid.setAttribute('muted', '');
      vid.setAttribute('playsinline', '');
      vid.setAttribute('autoplay', '');
      vid.setAttribute('loop', '');
      vid.muted   = true;
      vid.loop    = true;
      vid.preload = 'auto';
      vid.style.cssText = css + 'object-fit:cover;';
      watchVideo(vid, 'ctr' + d.displayId + 's' + si);
      hardenVideo(vid, 'ctr' + d.displayId + 's' + si);
      syncVideo(vid, 'ctr' + d.displayId + 's' + si);
      vid.src = d.src;
      var p = vid.play();
      if (p && p.catch) p.catch(function() {{}});
      return vid;
    }}
    if (d.src && d.srcType === 'iframe') {{
      // Airline has no per-display pinned asset; show the constraint slideshow
      // served by counter-content for this cluster's transient location.
      var ifr = document.createElement('iframe');
      ifr.src = d.src;
      ifr.setAttribute('sandbox', 'allow-scripts allow-same-origin');
      ifr.scrolling = 'no';
      ifr.style.cssText = css + 'border:none;';
      return ifr;
    }}
    var blank = document.createElement('div');
    blank.style.cssText = css;
    return blank;
  }});
}}

// Desired element list for the enabled layers. Signatures carry only what each
// element renders (its geometry + its own content path/version), keeping change
// detection scoped per element and per layer. Keys are prefixed per layer so
// (e.g.) a cluster's mask and background elements coexist in one page.
function buildDesired(alloc, items) {{
  var out = [];

  if (has('background')) {{
    // loopCount > 1 repeats the zone's content side-by-side (horizontalImageLoop).
    ((alloc && alloc.backgroundZones) || []).forEach(function(z, zi) {{
      if (!z.contentPath) return;
      var loops = z.loopCount || 1;
      for (var t = 0; t < loops; t++) {{
        (function(t) {{
          var x = z.leftPx + t * z.widthPx;
          if (!visible(x, z.widthPx)) return;
          out.push({{
            key: 'bz:' + zi + ':' + t,
            sig: z.contentPath + ':' + (z.contentVersion || 0) + ':' +
                 Math.round(x) + ':' + Math.round(z.widthPx) + ':' + (z.zIndex || 0) +
                 ':pw' + (z.prewrapped ? 1 : 0),
            build: function() {{ return buildZoneEl(z, zi, t, x); }}
          }});
        }})(t);
      }}
    }});
  }}

  ((alloc && alloc.clusters) || []).forEach(function(c) {{
    if (!visible(c.leftPx, c.widthPx)) return;
    var geo = ':' + Math.round(c.leftPx) + ':' + Math.round(c.widthPx) + ':' + (c.isRightmost ? 1 : 0);
    if (has('background')) {{
      if (c.backgroundSegments && c.backgroundSegments.length) {{
        pushRegionSegments(out, c, c.backgroundSegments, 10, 'bseg', c.backgroundOpacityPct);
      }} else if (c.backgroundPath) {{
        out.push({{ key: 'b:cl:' + c.startIndex,
                    sig: c.groupId + ':' + (c.backgroundVersion || 0) + ':' + c.backgroundPath + ':' + (c.backgroundMode || '') + ':' + (c.backgroundOpacityPct == null ? 100 : c.backgroundOpacityPct) + geo,
                    build: function() {{ return buildRegionEl(c, c.backgroundPath, c.backgroundVersion, 10, c.backgroundMode, c.backgroundOpacityPct); }} }});
      }}
    }}
    if (has('mask')) {{
      if (c.maskSegments && c.maskSegments.length) {{
        pushRegionSegments(out, c, c.maskSegments, 15, 'mseg', c.maskOpacityPct);
      }} else if (c.maskPath) {{
        out.push({{ key: 'm:cl:' + c.startIndex,
                    sig: c.groupId + ':' + (c.maskVersion || 0) + ':' + c.maskPath + ':' + (c.maskMode || '') + ':' + (c.maskOpacityPct == null ? 100 : c.maskOpacityPct) + geo,
                    build: function() {{ return buildRegionEl(c, c.maskPath, c.maskVersion, 15, c.maskMode, c.maskOpacityPct); }} }});
      }}
    }}
  }});

  if (has('logo')) {{
    ((alloc && alloc.logoZones) || []).forEach(function(z, i) {{
      if (!z.logoPath) return;
      if (!visible(z.leftPx, z.widthPx)) return;
      out.push({{
        key: 'logo:' + i + ':' + (z.roleKey || ''),
        sig: (z.groupId || 'none') + ':' + (z.logoVersion || 0) + ':' + z.logoPath + ':' +
             (z.contentMode || '') + ':' + Math.round(z.leftPx) + ':' + Math.round(z.widthPx) + ':' +
             Math.round(z.topPx || 0) + ':' + Math.round(z.heightPx || 0) + ':' + (z.zIndex || 0),
        build: function() {{ return buildLogoEl(z, i); }}
      }});
    }});
  }}

  if (has('counter')) {{
    (items || []).forEach(function(d, i) {{
      if (!visible(d.left, d.width)) return;
      out.push({{
        key: 'd:' + (d.displayId != null ? d.displayId : i),
        sig: [d.left, d.top, d.width, d.height, d.src, d.srcType].join(':'),
        build: function() {{ return buildCounterItemEl(d); }}
      }});
    }});
  }}

  return out;
}}

// Mirrors buildDesired's content walk: every renderable asset URL for this
// output, handed to the service worker for offline precaching. A URL missed
// here degrades to online-only for that asset, nothing worse.
function collectAssets(alloc, items) {{
  var urls = {{}};
  function add(u) {{ if (u && !/^https?:\/\//i.test(u)) urls[u] = true; }}
  if (has('background')) {{
    ((alloc && alloc.backgroundZones) || []).forEach(function(z) {{
      if (z.contentPath && visible(z.leftPx, z.widthPx * (z.loopCount || 1)))
        add(assetUrl(z.contentPath, z.contentVersion));
    }});
  }}
  ((alloc && alloc.clusters) || []).forEach(function(c) {{
    if (!visible(c.leftPx, c.widthPx)) return;
    if (has('background')) {{
      if (c.backgroundSegments && c.backgroundSegments.length)
        c.backgroundSegments.forEach(function(s) {{ add(assetUrl(s.path, s.version)); }});
      else if (c.backgroundPath) add(assetUrl(c.backgroundPath, c.backgroundVersion));
    }}
    if (has('mask')) {{
      if (c.maskSegments && c.maskSegments.length)
        c.maskSegments.forEach(function(s) {{ add(assetUrl(s.path, s.version)); }});
      else if (c.maskPath) add(assetUrl(c.maskPath, c.maskVersion));
    }}
  }});
  if (has('logo')) {{
    ((alloc && alloc.logoZones) || []).forEach(function(z) {{
      if (z.logoPath && visible(z.leftPx, z.widthPx)) add(assetUrl(z.logoPath, z.logoVersion));
    }});
  }}
  if (has('counter')) {{
    (items || []).forEach(function(d) {{
      if ((d.srcType === 'image' || d.srcType === 'video') && d.src && visible(d.left, d.width))
        add(d.src);
    }});
  }}
  return Object.keys(urls);
}}

function applyData(state) {{
  if (!state || !state.alloc) return;
  reconcile(document.getElementById('canvas'), buildDesired(state.alloc, state.items));
  // Persist so the next page load can show last-known content immediately.
  try {{ localStorage.setItem(CACHE_KEY, JSON.stringify(state)); }} catch(e) {{}}
  // Keep the worker's offline media copies current with what this output renders.
  try {{
    if (navigator.serviceWorker && navigator.serviceWorker.controller)
      navigator.serviceWorker.controller.postMessage(
        {{ type: 'dx-precache', urls: collectAssets(state.alloc, state.items) }});
  }} catch(e) {{}}
}}

// Cold-start recovery: render from localStorage before the first network fetch completes.
// If Director was down when Pixera loaded this URL, this keeps the last-known layout visible
// rather than showing a blank canvas until the server becomes reachable again.
try {{
  var cached = JSON.parse(localStorage.getItem(CACHE_KEY) || 'null');
  if (cached) applyData(cached);
}} catch(e) {{}}

function fetchAndApply() {{
  // Wrap-mode pages pass their geometry so the server can substitute pre-wrapped
  // background renditions for this specific output.
  var allocUrl = API_BASE + '/allocation/' + LOCATION +
    (WRAP ? '?stripw=' + WRAP.w + '&striph=' + WRAP.h + '&outh=' + OUTH + '&left=' + LEFT : '') +
    (MONITOR ? (WRAP ? '&' : '?') + 'id=' + MONITOR : '');
  var pAlloc = fetch(allocUrl)
    .then(function(r) {{ dbg('alloc http ' + r.status); return r.json(); }});
  // The counter layer renders per-display items from its own endpoint; all other
  // layers come from the allocation payload.
  var pItems = has('counter')
    ? fetch(API_BASE + '/counter-items?locationId=' + LOCATION + (MONITOR ? '&id=' + MONITOR : ''))
        .then(function(r) {{ dbg('items http ' + r.status); return r.json(); }})
    : Promise.resolve(null);
  Promise.all([pAlloc, pItems])
    .then(function(res) {{
      dbg('alloc clusters=' + ((res[0] && res[0].clusters) || []).length +
          ' bgZones=' + ((res[0] && res[0].backgroundZones) || []).length +
          (res[1] ? ' counterItems=' + res[1].length : ''));
      applyData({{ alloc: res[0], items: res[1] }});
    }})
    .catch(function(e) {{ dbg('fetch FAILED: ' + e); /* server unreachable — hold current; localStorage remains valid */ }});
}}

// Fetch fresh data immediately, then poll for changes.
fetchAndApply();
setInterval(fetchAndApply, POLL_MS);
</script>
</body>
</html>";

    /// <summary>
    /// Builds a self-contained HTML slideshow page that rotates through the provided assets.
    /// Images advance after their configured Duration; videos advance when playback ends.
    /// Renders a black screen when the asset list is empty.
    /// </summary>
    private static string BuildSlideshowHtml(List<TblAsset> assets)
    {
        var slides = assets
            .Select(a => new
            {
                src      = NormalizePath(a.AssetPath),
                type     = GetMediaType(a.AssetPath),
                duration = a.Duration > 0 ? a.Duration : 7.0
            })
            .Where(s => s.type != "unknown" && !string.IsNullOrEmpty(s.src))
            .ToList();

        var slidesJson = JsonSerializer.Serialize(slides, _jsonOpts);

        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8' />
<style>
html, body {{ margin: 0; padding: 0; background: transparent; overflow: hidden; width: 100vw; height: 100vh; }}
#stage {{ position: relative; width: 100%; height: 100%; }}
.slide {{ position: absolute; inset: 0; opacity: 0; transition: opacity 0.5s ease; }}
.slide.active {{ opacity: 1; }}
.slide img, .slide video {{ width: 100%; height: 100%; object-fit: cover; display: block; }}
</style>
</head>
<body>
<div id='stage'></div>
<script>
var slides = {slidesJson};
var current = 0;
var timer = null;
var stage = document.getElementById('stage');

function buildSlide(s) {{
  var el = document.createElement('div');
  el.className = 'slide';
  if (s.type === 'image') {{
    var img = document.createElement('img');
    img.src = s.src;
    el.appendChild(img);
  }} else {{
    var vid = document.createElement('video');
    vid.src = s.src;
    vid.muted = true;
    vid.playsInline = true;
    vid.preload = 'auto';
    vid.addEventListener('ended', advance);
    el.appendChild(vid);
  }}
  return el;
}}

var els = slides.map(buildSlide);
els.forEach(function(e) {{ stage.appendChild(e); }});

function show(i) {{
  if (slides.length === 0) return;
  els.forEach(function(e, j) {{ e.classList.toggle('active', j === i); }});
  if (timer) clearTimeout(timer);
  if (slides[i].type === 'image') {{
    timer = setTimeout(advance, slides[i].duration * 1000);
  }} else {{
    var vid = els[i].querySelector('video');
    if (vid) {{ vid.currentTime = 0; vid.play().catch(function() {{}}); }}
  }}
}}

function advance() {{
  current = (current + 1) % slides.length;
  show(current);
}}

if (slides.length > 0) show(0);
</script>
</body>
</html>";
    }

    /// <summary>
    /// Returns the first AssetId from a ContentAssignment JSON blob, or null if the blob
    /// is empty / carries no content (e.g. airline-allocation events use "{}").
    /// </summary>
    private static int? ParseFirstAssetId(string? contentAssignment)
    {
        if (string.IsNullOrWhiteSpace(contentAssignment)) return null;
        try
        {
            using var doc = JsonDocument.Parse(contentAssignment);
            if (doc.RootElement.TryGetProperty("Content", out var content) &&
                content.ValueKind == JsonValueKind.Array &&
                content.GetArrayLength() > 0 &&
                content[0].TryGetProperty("AssetId", out var assetId))
                return assetId.GetInt32();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Returns the AssetId that should currently be showing on a display, based on its
    /// schedule events. Timed events (DefaultContent=false) that are currently active beat
    /// the standing default-content event.
    /// </summary>
    private static int? ResolveScheduledAssetId(List<TblScheduleEvent> events, DateTime atTime)
    {
        // 1. An active timed event carrying an explicit content reference (e.g. a StatusSelector
        //    temp event bounded to an access window) is a deliberate one-shot pick and wins
        //    outright. Airline allocation events carry "{}" here, so they fall through to step 2/3.
        foreach (var ev in events.Where(e => !e.DefaultContent))
        {
            if (!IsScheduleTimeActive(ev, atTime) || !IsScheduleDayActive(ev, atTime)) continue;
            var explicitId = ParseFirstAssetId(ev.ContentAssignment);
            if (explicitId.HasValue) return explicitId;
        }

        // 2. The DefaultContent event is the single source of truth for counter content. Both the
        //    Ticket Counter Scheduler (via DefaultAssetActivatorService, which copies an activated
        //    block's DefaultAssetId here on transition) and manual StatusSelector changes made from
        //    the main or transient view write the selected asset here — so reading it keeps the
        //    Pixera counter URL in sync with whatever the operator last selected on any surface.
        var defaultAsset = events
            .Where(e => e.DefaultContent)
            .Select(e => ParseFirstAssetId(e.ContentAssignment))
            .FirstOrDefault(id => id.HasValue);
        if (defaultAsset.HasValue) return defaultAsset;

        // 3. Pre-activation fallback: an airline block is active but the activator's 30s cycle has
        //    not yet copied its DefaultAssetId into the DefaultContent event. Show the scheduled
        //    default in the meantime; step 2 takes over once it is copied (or the operator changes it).
        foreach (var ev in events.Where(e => !e.DefaultContent))
        {
            if (!IsScheduleTimeActive(ev, atTime) || !IsScheduleDayActive(ev, atTime)) continue;
            if (ev.DefaultAssetId.HasValue) return ev.DefaultAssetId;
        }
        return null;
    }

    private static bool IsScheduleTimeActive(TblScheduleEvent e, DateTime atTime)
        => ScheduleTime.IsWithinWindow(TimeOnly.FromDateTime(atTime), e.StartTime, e.EndTime);

    private static bool IsScheduleDayActive(TblScheduleEvent e, DateTime atTime)
    {
        bool anySet = e.Sunday || e.Monday || e.Tuesday || e.Wednesday ||
                      e.Thursday || e.Friday || e.Saturday;
        if (!anySet) return true;
        return atTime.DayOfWeek switch
        {
            DayOfWeek.Sunday    => e.Sunday,
            DayOfWeek.Monday    => e.Monday,
            DayOfWeek.Tuesday   => e.Tuesday,
            DayOfWeek.Wednesday => e.Wednesday,
            DayOfWeek.Thursday  => e.Thursday,
            DayOfWeek.Friday    => e.Friday,
            DayOfWeek.Saturday  => e.Saturday,
            _                   => false
        };
    }

    /// <summary>
    /// Returns counter display IDs sorted by their absolute canvas X position.
    /// Mirrors the same method in PixeraAllocationService — keeps both orderings consistent.
    /// </summary>
    private static List<int> SortDisplaysByPosition(
        IReadOnlyList<PanoramaGroup> counterGroups,
        Dictionary<int, TblDisplay> displayById)
    {
        var positioned = new List<(int DisplayId, double AbsoluteX)>();
        foreach (var group in counterGroups)
        {
            double scale = group.DisplayScale > 0 ? group.DisplayScale : 1.0;
            var scaledWidths = group.DisplayIds
                .Select(id => displayById.TryGetValue(id, out var d) && d.DisplayWidth > 0
                    ? d.DisplayWidth * scale : 1920 * scale)
                .ToList<double>();
            for (int i = 0; i < group.DisplayIds.Count; i++)
                positioned.Add((group.DisplayIds[i], group.ComputeAccumulatedLeft(i, scaledWidths)));
        }
        return positioned.OrderBy(p => p.AbsoluteX).Select(p => p.DisplayId).Distinct().ToList();
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        path = path.Replace('\\', '/');
        return path.StartsWith('/') ? path : "/" + path;
    }

    private static string GetMediaType(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "unknown";
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" => "image",
            ".mp4" or ".webm" or ".mov" or ".m4v" => "video",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Resolves the content assets for a single cluster using the same three-step
    /// fallback chain as /videowall/counter-content:
    ///   1. Direct assets on the transient cluster location
    ///   2. Assets in the airline's DefaultConstraintId library
    ///   3. Non-layer assets on the wall location itself (ConstraintId == null)
    /// Returns an empty list for unallocated clusters (no TransientLocationId).
    /// </summary>
    private static async Task<List<TblAsset>> ResolveClusterAssetsAsync(
        IDirectorDBContext db, DisplayCluster cluster, int wallLocationId)
    {
        if (!cluster.TransientLocationId.HasValue)
            return [];

        int transLocId = cluster.TransientLocationId.Value;

        // Step 1: Direct assets on the transient location
        var assets = await db.TblAssets.AsNoTracking()
            .Where(a => a.LocationId == transLocId && !a.IsDeleted)
            .OrderBy(a => a.SortOrder)
            .ToListAsync();

        if (assets.Count == 0 && cluster.GroupId.HasValue)
        {
            // Step 2: Airline's DefaultConstraintId from TblUserGroupLocationDefault
            var def = await db.TblUserGroupLocationDefaults.AsNoTracking()
                .FirstOrDefaultAsync(d => d.LocationId == wallLocationId
                                       && d.UserGroupId == cluster.GroupId.Value);
            if (def?.DefaultConstraintId.HasValue == true)
            {
                assets = await db.TblAssets.AsNoTracking()
                    .Where(a => a.ConstraintId == def.DefaultConstraintId.Value && !a.IsDeleted)
                    .OrderBy(a => a.SortOrder)
                    .ToListAsync();
            }
        }

        // Step 3: Non-layer assets on the wall location (constraint-bound assets belong to layers)
        if (assets.Count == 0)
        {
            assets = await db.TblAssets.AsNoTracking()
                .Where(a => a.LocationId == wallLocationId && !a.IsDeleted && a.ConstraintId == null)
                .OrderBy(a => a.SortOrder)
                .ToListAsync();
        }

        return assets;
    }

    /// <summary>
    /// Converts a flat list of TblAsset records into wall-assignment DTOs.
    /// Assets whose name ends with _mask.{ext} are treated as mask companions rather
    /// than standalone content — they are paired with the primary asset that shares
    /// their basename (e.g. "logo_mask.png" pairs with "logo.png") and folded into
    /// that asset's MaskAssetPath field. Any _mask asset with no matching primary is
    /// silently dropped. The asset's VideoMaskPath field is used as a fallback when no
    /// _mask sibling exists in the pool.
    /// </summary>
    private static IReadOnlyList<WallAssignmentAsset> BuildWallAssignmentAssets(List<TblAsset> assets)
    {
        // Index mask-variant assets by the name they would pair with (i.e. the primary name).
        // "logo_mask.png" → key "logo.png"
        var maskByPrimaryName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assets)
        {
            if (string.IsNullOrEmpty(a.AssetName) || string.IsNullOrEmpty(a.AssetPath)) continue;
            var dot = a.AssetName.LastIndexOf('.');
            if (dot > 5 && a.AssetName[..dot].EndsWith("_mask", StringComparison.OrdinalIgnoreCase))
            {
                // Derive the primary filename: strip "_mask" before the extension
                var primaryName = a.AssetName[..(dot - 5)] + a.AssetName[dot..];
                maskByPrimaryName[primaryName] = a.AssetPath;
            }
        }

        // Emit one DTO per primary asset (i.e. assets that are NOT themselves _mask variants)
        var result = new List<WallAssignmentAsset>();
        foreach (var a in assets)
        {
            var name = a.AssetName ?? "";
            var dot  = name.LastIndexOf('.');
            bool isMaskVariant = dot > 5 && name[..dot].EndsWith("_mask", StringComparison.OrdinalIgnoreCase);
            if (isMaskVariant) continue;

            // Prefer a _mask sibling from the same pool; fall back to the DB VideoMaskPath field
            maskByPrimaryName.TryGetValue(name, out var maskPath);
            maskPath ??= a.VideoMaskPath;

            result.Add(new WallAssignmentAsset(
                AssetId:      a.AssetId,
                AssetName:    a.AssetName,
                AssetPath:    a.AssetPath,
                MaskAssetPath: maskPath,
                AssetTypeId:  a.AssetTypeId,
                Duration:     a.Duration,
                SortOrder:    a.SortOrder));
        }

        return result;
    }
}
