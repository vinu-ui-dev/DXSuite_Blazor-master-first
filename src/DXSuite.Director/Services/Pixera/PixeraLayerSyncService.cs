using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Connectors.Services.Connectors;

namespace DXSuite.Director.Services.Pixera;

/// <summary>
/// Background service that maps the active scheduled asset on each Pixera folder channel
/// to its assigned composition layer. Runs every 30 seconds, offset 45 seconds from startup
/// to avoid racing with PixeraContentSyncService (which starts at 35s).
///
/// For each pixera-folder channel that has layers configured in pixeraLayerSettings:
///   1. Resolves the currently active schedule asset for the channel's display.
///   2. Skips if there is no active content (holds whatever is already on the layer).
///   3. Calls assignResourceToLayer + setTransportModeOnLayer on every configured layer.
///      Always re-applies so Pixera reboots self-correct within one cycle.
///   4. Writes lastSyncedAt back to ExtendedData.
/// </summary>
public class PixeraLayerSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PixeraLayerSyncService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly ConnectorConfigProtector _configProtector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ScheduleEvaluator _scheduleEvaluator;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);

    public PixeraLayerSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<PixeraLayerSyncService> logger,
        IDistributedLockService lockService,
        ConnectorConfigProtector configProtector,
        IHttpClientFactory httpClientFactory,
        ScheduleEvaluator scheduleEvaluator)
    {
        _scopeFactory      = scopeFactory;
        _logger            = logger;
        _lockService       = lockService;
        _configProtector   = configProtector;
        _httpClientFactory = httpClientFactory;
        _scheduleEvaluator = scheduleEvaluator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PixeraLayerSyncService: starting (delay {Delay}s)", StartupDelay.TotalSeconds);
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                "pixera-layer-sync", TimeSpan.FromMinutes(2), stoppingToken);

            if (distLock != null)
            {
                try
                {
                    _logger.LogDebug("PixeraLayerSyncService: running sync cycle");
                    await RunSyncCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PixeraLayerSyncService: error during sync cycle");
                }
            }
            else
            {
                _logger.LogDebug("Another instance is handling Pixera layer sync, skipping this cycle");
            }

            try { await Task.Delay(SyncInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var connectors = await db.TblCloudPlatformConnectors
            .Where(c => c.IsEnabled && c.ContentMode != "monitoring-only" && c.PlatformId == "pixera")
            .ToListAsync(ct);

        foreach (var conn in connectors)
        {
            try
            {
                await SyncConnectorLayersAsync(db, conn, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PixeraLayerSyncService: error syncing connector {ConnectorId}", conn.ConnectorId);
            }
        }
    }

    private async Task SyncConnectorLayersAsync(
        IDirectorDBContext db, TblCloudPlatformConnector conn, CancellationToken ct)
    {
        var config = _configProtector.Decrypt(conn.ConfigJson);
        if (string.IsNullOrEmpty(config?.BaseUrl)) return;

        // Only channels that have both a display (for schedule resolution) and a layer assignment
        var folderChannels = await db.TblContentChannels
            .Where(c => c.SourceType == "pixera-folder" &&
                        c.ConnectorId == conn.ConnectorId &&
                        c.DisplayId != null)
            .ToListAsync(ct);

        var channelsWithLayer = folderChannels
            .Select(c => (Channel: c, Settings: ParseLayerSettings(c.ExtendedData)))
            .Where(x => x.Settings?.AssignedLayerPaths?.Count > 0)
            .ToList();

        if (channelsWithLayer.Count == 0) return;

        var rpcUrl    = config.BaseUrl.TrimEnd('/') + "/jsonrpc";
        var apiClient = new PixeraApiClient(rpcUrl, _httpClientFactory.CreateClient(), _logger);

        var now = DateTime.Now; // Use local time — schedule events are stored in local time

        foreach (var (channel, settings) in channelsWithLayer)
        {
            try
            {
                await SyncChannelLayerAsync(db, apiClient, channel, settings!, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PixeraLayerSyncService: error syncing channel {ChannelId}", channel.ChannelId);
            }
        }
    }

    private async Task SyncChannelLayerAsync(
        IDirectorDBContext db,
        PixeraApiClient apiClient,
        TblContentChannel channel,
        LayerSettingsData settings,
        DateTime now,
        CancellationToken ct)
    {
        // Load schedule events for the display assigned to this folder channel
        var events = await db.TblScheduleEvents
            .Where(e => e.DisplayId == channel.DisplayId!.Value)
            .ToListAsync(ct);

        var activeAssetId = ResolveActiveAssetId(events, now);

        if (activeAssetId == null)
        {
            // No active schedule content — hold whatever is on the layer (no black screen)
            _logger.LogDebug(
                "PixeraLayerSyncService: no active content for channel {ChannelId} (display {DisplayId}) — holding",
                channel.ChannelId, channel.DisplayId);
            return;
        }

        // Look up the asset to get its filename for the resource path
        var asset = await db.TblAssets.FirstOrDefaultAsync(a => a.AssetId == activeAssetId.Value, ct);
        if (asset == null)
        {
            _logger.LogWarning(
                "PixeraLayerSyncService: asset {AssetId} not found — skipping channel {ChannelId}",
                activeAssetId, channel.ChannelId);
            return;
        }

        var fileName = Path.GetFileName(asset.AssetPath);

        // Prefer the Pixera path stored by PixeraContentSyncService over constructing it manually
        var resourcePath = GetStoredPixeraPath(channel.ExtendedData, fileName)
            ?? $"{channel.ExternalId}/{fileName}";

        // Push to every configured layer — always re-apply so a Pixera reboot self-corrects
        // within one sync cycle rather than holding Pixera's last-loaded state.
        foreach (var layerPath in settings.AssignedLayerPaths!)
        {
            _logger.LogInformation(
                "PixeraLayerSyncService: assigning '{ResourcePath}' to layer '{LayerPath}' for channel {ChannelId}",
                resourcePath, layerPath, channel.ChannelId);

            if (settings.TransportEnabled)
            {
                // Director-managed playback: live layer assignment plus transport commands.
                await apiClient.AssignResourceToLayerAsync(resourcePath, layerPath, ct);
                await apiClient.SetTransportModeOnLayerAsync(
                    layerPath, settings.TransportMode, settings.TransportLoop, ct);
            }
            else
            {
                // Timeline-governed playback: swap media on the programmed clip and clear
                // any live "domination" overrides. Live assignment or transport commands —
                // even Stop — would detach the layer from timeline sync.
                await AssignGovernedAsync(apiClient, channel, layerPath, resourcePath, ct);
            }
        }

        // Persist the last-sync timestamp for diagnostics. The RPC calls above take
        // seconds, so merge into the current database value rather than this cycle's
        // snapshot — an admin settings save landing mid-cycle must survive this write.
        channel.ExtendedData = PixeraChannelExtendedData.WithLayerSyncTimestamp(
            await PixeraChannelExtendedData.ReadCurrentAsync(db, channel.ChannelId, ct),
            DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Content swap for a timeline-governed layer (clip play mode "Lock to time").
    /// The media is swapped on the layer's programmed clip (index 0 — the pre-built
    /// long-duration looping block), only when it actually changed, and the layer's
    /// dominant (live override) state is cleared every cycle — so a stray manual
    /// assignment or transport press in the Pixera UI heals within one sync cycle
    /// instead of permanently detaching the layer from timeline sync.
    /// The governed clip is the operator's to build; when a layer has no clip this
    /// logs a warning and deliberately does NOT fall back to live assignment, which
    /// would recreate the exact override this mode exists to avoid.
    /// </summary>
    private async Task AssignGovernedAsync(
        PixeraApiClient apiClient,
        TblContentChannel channel,
        string layerPath,
        string resourcePath,
        CancellationToken ct)
    {
        var layerHandle = await apiClient.GetLayerHandleAsync(layerPath, ct);
        if (layerHandle == null || layerHandle.Type == JTokenType.Null)
        {
            _logger.LogWarning(
                "PixeraLayerSyncService: governed layer '{LayerPath}' not found for channel {ChannelId}",
                layerPath, channel.ChannelId);
            return;
        }

        var clipHandle = await apiClient.GetClipAtIndexAsync(layerHandle, 0, ct);
        if (clipHandle == null || clipHandle.Type == JTokenType.Null)
        {
            _logger.LogWarning(
                "PixeraLayerSyncService: governed layer '{LayerPath}' has no clip (channel {ChannelId}) — " +
                "build the lock-to-time looping clip in Pixera before assigning content",
                layerPath, channel.ChannelId);
            return;
        }

        // resourcePath is "{folderNamePath}/{resourceName}"
        var slashIdx     = resourcePath.LastIndexOf('/');
        var folderPath   = slashIdx > 0 ? resourcePath[..slashIdx] : channel.ExternalId ?? "";
        var resourceName = slashIdx > 0 ? resourcePath[(slashIdx + 1)..] : resourcePath;
        var resourceStem = Path.GetFileNameWithoutExtension(resourceName);

        // Re-assigning the same media every cycle could glitch live playback, so only
        // swap on a genuine change. Pixera resource names may or may not carry the
        // file extension — compare against both forms.
        var currentName = await apiClient.GetClipAssignedResourceNameAsync(clipHandle, ct);
        bool unchanged = !string.IsNullOrEmpty(currentName) &&
            (string.Equals(currentName, resourceName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(currentName, resourceStem, StringComparison.OrdinalIgnoreCase));

        if (!unchanged)
        {
            var resourceId = await apiClient.GetResourceIdAsync(folderPath, resourceName, ct)
                          ?? await apiClient.GetResourceIdAsync(folderPath, resourceStem, ct);
            if (resourceId == null)
            {
                _logger.LogWarning(
                    "PixeraLayerSyncService: resource '{ResourcePath}' not found for governed channel {ChannelId}",
                    resourcePath, channel.ChannelId);
                return;
            }

            _logger.LogInformation(
                "PixeraLayerSyncService: governed swap on layer '{LayerPath}' — clip resource -> '{ResourceName}' (channel {ChannelId})",
                layerPath, resourceName, channel.ChannelId);
            await apiClient.AssignResourceToClipAsync(clipHandle, resourceId.Value, setToResourceDuration: false, ct);
        }

        // Always clear dominant state — no-ops when the layer is clean.
        await apiClient.ResetLayerAssignedResourceAsync(layerHandle, ct);
        await apiClient.ResetLayerTransportModeAsync(layerHandle, ct);
    }

    // ── Schedule resolution ─────────────────────────────────────────────────

    private int? ResolveActiveAssetId(IList<TblScheduleEvent> events, DateTime now)
    {
        // Non-default events take priority — check time/date/day-of-week windows first
        foreach (var ev in events.Where(e => !e.DefaultContent).OrderBy(e => e.ScheduleEventId))
        {
            if (!_scheduleEvaluator.IsActive(new ScheduleEventWindow(ev), now)) continue;
            var assetId = ParseFirstAssetId(ev.ContentAssignment);
            if (assetId.HasValue) return assetId;
        }

        // Fall back to the default content event (no time window constraints)
        foreach (var ev in events.Where(e => e.DefaultContent).OrderBy(e => e.ScheduleEventId))
        {
            var assetId = ParseFirstAssetId(ev.ContentAssignment);
            if (assetId.HasValue) return assetId;
        }

        return null;
    }

    private static int? ParseFirstAssetId(string contentAssignment)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(contentAssignment);
            if (doc.RootElement.TryGetProperty("Content", out var content) &&
                content.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("AssetId", out var assetIdProp))
                    {
                        var id = assetIdProp.GetInt32();
                        if (id > 0) return id;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    // ── ExtendedData helpers ────────────────────────────────────────────────

    private static LayerSettingsData? ParseLayerSettings(string? extendedData)
    {
        if (string.IsNullOrEmpty(extendedData)) return null;
        try
        {
            var root = JObject.Parse(extendedData);
            var s = root["pixeraLayerSettings"] as JObject;
            if (s == null) return null;

            // Support both the new list format (assignedLayerPaths) and the old single-path format
            // (assignedLayerPath) so existing channel records migrate transparently on next save.
            List<string> paths;
            if (s["assignedLayerPaths"] is JArray arr)
                paths = arr.Select(t => t.ToString()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            else
            {
                var single = s["assignedLayerPath"]?.ToString();
                paths = string.IsNullOrEmpty(single) ? [] : [single];
            }

            return new LayerSettingsData
            {
                AssignedLayerPaths = paths,
                TransportMode      = s["transportMode"]?.Value<int>() ?? 1,
                TransportLoop      = s["transportLoop"]?.Value<bool>() ?? true,
                TransportEnabled   = s["transportEnabled"]?.Value<bool>() ?? true,
            };
        }
        catch { return null; }
    }

    private static string? GetStoredPixeraPath(string? extendedData, string fileName)
    {
        if (string.IsNullOrEmpty(extendedData)) return null;
        try
        {
            var root = JObject.Parse(extendedData);
            // New format: value is a JSON object with UncPath and PixeraPath properties
            if (root["pixeraHandleMap"]?[fileName] is JObject entry)
            {
                var path = entry["PixeraPath"]?.ToString();
                return string.IsNullOrEmpty(path) ? null : path;
            }
        }
        catch { }
        return null;
    }


    // ── Private record types ────────────────────────────────────────────────

    private sealed class LayerSettingsData
    {
        public List<string>? AssignedLayerPaths { get; set; }
        public int           TransportMode      { get; set; } = 1;
        public bool          TransportLoop      { get; set; } = true;
        public bool          TransportEnabled   { get; set; } = true;
    }

    /// <summary>
    /// Adapts TblScheduleEvent to IScheduleWindow so ScheduleEvaluator can evaluate it.
    /// </summary>
    private sealed record ScheduleEventWindow(TblScheduleEvent Event) : IScheduleWindow
    {
        public DateTime? StartDate => Event.StartDate;
        public DateTime? EndDate   => Event.EndDate;
        public TimeOnly? StartTime => Event.StartTime;
        public TimeOnly? EndTime   => Event.EndTime;
        public bool Sunday         => Event.Sunday;
        public bool Monday         => Event.Monday;
        public bool Tuesday        => Event.Tuesday;
        public bool Wednesday      => Event.Wednesday;
        public bool Thursday       => Event.Thursday;
        public bool Friday         => Event.Friday;
        public bool Saturday       => Event.Saturday;
    }
}
