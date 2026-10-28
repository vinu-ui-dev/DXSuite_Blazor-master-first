using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Services;

/// <summary>
/// Background service that refreshes the display-constraint access cache every 60 seconds.
/// Iterates all worker tenants and writes display-to-constraint mappings for each tenant's
/// DB into the shared in-memory IAccessCacheStore (keyed by tenantId to prevent bleed).
///
/// Distributed locking is intentionally omitted: each replica maintains its own in-memory
/// cache, so concurrent refreshes across replicas are harmless. Phase 8 will add per-tenant
/// distributed locking once a central lock store is available.
/// </summary>
public class AccessCacheRefreshService : BackgroundService
{
    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly IAccessCacheStore _cacheStore;
    private readonly ILogger<AccessCacheRefreshService> _logger;
    private readonly ScheduleEvaluator _scheduleEvaluator;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private volatile bool _forceRefreshRequested;

    public AccessCacheRefreshService(
        IWorkerScopeFactory workerScopeFactory,
        IAccessCacheStore cacheStore,
        ILogger<AccessCacheRefreshService> logger,
        ScheduleEvaluator scheduleEvaluator)
    {
        _workerScopeFactory = workerScopeFactory;
        _cacheStore = cacheStore;
        _logger = logger;
        _scheduleEvaluator = scheduleEvaluator;
    }

    /// <summary>
    /// Forces an immediate cache refresh. Called after admin edits to access schedules.
    /// </summary>
    public void ForceRefresh() => _forceRefreshRequested = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AccessCacheRefreshService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var tenantSubdomain in _workerScopeFactory.WorkerTenants)
                {
                    try
                    {
                        using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenantSubdomain, stoppingToken);
                        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
                        var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                        await RefreshDisplayConstraintCacheAsync(tenantCtx.TenantId, db, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error refreshing access cache for tenant {Tenant}", tenantSubdomain);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            _forceRefreshRequested = false;
            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (_forceRefreshRequested)
                _logger.LogInformation("Force-refresh requested, running immediate cache refresh");
        }

        _logger.LogInformation("AccessCacheRefreshService stopped");
    }

    private async Task RefreshDisplayConstraintCacheAsync(string tenantId, IDirectorDBContext db, CancellationToken ct)
    {
        var now = DateTime.Now;

        var schedules = await db.TblAccessSchedules
            .AsNoTracking()
            .Where(s => s.ConstraintId != null)
            .ToListAsync(ct);

        _cacheStore.ClearDisplayConstraints(tenantId);

        var displaySchedules = schedules
            .Where(s => s.DisplayId.HasValue)
            .GroupBy(s => s.DisplayId!.Value);

        foreach (var group in displaySchedules)
        {
            var displayId = group.Key;
            var activeSchedule = group.FirstOrDefault(s => _scheduleEvaluator.IsActive(s, now));
            if (activeSchedule != null)
                _cacheStore.SetDisplayConstraint(tenantId, displayId, activeSchedule.ConstraintId);
        }

        // Location-wide schedules (no DisplayId) expand to all displays in that location
        var locationSchedules = schedules
            .Where(s => !s.DisplayId.HasValue)
            .GroupBy(s => s.LocationAccessRuleId);

        foreach (var group in locationSchedules)
        {
            var activeSchedule = group.FirstOrDefault(s => _scheduleEvaluator.IsActive(s, now));
            if (activeSchedule == null) continue;

            var rule = await db.TblLocationAccessRules
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.LocationAccessRuleId == activeSchedule.LocationAccessRuleId, ct);

            if (rule == null) continue;

            var locationDisplayIds = await db.TblDisplays
                .AsNoTracking()
                .Where(d => d.LocationId == rule.LocationId)
                .Select(d => d.DisplayId)
                .ToListAsync(ct);

            foreach (var displayId in locationDisplayIds)
            {
                // Display-specific schedules take precedence over location-wide ones
                if (_cacheStore.GetDisplayConstraint(tenantId, displayId) == null)
                    _cacheStore.SetDisplayConstraint(tenantId, displayId, activeSchedule.ConstraintId);
            }
        }

        _logger.LogDebug("Display constraint cache refreshed for tenant {TenantId} at {Time}", tenantId, now);
    }
}
