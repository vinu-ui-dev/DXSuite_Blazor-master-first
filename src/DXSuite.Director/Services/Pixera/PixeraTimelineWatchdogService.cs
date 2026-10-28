using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Connectors.Services.Connectors;

namespace DXSuite.Director.Services.Pixera;

/// <summary>
/// Keeps timeline-governed Pixera playback running. For every imported timeline
/// channel ("pixera-timeline" rows) with control enabled, the watchdog checks the
/// timeline's transport each cycle and issues Play when it is not playing — a
/// rebooted Pixera comes up with its timelines stopped and would otherwise sit
/// idle until someone presses Play by hand.
///
/// The per-timeline toggle in Content Channels doubles as the maintenance
/// switch: disable it before intentionally pausing or stopping a timeline, or
/// the watchdog restarts it within one cycle.
/// </summary>
public class PixeraTimelineWatchdogService : BackgroundService
{
    private const int TransportPlay = 1; // 1 = Play, 2 = Pause, 3 = Stop

    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay  = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PixeraTimelineWatchdogService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly ConnectorConfigProtector _configProtector;
    private readonly IHttpClientFactory _httpClientFactory;

    public PixeraTimelineWatchdogService(
        IServiceScopeFactory scopeFactory,
        ILogger<PixeraTimelineWatchdogService> logger,
        IDistributedLockService lockService,
        ConnectorConfigProtector configProtector,
        IHttpClientFactory httpClientFactory)
    {
        _scopeFactory      = scopeFactory;
        _logger            = logger;
        _lockService       = lockService;
        _configProtector   = configProtector;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PixeraTimelineWatchdogService: starting (delay {Delay}s)", StartupDelay.TotalSeconds);
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                "pixera-timeline-watchdog", TimeSpan.FromMinutes(2), stoppingToken);

            if (distLock != null)
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PixeraTimelineWatchdogService: error during cycle");
                }
            }
            else
            {
                _logger.LogDebug("Another instance is handling the Pixera timeline watchdog, skipping this cycle");
            }

            try { await Task.Delay(CycleInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
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
                await WatchConnectorTimelinesAsync(db, conn, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PixeraTimelineWatchdogService: error on connector {ConnectorId}", conn.ConnectorId);
            }
        }
    }

    private async Task WatchConnectorTimelinesAsync(
        IDirectorDBContext db, TblCloudPlatformConnector conn, CancellationToken ct)
    {
        var config = _configProtector.Decrypt(conn.ConfigJson);
        if (string.IsNullOrEmpty(config?.BaseUrl)) return;

        var timelineChannels = await db.TblContentChannels.AsNoTracking()
            .Where(c => c.SourceType == "pixera-timeline" && c.ConnectorId == conn.ConnectorId)
            .ToListAsync(ct);

        var enabled = timelineChannels
            .Where(c => !string.IsNullOrEmpty(c.ExternalId) && IsControlEnabled(c.ExtendedData))
            .ToList();
        if (enabled.Count == 0) return;

        var rpcUrl    = config.BaseUrl.TrimEnd('/') + "/jsonrpc";
        var apiClient = new PixeraApiClient(rpcUrl, _httpClientFactory.CreateClient(), _logger);

        foreach (var channel in enabled)
        {
            try
            {
                var mode = await apiClient.GetTransportModeOnTimelineAsync(channel.ExternalId!, ct);
                if (mode == null)
                {
                    _logger.LogWarning(
                        "PixeraTimelineWatchdogService: timeline '{Timeline}' not found on connector {ConnectorId} — renamed or deleted?",
                        channel.ExternalId, conn.ConnectorId);
                    continue;
                }

                if (mode.Value != TransportPlay)
                {
                    _logger.LogInformation(
                        "PixeraTimelineWatchdogService: timeline '{Timeline}' is not playing (mode {Mode}) — issuing Play",
                        channel.ExternalId, mode.Value);
                    await apiClient.SetTransportModeOnTimelineAsync(channel.ExternalId!, TransportPlay, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "PixeraTimelineWatchdogService: error watching timeline '{Timeline}' on connector {ConnectorId}",
                    channel.ExternalId, conn.ConnectorId);
            }
        }
    }

    private static bool IsControlEnabled(string? extendedData)
    {
        if (string.IsNullOrEmpty(extendedData)) return false;
        try
        {
            var root = JObject.Parse(extendedData);
            return (root["pixeraTimelineSettings"] as JObject)?["controlEnabled"]?.Value<bool>() ?? false;
        }
        catch { return false; }
    }
}
