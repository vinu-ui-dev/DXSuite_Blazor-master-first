using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.NetJets.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace DXSuite.NetJets.Controllers;

[ApiController]
[Route("api/netjets")]
public class NetJetsController : ControllerBase
{
    private readonly IDirectorDBContext _dbContext;
    private readonly ILogger<NetJetsController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;

    public NetJetsController(
        IDirectorDBContext dbContext,
        ILogger<NetJetsController> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IConfiguration config)
    {
        _dbContext = dbContext;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _config = config;
    }

    /// <summary>
    /// Returns the NetJets video wall content for the given location: main + detail messages
    /// (always length 3, compacted) plus the two toggle states. Moxie polls this to drive
    /// the video wall takeover banner and the full-screen TV mode.
    /// </summary>
    [HttpGet("GetNetJetsContent")]
    [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
    public async Task<IActionResult> GetNetJetsContent([FromQuery] int locationId)
    {
        if (this.CheckScope(ApiKeyScopes.SignageRead) is { } denied) return denied;
        var location = await _dbContext.TblLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        if (location == null)
        {
            return NotFound(new { error = $"Location {locationId} not found" });
        }

        NetJetsVideoWallState state = new NetJetsVideoWallState();
        if (!string.IsNullOrWhiteSpace(location.ExtendedData))
        {
            try
            {
                var parsed = JsonConvert.DeserializeObject<NetJetsVideoWallState>(location.ExtendedData);
                if (parsed != null)
                {
                    state = parsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize NetJets ExtendedData for location {LocationId}; returning defaults", locationId);
            }
        }

        // Defensive compaction in case any writer left unnormalized state on disk.
        state = state.Compact();

        var response = new NetJetsContentResponse
        {
            LocationId = locationId,
            MainMessage = state.MainMessage,
            DetailMessages = state.DetailMessages,
            MessagesEnabled = state.MessagesEnabled,
            FullScreenTv = state.FullScreenTv,
            SpecialContentTakeover = state.SpecialContentTakeover
        };

        return Ok(response);
    }

    /// <summary>
    /// Proxy for the KPI dashboard display. Fetches live data from the Moxie IronPython
    /// listener, caches it for 60 seconds, and re-serves it over HTTPS so the display
    /// never contacts the listener directly (avoids DHCP IP changes and mixed-content issues).
    /// </summary>
    [HttpGet("kpi")]
    [AllowAnonymous]
    public async Task<IActionResult> GetKpi(CancellationToken ct)
    {
        const string CacheKey = "netjets:kpi";

        if (_cache.TryGetValue(CacheKey, out string? cached))
            return Content(cached!, "application/json");

        var listenerUrl = _config["NetJets:KpiListenerUrl"] ?? "http://localhost:44322/KPI/";

        try
        {
            var client = _httpClientFactory.CreateClient();
            var json = await client.GetStringAsync(listenerUrl, ct);
            _cache.Set(CacheKey, json, TimeSpan.FromSeconds(60));
            return Content(json, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KPI listener unreachable at {Url}", listenerUrl);
            return StatusCode(503);
        }
    }
}
