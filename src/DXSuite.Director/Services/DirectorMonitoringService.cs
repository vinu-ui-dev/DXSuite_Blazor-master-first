using DXSuite.Connectors.Services.Connectors;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Director.Services.Pixera;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// Absence detection for monitored Director URLs and active health probing of
/// Pixera connectors. Both feed the same transition state machine: one alert on
/// failure, one on recovery, fanned out to every registered IDirectorAlertSink
/// (bell notifications, SLC Pulse, future subscribers).
///
/// Runs with the same single-instance assumption as the Pixera sync/watchdog
/// services (distributed-lock guarded, default tenant scope).
/// </summary>
public class DirectorMonitoringService : BackgroundService
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay  = TimeSpan.FromSeconds(60);

    /// <summary>
    /// After startup (or a deploy), every monitored URL looks overdue because no
    /// polls were recorded while the app was down. Missing transitions are
    /// suppressed until players have had time to check in; recoveries are not.
    /// </summary>
    private static readonly TimeSpan MissingEvaluationGrace = TimeSpan.FromSeconds(180);

    private const int PixeraProbeEveryNthCycle = 2;   // 30s cycle → 60s probe cadence
    private const int PerfCaptureEveryNthProbe = 10;  // ~10 min between perf-JSON log captures

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UrlHeartbeatService _heartbeats;
    private readonly IEnumerable<IDirectorAlertSink> _sinks;
    private readonly DistributedLock.IDistributedLockService _lockService;
    private readonly ConnectorConfigProtector _configProtector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DirectorMonitoringService> _logger;

    private DateTime _startedUtc;
    private int _cycle;
    private int _probeCount;

    // Connector reachability state is in-memory only: after a restart a
    // still-down Pixera re-alerts once, which doubles as a reminder.
    private readonly Dictionary<int, bool> _connectorOnline = new();

    public DirectorMonitoringService(
        IServiceScopeFactory scopeFactory,
        UrlHeartbeatService heartbeats,
        IEnumerable<IDirectorAlertSink> sinks,
        DistributedLock.IDistributedLockService lockService,
        ConnectorConfigProtector configProtector,
        IHttpClientFactory httpClientFactory,
        ILogger<DirectorMonitoringService> logger)
    {
        _scopeFactory     = scopeFactory;
        _heartbeats       = heartbeats;
        _sinks            = sinks;
        _lockService      = lockService;
        _configProtector  = configProtector;
        _httpClientFactory = httpClientFactory;
        _logger           = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DirectorMonitoringService: starting (delay {Delay}s)", StartupDelay.TotalSeconds);
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        _startedUtc = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            using (var distLock = await _lockService.TryAcquireLockAsync(
                       "director-monitoring", TimeSpan.FromMinutes(2), stoppingToken))
            {
                if (distLock != null)
                {
                    try { await SweepUrlsAsync(stoppingToken); }
                    catch (Exception ex) { _logger.LogError(ex, "DirectorMonitoringService: URL sweep failed"); }

                    if (++_cycle % PixeraProbeEveryNthCycle == 0)
                    {
                        try { await ProbePixeraConnectorsAsync(stoppingToken); }
                        catch (Exception ex) { _logger.LogError(ex, "DirectorMonitoringService: Pixera probe failed"); }
                    }
                }
            }

            try { await Task.Delay(CycleInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ── URL absence sweep ─────────────────────────────────────────────────────

    private async Task SweepUrlsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var monitored = await db.TblDirectorUrls
            .Where(u => u.IsMonitored)
            .ToListAsync(ct);
        if (monitored.Count == 0) return;

        var live = _heartbeats.Snapshot();
        var now = DateTime.UtcNow;
        bool inGrace = now - _startedUtc < MissingEvaluationGrace;

        foreach (var url in monitored)
        {
            var lastSeen = live.TryGetValue(url.MonitorId, out var mem) && mem > url.LastSeenUtc
                ? mem : url.LastSeenUtc;
            bool overdue = (now - lastSeen).TotalSeconds > Math.Max(60, url.ExpectedIntervalSeconds);

            if (overdue && url.AlertState == "online" && !inGrace)
            {
                url.AlertState = "missing";
                url.LastTransitionUtc = now;
                url.LastEditDate = now;
                await PublishAsync(new DirectorAlert(
                    DirectorAlertKind.UrlMissing, url.MonitorId,
                    $"URL '{url.MonitorId}' stopped polling — last seen {lastSeen:u} ({url.UrlKey})",
                    now, url.UrlKey, url.LocationId, url.LinkedDeviceId,
                    await ResolveDeviceNameAsync(db, url.LinkedDeviceId, ct)), ct);
            }
            else if (!overdue && url.AlertState == "missing")
            {
                url.AlertState = "online";
                url.LastTransitionUtc = now;
                url.LastEditDate = now;
                await PublishAsync(new DirectorAlert(
                    DirectorAlertKind.UrlRecovered, url.MonitorId,
                    $"URL '{url.MonitorId}' is polling again ({url.UrlKey})",
                    now, url.UrlKey, url.LocationId, url.LinkedDeviceId,
                    await ResolveDeviceNameAsync(db, url.LinkedDeviceId, ct)), ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<string?> ResolveDeviceNameAsync(IDirectorDBContext db, string? deviceId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;
        var device = await db.TblDevices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);
        return device?.Description;
    }

    // ── Pixera connector probing ──────────────────────────────────────────────

    private async Task ProbePixeraConnectorsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // Unlike the timeline watchdog, monitoring-only connectors are exactly in
        // scope here — health probing is monitoring.
        var connectors = await db.TblCloudPlatformConnectors
            .Where(c => c.IsEnabled && c.PlatformId == "pixera")
            .ToListAsync(ct);
        if (connectors.Count == 0) return;

        _probeCount++;

        foreach (var conn in connectors)
        {
            bool online = false;
            try
            {
                var config = _configProtector.Decrypt(conn.ConfigJson);
                if (string.IsNullOrEmpty(config?.BaseUrl)) continue;

                var rpcUrl = config.BaseUrl.TrimEnd('/') + "/jsonrpc";
                var client = new PixeraApiClient(rpcUrl, _httpClientFactory.CreateClient(), _logger);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(8));
                online = await client.GetApiVersionAsync(cts.Token);

                if (online)
                {
                    await StampDeviceHeartbeatsAsync(db, conn.ConnectorId, ct);
                    await LogSystemStatesAsync(client, conn, ct);
                    try { await HealStuckWebLayersAsync(client, conn, db, ct); }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Pixera web layer healing failed for {Connector}", conn.DisplayName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pixera probe error for connector {ConnectorId}", conn.ConnectorId);
            }

            var wasOnline = _connectorOnline.TryGetValue(conn.ConnectorId, out var prev) ? prev : (bool?)null;
            _connectorOnline[conn.ConnectorId] = online;

            if (wasOnline == true && !online)
            {
                await PublishAsync(new DirectorAlert(
                    DirectorAlertKind.DeviceProbeFailed, conn.DisplayName,
                    $"Pixera '{conn.DisplayName}' is not responding to API probes",
                    DateTime.UtcNow, DeviceId: $"cloud-{conn.ConnectorId}-", DeviceName: conn.DisplayName), ct);
            }
            else if (wasOnline == false && online)
            {
                await PublishAsync(new DirectorAlert(
                    DirectorAlertKind.DeviceProbeRecovered, conn.DisplayName,
                    $"Pixera '{conn.DisplayName}' is responding again",
                    DateTime.UtcNow, DeviceId: $"cloud-{conn.ConnectorId}-", DeviceName: conn.DisplayName), ct);
            }
        }
    }

    // A kicked layer needs time to boot its page and resume heartbeating before it
    // can fairly be considered stuck again; the cooldown also caps RPC churn when a
    // wall stays broken for reasons a re-navigation cannot fix.
    private static readonly TimeSpan WebLayerKickCooldown = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, DateTime> _lastWebLayerKick = new();

    /// <summary>
    /// Heartbeat-gated web layer healing. A Pixera web layer that navigated while
    /// this server was down sits on a browser error page forever — it never
    /// retries on its own. Monitors still in the missing state after the sweep's
    /// grace and overdue evaluation identify exactly those pages (anything alive
    /// resumes polling within one interval of recovery), so only their resources
    /// get a setUrl re-assertion. Live walls are never re-navigated.
    /// </summary>
    private async Task HealStuckWebLayersAsync(
        PixeraApiClient client, TblCloudPlatformConnector conn, IDirectorDBContext db, CancellationToken ct)
    {
        var missing = await db.TblDirectorUrls.AsNoTracking()
            .Where(u => u.IsMonitored && u.AlertState == "missing")
            .Select(u => u.MonitorId)
            .ToListAsync(ct);
        if (missing.Count == 0) return;
        var missingSet = missing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        var resources = await client.GetWebResourceUrlsAsync(cts.Token);

        var now = DateTime.UtcNow;
        foreach (var (handle, url) in resources)
        {
            var monitorId = PixeraWebLayerHealing.ExtractMonitorId(url);
            if (monitorId == null || !missingSet.Contains(monitorId)) continue;

            var kickKey = $"{conn.ConnectorId}:{monitorId}";
            if (_lastWebLayerKick.TryGetValue(kickKey, out var last) && now - last < WebLayerKickCooldown)
                continue;
            _lastWebLayerKick[kickKey] = now;

            await client.SetResourceUrlAsync(handle, url, cts.Token);
            _logger.LogWarning(
                "Re-asserted web layer URL for stalled monitor '{MonitorId}' on Pixera '{Connector}'",
                monitorId, conn.DisplayName);
        }
    }

    /// <summary>
    /// A successful probe stamps the synced Pixera devices' heartbeats so the
    /// Devices grid shows live check-ins like Engage players.
    /// </summary>
    private static async Task StampDeviceHeartbeatsAsync(IDirectorDBContext db, int connectorId, CancellationToken ct)
    {
        var prefix = $"cloud-{connectorId}-";
        var devices = await db.TblDevices
            .Where(d => d.DeviceId.StartsWith(prefix))
            .ToListAsync(ct);
        foreach (var d in devices) d.Heartbeat = DateTime.UtcNow;
        if (devices.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Tier-1 health: per-system states logged every probe; performance JSON
    /// captured periodically for empirical shape discovery (undocumented payload —
    /// no thresholds are built on it yet).
    /// </summary>
    private async Task LogSystemStatesAsync(PixeraApiClient client, TblCloudPlatformConnector conn, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var systems = await client.GetLiveSystemsAsync(cts.Token);
            foreach (var s in systems)
                _logger.LogInformation(
                    "Pixera[{Connector}] system '{Name}' ip={Ip} state={State}",
                    conn.DisplayName, s.Name, s.Ip ?? "-", s.State ?? "-");

            if (_probeCount % PerfCaptureEveryNthProbe == 0 && systems.Count > 0)
            {
                var perf = await client.GetPerformanceJsonAsync(systems[0].Name, cts.Token);
                if (!string.IsNullOrEmpty(perf))
                    _logger.LogInformation("Pixera[{Connector}] perf '{Name}': {Perf}",
                        conn.DisplayName, systems[0].Name,
                        perf.Length > 2000 ? perf[..2000] : perf);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pixera system-state read failed for {Connector}", conn.DisplayName);
        }
    }

    // ── Sink fan-out ──────────────────────────────────────────────────────────

    private async Task PublishAsync(DirectorAlert alert, CancellationToken ct)
    {
        _logger.LogWarning("Monitoring alert: {Kind} {Source} — {Message}", alert.Kind, alert.Source, alert.Message);
        foreach (var sink in _sinks)
        {
            try { await sink.PublishAsync(alert, ct); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alert sink {Sink} failed for {Kind} {Source}",
                    sink.GetType().Name, alert.Kind, alert.Source);
            }
        }
    }
}
