using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Controllers;

/// <summary>
/// Serves one zone of a wall-controller bundle's published layout to a player webzone.
/// A Moxie (or Pixera) webzone points at the HTML route once and never changes: preset
/// activation, dashboard edits, and dwell changes all arrive through the shell's own poll.
///
/// Both routes are anonymous by necessity — a CEF webzone performs a plain browser
/// navigation and cannot attach an X-API-KEY header, which is why every existing
/// player-facing route in PixeraController is anonymous too. The consequence is that a
/// wall's published dashboard URLs are readable by anyone who can reach this server and
/// knows the location id, the same exposure as /api/videowall/allocation/{id}. Deployments
/// that cannot accept that should drive the zones from a script against
/// /api/player/bundle-state instead, which is API-key authenticated.
/// </summary>
[ApiController]
[AllowAnonymous]
public class WallZoneController : ControllerBase
{
    /// <summary>Storage key the wall-controller bundle publishes its resolved layout to.</summary>
    private const string WallStateKey = "public.wall";

    private const int PollIntervalMs = 30000;
    private const int MinDwellSeconds = 5;
    private const int DefaultDwellSeconds = 60;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Default encoder escapes '<', '>' and '&', so a hostile dashboard URL cannot break out
    // of the <script> block the payload is embedded in.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDirectorDBContext _db;
    private readonly IOptions<FeatureFlags> _featureFlags;

    public WallZoneController(IDirectorDBContext db, IOptions<FeatureFlags> featureFlags)
    {
        _db = db;
        _featureFlags = featureFlags;
    }

    /// <summary>
    /// Self-contained shell for one zone. The current payload is baked in so the first paint
    /// needs no round trip; after that the shell polls the JSON route and re-renders only
    /// when the revision changes.
    /// </summary>
    [HttpGet("/player/wall-zone/{locationId:int}/{zoneIndex:int}")]
    public async Task<IActionResult> GetZoneShell(int locationId, int zoneIndex)
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();

        // Rendered inside the player's CEF browser as a webzone source; the global security
        // middleware's X-Frame-Options: DENY must be cleared or the frame stays blank.
        Response.Headers.Remove("X-Frame-Options");

        // Short max-age keeps the shell fresh while Director is up; stale-if-error lets the
        // player's own HTTP cache serve it through an outage.
        Response.Headers.CacheControl = "public, max-age=30, stale-if-error=86400";

        // A missing document is not an error here: an installer may wire the webzone before
        // the first preset is activated. The shell renders blank and picks content up on poll.
        var payload = await LoadZoneAsync(locationId, zoneIndex);
        var initialJson = payload == null ? "null" : JsonSerializer.Serialize(payload, WriteOptions);

        return Content(BuildZoneShellHtml(locationId, zoneIndex, initialJson), "text/html; charset=utf-8");
    }

    /// <summary>Polled by the shell. Carries the revision so the shell can skip a no-op re-render.</summary>
    [HttpGet("/api/player/wall-zone/{locationId:int}/{zoneIndex:int}")]
    public async Task<IActionResult> GetZoneData(int locationId, int zoneIndex)
    {
        if (!_featureFlags.Value.LocationBundles) return NotFound();

        var payload = await LoadZoneAsync(locationId, zoneIndex);
        if (payload == null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        return new JsonResult(payload, WriteOptions);
    }

    // ────────────────────────────────────────────────────────────────────────

    private async Task<ZonePayload?> LoadZoneAsync(int locationId, int zoneIndex)
    {
        var value = await _db.TblLocationBundleStorages
            .AsNoTracking()
            .Where(s => s.LocationId == locationId && s.StorageKey == WallStateKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (string.IsNullOrEmpty(value)) return null;

        WallDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<WallDocument>(value, ReadOptions);
        }
        catch (JsonException)
        {
            // The bundle owns this document's shape; malformed content is a publish-side bug
            // and must not take the player page down.
            return null;
        }

        var zone = document?.Zones?.FirstOrDefault(z => z.Index == zoneIndex);
        if (zone == null) return null;

        var dwell = zone.DwellSeconds > 0 ? zone.DwellSeconds : DefaultDwellSeconds;
        return new ZonePayload(
            document!.Revision,
            document.PresetName,
            zone with { DwellSeconds = Math.Max(MinDwellSeconds, dwell) });
    }

    private sealed record ZonePayload(int Revision, string? PresetName, WallZone Zone);

    private sealed record WallDocument(
        int Schema,
        int Revision,
        string? PresetId,
        string? PresetName,
        List<WallZone>? Zones);

    private sealed record WallZone(
        int Index,
        double Left,
        double Top,
        double Width,
        double Height,
        int DwellSeconds,
        List<WallItem>? Items);

    private sealed record WallItem(string? Name, string? Url);

    // Single-quoted HTML attributes and JS strings so the verbatim interpolated string needs
    // no "" escaping; CSS braces are doubled.
    private static string BuildZoneShellHtml(int locationId, int zoneIndex, string initialJson) =>
        $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8' />
<style>
html, body {{ margin: 0; padding: 0; background: transparent; overflow: hidden; width: 100vw; height: 100vh; }}
#stage {{ position: relative; width: 100%; height: 100%; }}
.slide {{ position: absolute; inset: 0; width: 100%; height: 100%; border: 0; opacity: 0; transition: opacity 0.5s ease; }}
.slide.active {{ opacity: 1; z-index: 1; }}
</style>
</head>
<body>
<div id='stage'></div>
<script>
var LOCATION  = {locationId};
var ZONE      = {zoneIndex};
var API       = '/api/player/wall-zone/' + LOCATION + '/' + ZONE;
var POLL_MS   = {PollIntervalMs};
var CACHE_KEY = 'wz-' + LOCATION + '-' + ZONE;
var INITIAL   = {initialJson};

var stage    = document.getElementById('stage');
var revision = -1;
var timer    = null;

// Every dashboard stays mounted for the life of a revision and rotation only toggles
// opacity. Rebuilding the iframe each cycle would re-run each dashboard's login redirect,
// so an SSO-protected dashboard would re-authenticate on every rotation.
function render(payload) {{
  if (!payload || !payload.zone) return;
  if (payload.revision === revision) return;
  revision = payload.revision;

  if (timer) {{ clearInterval(timer); timer = null; }}
  stage.innerHTML = '';

  var items = payload.zone.items || [];
  if (!items.length) return;

  var frames = items.map(function(item, i) {{
    var frame = document.createElement('iframe');
    frame.className = 'slide' + (i === 0 ? ' active' : '');
    // No sandbox attribute: these are customer-authored trusted dashboard URLs, and
    // sandboxing breaks the scripting and storage most real dashboards depend on.
    frame.setAttribute('scrolling', 'no');
    frame.setAttribute('allow', 'fullscreen');
    frame.src = item.url;
    stage.appendChild(frame);
    return frame;
  }});

  if (frames.length === 1) return;

  var dwellMs = payload.zone.dwellSeconds * 1000;
  var current = 0;
  timer = setInterval(function() {{
    frames[current].classList.remove('active');
    current = (current + 1) % frames.length;
    frames[current].classList.add('active');
  }}, dwellMs);
}}

function remember(payload) {{
  try {{ localStorage.setItem(CACHE_KEY, JSON.stringify(payload)); }} catch (e) {{}}
}}

function poll() {{
  fetch(API, {{ cache: 'no-store' }})
    .then(function(r) {{ return r.ok ? r.json() : null; }})
    .then(function(payload) {{
      if (!payload) return;
      remember(payload);
      render(payload);
    }})
    .catch(function() {{ /* unreachable — keep whatever is on screen */ }});
}}

// Cold start: prefer the baked payload, else the last one seen. Without this a player that
// reboots while Director is down would sit on a blank zone until the server returns.
var boot = INITIAL;
if (boot) {{
  remember(boot);
}} else {{
  try {{ boot = JSON.parse(localStorage.getItem(CACHE_KEY) || 'null'); }} catch (e) {{}}
}}
if (boot) render(boot);

poll();
setInterval(poll, POLL_MS);
</script>
</body>
</html>";
}
