using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services;

/// <summary>
/// Monitors airline allocation schedule blocks and applies StatusSelector content on transitions.
///
/// On block activation: if a DefaultAssetId is configured, writes it to the display's DefaultContent
/// event via SetActiveContentAsync — equivalent to an operator manually selecting that asset.
/// On block deactivation: clears the DefaultContent event so the display shows nothing.
///
/// Manual StatusSelector changes made while a block is active are preserved between cycles
/// because the service only acts on start/end transitions, not continuously.
///
/// State is tracked per display as the active event ID (not a simple bool) so that back-to-back
/// blocks — block A ends at the same instant block B starts — register as a genuine transition
/// (the active ID changes A→B) and B's DefaultAssetId is applied. A bool would see "active" both
/// before and after and skip the switch entirely.
/// </summary>
public class DefaultAssetActivatorService : BackgroundService
{
    private readonly DXSuite.Shared.Multitenancy.IWorkerScopeFactory _workerScopeFactory;
    private readonly ILogger<DefaultAssetActivatorService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    // Tracks the active block for each display on the previous cycle as
    // "{ScheduleEventId}:{UserGroupId}". The airline group is part of the key so an
    // in-place schedule edit that swaps the airline on the SAME event registers as a
    // transition — the event id alone would read as "no change" and the previous
    // airline's counter selection would survive the swap.
    // Missing key = first evaluation after startup; "0" = no block was active. Keyed by tenant too,
    // because display ids repeat across tenant databases.
    private readonly ConcurrentDictionary<(string Tenant, int DisplayId), string> _lastStateByDisplay = new();

    public DefaultAssetActivatorService(
        DXSuite.Shared.Multitenancy.IWorkerScopeFactory workerScopeFactory,
        ILogger<DefaultAssetActivatorService> logger,
        IDistributedLockService lockService)
    {
        _workerScopeFactory = workerScopeFactory;
        _logger = logger;
        _lockService = lockService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Default Asset Activator Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "default-asset-activator-lock",
                    TimeSpan.FromMinutes(5),
                    stoppingToken);

                if (distLock != null)
                {
                    // A tenant scope populates ITenantContext, which both the tenant's database and the
                    // invalidation of the display's cached publication depend on.
                    foreach (var tenant in _workerScopeFactory.WorkerTenants)
                    {
                        try
                        {
                            using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenant, stoppingToken);
                            var tenantContext = scope.ServiceProvider.GetRequiredService<DXSuite.Shared.Multitenancy.ITenantContext>();
                            if (tenantContext.DirectorConnectionString == null) continue;
                            await CheckAndApplyDefaultAssetsAsync(scope, tenant);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error checking default asset activations for tenant {Tenant}", tenant);
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("Another instance is handling default asset activation, skipping this cycle");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking default asset activations");
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        _logger.LogInformation("Default Asset Activator Service stopped.");
    }

    private async Task CheckAndApplyDefaultAssetsAsync(IServiceScope scope, string tenant)
    {
        var db             = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var statusSelector = scope.ServiceProvider.GetRequiredService<IStatusSelectorService>();

        var atTime = DateTime.Now;

        // Airline allocation events are created by the Ticket Counter Scheduler;
        // they always have UserGroupId set and DefaultContent = false.
        var events = await db.TblScheduleEvents.AsNoTracking()
            .Where(e => !e.DefaultContent && e.UserGroupId != null)
            .ToListAsync();

        // Group by display: a display may have several sequential airline blocks
        // (e.g., Airline A 06:00–10:00, Airline B 10:00–14:00). Only one can be
        // active at a time, and we track state at the display level so the two
        // events don't interfere with each other.
        var eventsByDisplay = events
            .GroupBy(e => e.DisplayId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (displayId, displayEvents) in eventsByDisplay)
        {
            try
            {
                // Find the event that is currently active for this display, if any.
                var activeEvent = displayEvents.FirstOrDefault(ev => IsEventActive(ev, atTime));
                var currentKey  = activeEvent == null
                    ? "0"
                    : $"{activeEvent.ScheduleEventId}:{activeEvent.UserGroupId}";

                bool hasPrevious = _lastStateByDisplay.TryGetValue((tenant, displayId), out var previousKey);

                // No change since last cycle (same block + same airline still active, or still
                // idle) — leave content as-is so manual StatusSelector changes are preserved.
                if (hasPrevious && previousKey == currentKey) continue;

                if (!hasPrevious)
                {
                    // First evaluation after startup — record state without forcing a transition.
                    _lastStateByDisplay[(tenant, displayId)] = currentKey;

                    if (activeEvent == null)
                    {
                        // No block active. Clear the DefaultContent event to remove any stale
                        // selection left from a missed deactivation (e.g., the service was down
                        // when a prior block ended).
                        _logger.LogDebug(
                            "Display {DisplayId} — no active airline block on startup, clearing stale content",
                            displayId);
                        await statusSelector.ClearActiveContentAsync(displayId, "System");
                    }
                    else if (activeEvent.DefaultAssetId.HasValue &&
                             await IsDefaultContentEmptyAsync(db, displayId))
                    {
                        // Block already running but the DefaultContent event is empty — covers a
                        // fresh install or a restart where the activation write never happened.
                        // If the event already has content, leave it (preserves a manual change).
                        _logger.LogInformation(
                            "Display {DisplayId} — active on startup with empty content, applying asset {AssetId}",
                            displayId, activeEvent.DefaultAssetId.Value);
                        await statusSelector.SetActiveContentAsync(
                            displayId, activeEvent.DefaultAssetId.Value, null, "System");
                    }
                    continue;
                }

                // Genuine transition: inactive→active, active→inactive, block-A→block-B, or an
                // in-place airline swap on the same block (detected via the group in the key).
                _lastStateByDisplay[(tenant, displayId)] = currentKey;

                if (activeEvent?.DefaultAssetId.HasValue == true)
                {
                    _logger.LogInformation(
                        "Display {DisplayId} transitioned to event {EventId} — setting default asset {AssetId}",
                        displayId, activeEvent.ScheduleEventId, activeEvent.DefaultAssetId.Value);
                    await statusSelector.SetActiveContentAsync(
                        displayId, activeEvent.DefaultAssetId.Value, null, "System");
                }
                else
                {
                    // Block ended with no successor, or the new block has no DefaultAssetId —
                    // clear so the counter goes blank everywhere (URL and views).
                    _logger.LogInformation(
                        "Display {DisplayId} — block ended or new block has no default asset, clearing content",
                        displayId);
                    await statusSelector.ClearActiveContentAsync(displayId, "System");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing default asset for display {DisplayId}", displayId);
            }
        }

        // Evict state entries for displays that no longer have any airline allocation events.
        var currentDisplayIds = eventsByDisplay.Keys.ToHashSet();
        foreach (var key in _lastStateByDisplay.Keys.Where(k => k.Tenant == tenant && !currentDisplayIds.Contains(k.DisplayId)).ToList())
            _lastStateByDisplay.TryRemove(key, out _);
    }

    private static bool IsEventActive(TblScheduleEvent ev, DateTime atTime)
    {
        // Check date range
        if (ev.StartDate.HasValue && atTime < ev.StartDate.Value) return false;
        if (ev.EndDate.HasValue   && atTime > ev.EndDate.Value)   return false;

        // Check day of week (if no days are set, treat as every day)
        bool anyDaySet = ev.Sunday || ev.Monday || ev.Tuesday || ev.Wednesday ||
                         ev.Thursday || ev.Friday || ev.Saturday;
        if (anyDaySet)
        {
            bool dayActive = atTime.DayOfWeek switch
            {
                DayOfWeek.Sunday    => ev.Sunday,
                DayOfWeek.Monday    => ev.Monday,
                DayOfWeek.Tuesday   => ev.Tuesday,
                DayOfWeek.Wednesday => ev.Wednesday,
                DayOfWeek.Thursday  => ev.Thursday,
                DayOfWeek.Friday    => ev.Friday,
                DayOfWeek.Saturday  => ev.Saturday,
                _                   => false
            };
            if (!dayActive) return false;
        }

        // Check time range (no times = active all day)
        if (ev.StartTime == null && ev.EndTime == null) return true;

        return ScheduleTime.IsWithinWindow(TimeOnly.FromDateTime(atTime), ev.StartTime, ev.EndTime);
    }

    /// <summary>
    /// Returns true when the display's DefaultContent event has no asset assigned. Used on startup
    /// to decide whether to (re-)apply the scheduled DefaultAssetId without clobbering a manual
    /// selection that survives across a restart. Emptiness matches PixeraController.ParseFirstAssetId:
    /// no Content array, or an empty one (airline-allocation writes "{}").
    /// </summary>
    private static async Task<bool> IsDefaultContentEmptyAsync(IDirectorDBContext db, int displayId)
    {
        var ev = await db.TblScheduleEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.DisplayId == displayId && e.DefaultContent);
        if (ev == null || string.IsNullOrWhiteSpace(ev.ContentAssignment)) return true;
        try
        {
            using var doc = JsonDocument.Parse(ev.ContentAssignment);
            return !(doc.RootElement.TryGetProperty("Content", out var content)
                     && content.ValueKind == JsonValueKind.Array
                     && content.GetArrayLength() > 0);
        }
        catch
        {
            return true;
        }
    }
}
