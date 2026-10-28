using DXSuite.Director.Entities;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Pulse;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DXSuite.Sentinel.Services.Engine;

/// <summary>
/// Publishes the health of Pulse-linked Sentinel devices into each tenant's Pulse database. A state
/// reconciler: every cycle re-derives current truth through <see cref="PulsePublishPlanner"/> and writes
/// only what differs from what was last published. Nothing is queued or retried across cycles, so a
/// failed write is simply re-derived on the next one. See docs/spec/sentinel-pulse-reporting.md section 4.
/// Runs apart from the evaluator cycle so a bad remediation run never stops heartbeats.
/// </summary>
public sealed class PulsePublisherEngine : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultCycle = TimeSpan.FromSeconds(60);
    private const string LockName = "sentinel-pulse-publisher-lock";

    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly PulseReportingSettingsResolver _settings;
    private readonly IDistributedLockService _lockService;
    private readonly ISentinelAvailabilityService _sentinelAvailability;
    private readonly ILogger<PulsePublisherEngine> _logger;

    // Tenants may set different CycleSeconds; the loop ticks at the shortest and skips tenants not yet due.
    private readonly Dictionary<string, DateTime> _lastRunUtc = new(StringComparer.OrdinalIgnoreCase);

    public PulsePublisherEngine(
        IWorkerScopeFactory workerScopeFactory,
        PulseReportingSettingsResolver settings,
        IDistributedLockService lockService,
        ISentinelAvailabilityService sentinelAvailability,
        ILogger<PulsePublisherEngine> logger)
    {
        _workerScopeFactory = workerScopeFactory;
        _settings = settings;
        _lockService = lockService;
        _sentinelAvailability = sentinelAvailability;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        // After the delay because the SQL tenant store loads its tenants in its own StartAsync.
        // An enabled tenant with a blank CustomerId would write readings Pulse never shows, so
        // the resolver's exception is allowed to stop the host.
        var cycle = ValidateTenants();
        _logger.LogInformation("PulsePublisherEngine starting, cycle {Cycle}", cycle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_sentinelAvailability.IsAvailable)
                {
                    using var distLock = await _lockService.TryAcquireLockAsync(LockName, TimeSpan.FromMinutes(5), stoppingToken);
                    if (distLock != null)
                        await RunCycleAsync(stoppingToken);
                    else
                        _logger.LogDebug("Another instance is running the Pulse publisher cycle, skipping");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PulsePublisherEngine cycle");
            }

            try { await Task.Delay(cycle, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("PulsePublisherEngine stopped");
    }

    private TimeSpan ValidateTenants()
    {
        var cycles = _workerScopeFactory.WorkerTenants
            .Select(t => _settings.Resolve(t).Settings)
            .Where(s => s.Enabled)
            .Select(s => TimeSpan.FromSeconds(s.CycleSeconds))
            .ToList();
        return cycles.Count == 0 ? DefaultCycle : cycles.Min();
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        // Sequential: each tenant takes its own stamp, which keeps tblPulse key collisions rare.
        foreach (var subdomain in _workerScopeFactory.WorkerTenants)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var resolved = _settings.Resolve(subdomain);
                if (!resolved.Settings.Enabled) continue;

                var now = DateTime.UtcNow;
                if (_lastRunUtc.TryGetValue(subdomain, out var last)
                    && now - last < TimeSpan.FromSeconds(resolved.Settings.CycleSeconds) - TimeSpan.FromSeconds(1))
                    continue;
                _lastRunUtc[subdomain] = now;

                using var scope = await _workerScopeFactory.CreateTenantScopeAsync(subdomain, ct);
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                if (tenant.SentinelConnectionString == null) continue;

                await PublishTenantAsync(scope.ServiceProvider, tenant, resolved.Settings, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing Pulse health for tenant {Tenant}", subdomain);
            }
        }
    }

    private async Task PublishTenantAsync(
        IServiceProvider services, ITenantContext tenant, PulseReportingSettings settings, CancellationToken ct)
    {
        var nowUtc = TruncateToSecond(DateTime.UtcNow);
        var db = services.GetRequiredService<ISentinelDBContext>();

        var devices = await db.DeviceInstances.AsNoTracking()
            .Where(d => d.PulseDeviceId != null && d.IsEnabled)
            .Select(d => new { d.DeviceInstanceId, d.PulseDeviceId })
            .ToListAsync(ct);

        // An unlinked device causes no Pulse I/O at all, and a tenant with none causes none.
        if (devices.Count == 0) return;

        var sources = (await db.DeviceSourceStates.AsNoTracking()
                .Where(s => s.DeviceInstance.PulseDeviceId != null && s.DeviceInstance.IsEnabled)
                .ToListAsync(ct))
            .ToLookup(s => s.DeviceInstanceId);
        var deviceStates = (await db.SentinelPulseDeviceStates
                .Where(s => db.DeviceInstances.Any(d => d.DeviceInstanceId == s.DeviceInstanceId && d.PulseDeviceId != null && d.IsEnabled))
                .ToListAsync(ct))
            .ToDictionary(s => s.DeviceInstanceId);
        var itemStates = (await db.SentinelPulseItemStates
                .Where(s => db.DeviceInstances.Any(d => d.DeviceInstanceId == s.DeviceInstanceId && d.PulseDeviceId != null && d.IsEnabled))
                .ToListAsync(ct))
            .ToLookup(s => s.DeviceInstanceId);

        var overrides = await services.GetRequiredService<PulseIssueLevelService>().LoadAsync(ct);
        var gateways = await db.SiteGateways.AsNoTracking()
            .ToDictionaryAsync(g => g.GatewayId, g => g.SiteName, ct);

        var (urlMonitors, connectors) = await LoadDirectorInputsAsync(
            services, tenant, devices.Select(d => d.DeviceInstanceId).ToList(), ct);

        var names = new PulseSourceNames(connectors, gateways);
        var pulse = services.GetRequiredService<PulseGateway>();
        var reassert = TimeSpan.FromHours(settings.ReassertHours);

        foreach (var device in devices)
        {
            if (ct.IsCancellationRequested) break;

            var plan = PulsePublishPlanner.Plan(new PulsePlanInput
            {
                DeviceInstanceId = device.DeviceInstanceId,
                PulseDeviceId = device.PulseDeviceId!,
                PulseCustomerId = settings.CustomerId,
                Sources = sources[device.DeviceInstanceId].ToList(),
                UrlMonitors = urlMonitors[device.DeviceInstanceId].ToList(),
                DeviceState = deviceStates.GetValueOrDefault(device.DeviceInstanceId),
                ItemStates = itemStates[device.DeviceInstanceId].ToList(),
                Overrides = overrides,
                Names = names,
                ReassertInterval = reassert,
                NowUtc = nowUtc,
            });

            // Attempts stop until the operator retries or relinks; the row is left exactly as it was.
            if (plan.LinkBroken) continue;

            await PublishDeviceAsync(db, pulse, device.DeviceInstanceId, device.PulseDeviceId!, plan,
                deviceStates.GetValueOrDefault(device.DeviceInstanceId),
                itemStates[device.DeviceInstanceId].ToDictionary(s => s.MonitorItemId),
                nowUtc, ct);
        }
    }

    private async Task PublishDeviceAsync(
        ISentinelDBContext db,
        PulseGateway pulse,
        int deviceInstanceId,
        string pulseDeviceId,
        PulsePlan plan,
        SentinelPulseDeviceState? stored,
        Dictionary<int, SentinelPulseItemState> storedItems,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (plan.Relinked)
        {
            db.SentinelPulseItemStates.RemoveRange(storedItems.Values);
            storedItems.Clear();
            await db.SaveChangesAsync(ct);
        }

        DateTime? heartbeatWritten = null;
        string? error = null;
        var linkBroken = false;
        var stop = false;

        // Heartbeat before readings: a Critical reading on a device Pulse already thinks is down reads as two faults.
        if (plan.HeartbeatUtc is { } heartbeat)
        {
            var result = await pulse.WriteHeartbeatAsync(pulseDeviceId, heartbeat, ct);
            if (result.IsSuccess) heartbeatWritten = heartbeat;
            else (error, linkBroken, stop) = Classify(result);
        }

        foreach (var reading in plan.Readings)
        {
            if (stop) break;

            var result = await pulse.InsertReadingAsync(reading.ToPulseReading(pulseDeviceId, nowUtc), ct);

            // The Pulse Reporting tab validates items on save, so this is a backstop for an item deleted
            // from Pulse since. Item state stays keyed on the configured item so the next cycle is quiet.
            if (result.Outcome == PulseOutcome.MonitorItemMissing && reading.MonitorItemId != PulseIssueCatalogue.DefaultMonitorItemId)
            {
                _logger.LogWarning(
                    "Pulse monitor item {ItemId} does not exist; writing {Codes} for device {DeviceId} at item {DefaultItem} instead",
                    reading.MonitorItemId, reading.OpenIssueCodes, pulseDeviceId, PulseIssueCatalogue.DefaultMonitorItemId);
                result = await pulse.InsertReadingAsync(
                    reading.ToPulseReading(pulseDeviceId, nowUtc) with { MonitorItemId = PulseIssueCatalogue.DefaultMonitorItemId }, ct);
            }

            if (result.IsSuccess)
                UpsertItemState(db, storedItems, reading.ToItemState(deviceInstanceId, nowUtc));
            else
            {
                var (readingError, broken, halt) = Classify(result);
                error ??= readingError;
                linkBroken |= broken;
                stop = halt;
            }
        }

        var state = stored ?? db.SentinelPulseDeviceStates.Add(new SentinelPulseDeviceState { DeviceInstanceId = deviceInstanceId }).Entity;
        CopyPlanned(plan.DeviceState!, state);
        PulsePublishPlanner.RecordOutcome(state, nowUtc, heartbeatWritten, error, linkBroken);

        if (linkBroken)
            _logger.LogWarning("Pulse device {DeviceId} is missing; device {DeviceInstanceId} is marked link broken", pulseDeviceId, deviceInstanceId);
        else if (error is not null)
            _logger.LogWarning("Pulse publish for device {DeviceInstanceId} failed: {Error}", deviceInstanceId, error);

        // Saved per device so a later failure in the tenant cannot discard state for writes that landed.
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Maps a failed gateway call to the recorded error, whether the link is dangling, and whether the rest
    /// of the device's writes are pointless this cycle.
    /// </summary>
    private static (string Error, bool LinkBroken, bool Stop) Classify(PulseResult result) => result.Outcome switch
    {
        PulseOutcome.DeviceMissing => (result.Error ?? "Pulse device is missing.", true, true),
        PulseOutcome.Unreachable or PulseOutcome.NotConfigured => (result.Error ?? result.Outcome.ToString(), false, true),
        _ => (result.Error ?? result.Outcome.ToString(), false, false),
    };

    private static void UpsertItemState(ISentinelDBContext db, Dictionary<int, SentinelPulseItemState> storedItems, SentinelPulseItemState next)
    {
        if (storedItems.TryGetValue(next.MonitorItemId, out var row))
        {
            row.ErrorLevel = next.ErrorLevel;
            row.OpenIssueCodes = next.OpenIssueCodes;
            row.Summary = next.Summary;
            row.LastPublishedUtc = next.LastPublishedUtc;
        }
        else
        {
            db.SentinelPulseItemStates.Add(next);
            storedItems[next.MonitorItemId] = next;
        }
    }

    private static void CopyPlanned(SentinelPulseDeviceState from, SentinelPulseDeviceState to)
    {
        to.PulseCustomerId = from.PulseCustomerId;
        to.PulseDeviceId = from.PulseDeviceId;
        to.LastHeartbeatSentUtc = from.LastHeartbeatSentUtc;
        to.LastAttemptUtc = from.LastAttemptUtc;
        to.LastSuccessUtc = from.LastSuccessUtc;
        to.ConsecutiveFailures = from.ConsecutiveFailures;
        to.SuppressionReason = from.SuppressionReason;
        to.LastError = from.LastError;
        to.LinkBrokenUtc = from.LinkBrokenUtc;
    }

    /// <summary>
    /// URL monitors attached to the linked devices, and connector names for Source offline summaries. Both
    /// live in the tenant's Director database; a tenant without Director simply has neither.
    /// </summary>
    private static async Task<(ILookup<int, PulseUrlMonitor> Urls, IReadOnlyDictionary<int, PulseConnectorName> Connectors)>
        LoadDirectorInputsAsync(IServiceProvider services, ITenantContext tenant, List<int> deviceIds, CancellationToken ct)
    {
        if (tenant.DirectorConnectionString == null)
            return (Array.Empty<PulseUrlMonitor>().ToLookup(_ => 0), new Dictionary<int, PulseConnectorName>());

        var director = services.GetRequiredService<IDirectorDBContext>();

        var urls = await director.TblDirectorUrls.AsNoTracking()
            .Where(u => u.DeviceInstanceId != null && deviceIds.Contains(u.DeviceInstanceId.Value))
            .Select(u => new { DeviceInstanceId = u.DeviceInstanceId!.Value, u.MonitorId, u.UrlKey, u.IsMonitored, u.AlertState, u.LastTransitionUtc })
            .ToListAsync(ct);

        var registry = services.GetRequiredService<ICloudPlatformConnectorRegistry>();
        var connectors = (await director.TblCloudPlatformConnectors.AsNoTracking()
                .Select(c => new { c.ConnectorId, c.PlatformId, c.DisplayName })
                .ToListAsync(ct))
            .ToDictionary(
                c => c.ConnectorId,
                c => new PulseConnectorName(registry.GetConnector(c.PlatformId)?.DisplayName ?? c.PlatformId, c.DisplayName));

        return (
            urls.ToLookup(u => u.DeviceInstanceId, u => new PulseUrlMonitor(u.MonitorId, u.UrlKey, u.IsMonitored, u.AlertState, u.LastTransitionUtc)),
            connectors);
    }

    private static DateTime TruncateToSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
