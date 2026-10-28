using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Shared.Multitenancy;
using System.Text.Json;

namespace DXSuite.Director.Services;

/// <summary>
/// Background service that maintains transient TblLocation rows for airline counter clusters.
/// Each minute: for every VideoWall location, resolves current clusters and syncs the
/// corresponding transient locations + their airline group access rules.
/// Uses IWorkerScopeFactory for proper per-tenant DB context isolation.
/// </summary>
public class ClusterLifecycleService : BackgroundService
{
    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly IClusterResolutionService _clusterResolution;
    private readonly IWallContentBroadcastService _wallBroadcast;
    private readonly ILogger<ClusterLifecycleService> _logger;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    // Last boundary announced per wall, so a boundary inside two overlapping
    // check windows is only broadcast once.
    private readonly Dictionary<(string TenantId, int LocationId), DateTime> _announcedBoundaries = new();

    public ClusterLifecycleService(
        IWorkerScopeFactory workerScopeFactory,
        IClusterResolutionService clusterResolution,
        IWallContentBroadcastService wallBroadcast,
        ILogger<ClusterLifecycleService> logger)
    {
        _workerScopeFactory = workerScopeFactory;
        _clusterResolution = clusterResolution;
        _wallBroadcast = wallBroadcast;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClusterLifecycleService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var subdomain in _workerScopeFactory.WorkerTenants)
            {
                try
                {
                    using var scope = await _workerScopeFactory.CreateTenantScopeAsync(subdomain, stoppingToken);
                    var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
                    var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                    await SyncTenantClustersAsync(tenantCtx.TenantId, db, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing clusters for tenant {Tenant}", subdomain);
                }
            }

            try { await Task.Delay(CheckInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task SyncTenantClustersAsync(string tenantId, IDirectorDBContext db, CancellationToken ct)
    {
        var videoWallTypeId = await db.TblLocationTypes
            .AsNoTracking()
            .Where(t => t.LocationTypeName == "VideoWall")
            .Select(t => t.LocationTypeId)
            .FirstOrDefaultAsync(ct);

        var clusterTypeId = await db.TblLocationTypes
            .AsNoTracking()
            .Where(t => t.LocationTypeName == "AirlineClusterView")
            .Select(t => t.LocationTypeId)
            .FirstOrDefaultAsync(ct);

        if (videoWallTypeId == 0 || clusterTypeId == 0) return;

        var videoWalls = await db.TblLocations
            .AsNoTracking()
            .Where(l => l.LocationTypeId == videoWallTypeId)
            .ToListAsync(ct);

        var now = DateTime.Now;

        foreach (var wall in videoWalls)
        {
            try
            {
                await SyncWallClustersAsync(tenantId, db, wall, clusterTypeId, now, ct);
                await AnnounceUpcomingBoundaryAsync(tenantId, db, wall, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing clusters for VideoWall {LocationId}", wall.LocationId);
            }
        }
    }

    /// <summary>
    /// Broadcasts the next scheduled content boundary for a wall when it falls inside
    /// the coming check window, carrying its exact activation instant so wall surfaces
    /// flip together at the boundary instead of on their own polling phase. Boundary
    /// candidates come from counter schedule-event windows and airline dayparting
    /// state schedules; a boundary that turns out not to change anything just causes
    /// a harmless refresh.
    /// </summary>
    private async Task AnnounceUpcomingBoundaryAsync(
        string tenantId, IDirectorDBContext db, TblLocation wall, DateTime now, CancellationToken ct)
    {
        var boundaryTimes = new List<TimeOnly>();

        var displayIds = db.TblDisplays.Where(d => d.LocationId == wall.LocationId).Select(d => d.DisplayId);
        var eventWindows = await db.TblScheduleEvents.AsNoTracking()
            .Where(e => displayIds.Contains(e.DisplayId) && !e.DefaultContent)
            .Select(e => new { e.StartTime, e.EndTime })
            .ToListAsync(ct);
        foreach (var w in eventWindows)
        {
            if (w.StartTime != null) boundaryTimes.Add(w.StartTime.Value);
            // Windows end inclusively, so content changes the minute after EndTime
            if (w.EndTime != null) boundaryTimes.Add(w.EndTime.Value.AddMinutes(1));
        }

        var stateSchedules = await db.TblUserGroupLocationDefaults.AsNoTracking()
            .Where(d => d.LocationId == wall.LocationId && d.StateScheduleJson != null)
            .Select(d => d.StateScheduleJson)
            .ToListAsync(ct);
        foreach (var json in stateSchedules)
        {
            var schedule = AirlineStateResolver.Parse(json);
            if (schedule == null) continue;
            if (TimeOnly.TryParse(schedule.StartTime, out var start)) boundaryTimes.Add(start);
            if (TimeOnly.TryParse(schedule.EndTime, out var end)) boundaryTimes.Add(end.AddMinutes(1));
        }

        if (boundaryTimes.Count == 0) return;

        var nextBoundary = boundaryTimes
            .Select(t =>
            {
                var candidate = now.Date.Add(t.ToTimeSpan());
                return candidate > now ? candidate : candidate.AddDays(1);
            })
            .Min();

        if (nextBoundary > now + CheckInterval + TimeSpan.FromSeconds(15)) return;

        var key = (tenantId, wall.LocationId);
        if (_announcedBoundaries.TryGetValue(key, out var last) && last == nextBoundary) return;
        _announcedBoundaries[key] = nextBoundary;

        await _wallBroadcast.PublishAsync(tenantId, wall.LocationId,
            new WallContentChange(nextBoundary.ToUniversalTime()));
    }

    private async Task SyncWallClustersAsync(
        string tenantId, IDirectorDBContext db, TblLocation wall,
        int clusterTypeId, DateTime now, CancellationToken ct)
    {
        var clusters = await _clusterResolution.ResolveClustersForLocationAsync(tenantId, wall.LocationId, now);
        var assignedClusters = clusters.Where(c => c.UserGroupId.HasValue).ToList();

        // Load existing transient locations for this wall
        var existing = await db.TblLocations
            .Where(l => l.IsTransientCluster && l.ParentVideoWallLocationId == wall.LocationId)
            .ToListAsync(ct);

        var existingByKey = existing.ToDictionary(l => l.ClusterKey ?? "");
        var resolvedKeys = assignedClusters.Select(c => c.ClusterKey).ToHashSet();

        // Create or update transient locations for active clusters
        foreach (var cluster in assignedClusters)
        {
            if (existingByKey.TryGetValue(cluster.ClusterKey, out var loc))
            {
                // Update if airline changed — revoke previous airline's access before granting new one.
                if (loc.OwningUserGroupId != cluster.UserGroupId)
                {
                    if (loc.OwningUserGroupId.HasValue)
                    {
                        var oldRule = await db.TblLocationAccessRules
                            .FirstOrDefaultAsync(r => r.LocationId == loc.LocationId
                                && r.UserGroupId == loc.OwningUserGroupId, ct);
                        if (oldRule != null)
                            db.TblLocationAccessRules.Remove(oldRule);
                    }

                    loc.OwningUserGroupId = cluster.UserGroupId;
                    await EnsureAccessRuleAsync(db, loc.LocationId, cluster.UserGroupId!.Value, ct);
                }
            }
            else
            {
                // Name uses globally sequential counter numbers so end-users see
                // a single contiguous range regardless of how many layout groups the
                // cluster spans (counter rows exist only to model varying spacing).
                var (globalStart, globalEnd) = GetGlobalCounterRange(wall, cluster.DisplayIds);
                string clusterName = globalStart == globalEnd
                    ? $"{wall.LocationName} — Counter {globalStart}"
                    : $"{wall.LocationName} — Counters {globalStart}–{globalEnd}";

                // varchar(50) hard limit on LocationName
                if (clusterName.Length > 50)
                    clusterName = clusterName[..47] + "…";
                var newLoc = new TblLocation
                {
                    LocationName = clusterName,
                    LocationTypeId = clusterTypeId,
                    LocationGroupId = wall.LocationGroupId,
                    SortOrder = 99,
                    CreateDate = DateTime.UtcNow,
                    IsTransientCluster = true,
                    ParentVideoWallLocationId = wall.LocationId,
                    ClusterKey = cluster.ClusterKey,
                    OwningUserGroupId = cluster.UserGroupId
                };
                db.TblLocations.Add(newLoc);
                await db.SaveChangesAsync();

                await EnsureAccessRuleAsync(db, newLoc.LocationId, cluster.UserGroupId!.Value, ct);

                _logger.LogInformation("Created transient cluster location {Name} for group {GroupId}",
                    clusterName, cluster.UserGroupId);
            }
        }

        // Remove transient locations whose clusters no longer have an assignment
        foreach (var stale in existing.Where(l => !resolvedKeys.Contains(l.ClusterKey ?? "")))
        {
            // Remove access rules first, then the location
            var rules = await db.TblLocationAccessRules
                .Where(r => r.LocationId == stale.LocationId)
                .ToListAsync(ct);
            db.TblLocationAccessRules.RemoveRange(rules);
            db.TblLocations.Remove(stale);

            _logger.LogInformation("Removed stale transient cluster location {LocationId} ({ClusterKey})",
                stale.LocationId, stale.ClusterKey);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Returns the 1-based global counter range for a cluster by mapping the cluster's
    /// display IDs onto the ordered flat list of all AllowAirlineAllocation display IDs
    /// in wall layout order. Falls back to (1, clusterDisplayIds.Count) when the config
    /// cannot be parsed (e.g., missing ExtendedData on a legacy record).
    /// </summary>
    private static (int GlobalStart, int GlobalEnd) GetGlobalCounterRange(
        TblLocation wall, IReadOnlyList<int> clusterDisplayIds)
    {
        if (string.IsNullOrEmpty(wall.ExtendedData) || clusterDisplayIds.Count == 0)
            return (1, clusterDisplayIds.Count);

        VideoWallConfiguration? config = null;
        try
        {
            config = JsonSerializer.Deserialize<VideoWallConfiguration>(wall.ExtendedData,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { }

        if (config?.Layout?.Groups == null)
            return (1, clusterDisplayIds.Count);

        // Flatten all counter display IDs in config group order to get a stable global index.
        var allCounterIds = config.Layout.Groups
            .Where(g => g.AllowAirlineAllocation)
            .SelectMany(g => g.DisplayIds)
            .ToList();

        if (allCounterIds.Count == 0)
            return (1, clusterDisplayIds.Count);

        var clusterSet = clusterDisplayIds.ToHashSet();
        int globalMin = int.MaxValue;
        int globalMax = int.MinValue;

        for (int i = 0; i < allCounterIds.Count; i++)
        {
            if (!clusterSet.Contains(allCounterIds[i])) continue;
            int pos = i + 1; // 1-based
            if (pos < globalMin) globalMin = pos;
            if (pos > globalMax) globalMax = pos;
        }

        return globalMin == int.MaxValue
            ? (1, clusterDisplayIds.Count)
            : (globalMin, globalMax);
    }

    private static async Task EnsureAccessRuleAsync(
        IDirectorDBContext db, int locationId, int userGroupId, CancellationToken ct)
    {
        var exists = await db.TblLocationAccessRules
            .AnyAsync(r => r.LocationId == locationId && r.UserGroupId == userGroupId, ct);

        if (!exists)
        {
            db.TblLocationAccessRules.Add(new TblLocationAccessRule
            {
                LocationId = locationId,
                UserGroupId = userGroupId,
                AccessLevel = "full",
                CreateDate = DateTime.UtcNow
            });
        }
    }
}
