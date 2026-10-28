using System.Collections.Concurrent;
using System.Text.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Services.WallPlatforms;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

public class ClusterResolutionService : IClusterResolutionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserGroupLocationDefaultService _defaultService;

    // Cache key: "{tenantId}:{locationId}:{minuteBucket}"
    // Value: (expiry UTC, resolved clusters)
    private readonly ConcurrentDictionary<string, (DateTime Expiry, IReadOnlyList<ResolvedCluster> Clusters)> _cache = new();

    public ClusterResolutionService(
        IServiceScopeFactory scopeFactory,
        IUserGroupLocationDefaultService defaultService)
    {
        _scopeFactory = scopeFactory;
        _defaultService = defaultService;
    }

    public async Task<IReadOnlyList<ResolvedCluster>> ResolveClustersForLocationAsync(
        string tenantId, int locationId, DateTime atTime)
    {
        var minuteBucket = atTime.ToString("yyyyMMddHHmm");
        var cacheKey = $"{tenantId}:{locationId}:{minuteBucket}";

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            return cached.Clusters;

        var clusters = await ResolveAsync(locationId, atTime);

        var expiry = DateTime.UtcNow.AddMinutes(1);
        _cache[cacheKey] = (expiry, clusters);

        // Evict stale entries periodically (lazy GC — only when writing)
        if (_cache.Count > 200)
            PurgeExpired();

        return clusters;
    }

    public void InvalidateLocation(string tenantId, int locationId)
    {
        var prefix = $"{tenantId}:{locationId}:";
        foreach (var key in _cache.Keys.Where(k => k.StartsWith(prefix)).ToList())
            _cache.TryRemove(key, out _);
    }

    private async Task<IReadOnlyList<ResolvedCluster>> ResolveAsync(int locationId, DateTime atTime)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var location = await db.TblLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == locationId);

        if (location == null) return Array.Empty<ResolvedCluster>();

        VideoWallConfiguration? config = null;
        if (!string.IsNullOrEmpty(location.ExtendedData))
        {
            try
            {
                config = JsonSerializer.Deserialize<VideoWallConfiguration>(location.ExtendedData,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { }
        }

        if (config?.Layout?.Groups == null || config.Layout.Groups.Count == 0)
            return Array.Empty<ResolvedCluster>();

        // Load extended location data for GlobalConstraintId fallback
        int? globalConstraintId = null;
        if (!string.IsNullOrEmpty(location.ExtendedData))
        {
            try
            {
                var ext = JsonSerializer.Deserialize<LocationExtendedData>(location.ExtendedData,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                globalConstraintId = ext?.GlobalConstraintId;
            }
            catch { }
        }

        // Load all displays for this location once
        var displays = await db.TblDisplays.AsNoTracking()
            .Where(d => d.LocationId == locationId)
            .ToListAsync();

        // Load all active schedule events for displays in this location
        var displayIds = displays.Select(d => d.DisplayId).ToList();
        var activeEvents = await LoadActiveEventsAsync(db, displayIds, atTime);

        var result = new List<ResolvedCluster>();

        // Collect every per-group run first, then merge same-airline runs across groups.
        var assignedRuns   = new List<(PanoramaGroup Group, int Start, int End, int UserGroupId, int? OverrideConstraintId)>();
        var unassignedRuns = new List<(PanoramaGroup Group, int Start, int End)>();

        foreach (var group in config.Layout.Groups.Where(g => g.AllowAirlineAllocation))
        {
            foreach (var (startIdx, endIdx, userGroupId, overrideConstraintId, _) in BuildRuns(group, activeEvents))
            {
                if (userGroupId.HasValue)
                    assignedRuns.Add((group, startIdx, endIdx, userGroupId.Value, overrideConstraintId));
                else
                    unassignedRuns.Add((group, startIdx, endIdx));
            }
        }

        // Unassigned gaps remain per-group (no airline content, no transient location created).
        foreach (var (group, start, end) in unassignedRuns)
        {
            var runIds = group.DisplayIds.Skip(start).Take(end - start + 1).ToList();
            result.Add(new ResolvedCluster
            {
                ClusterKey = $"{group.GroupName}:{start}-{end}",
                GroupName  = group.GroupName,
                StartIndex = start,
                EndIndex   = end,
                Spans      = new List<ClusterSpan> { new(group.GroupName, start, end) }.AsReadOnly(),
                DisplayIds = runIds.AsReadOnly(),
                UserGroupId         = null,
                ResolvedConstraintId = globalConstraintId
            });
        }

        // Merge all runs for the same airline into one cluster so cross-group allocations
        // produce a single transient location rather than one per group.
        foreach (var airlineGroup in assignedRuns.GroupBy(r => r.UserGroupId))
        {
            var userGroupId  = airlineGroup.Key;
            var orderedRuns  = airlineGroup.ToList(); // preserves config group order

            var spans = orderedRuns
                .Select(r => new ClusterSpan(r.Group.GroupName, r.Start, r.End))
                .ToList();

            var allDisplayIds = orderedRuns
                .SelectMany(r => r.Group.DisplayIds.Skip(r.Start).Take(r.End - r.Start + 1))
                .ToList();

            var clusterKey = string.Join("|",
                spans.Select(s => $"{s.GroupName}:{s.StartIndex}-{s.EndIndex}"));

            var overrideConstraintId = orderedRuns
                .Select(r => r.OverrideConstraintId)
                .FirstOrDefault(id => id.HasValue);

            var defaults = await _defaultService.GetDefaultForGroupAsync(userGroupId, locationId, atTime);

            result.Add(new ResolvedCluster
            {
                ClusterKey           = clusterKey,
                GroupName            = spans.First().GroupName,
                StartIndex           = spans.First().StartIndex,
                EndIndex             = spans.Last().EndIndex,
                Spans                = spans.AsReadOnly(),
                DisplayIds           = allDisplayIds.AsReadOnly(),
                UserGroupId          = userGroupId,
                ResolvedConstraintId = overrideConstraintId ?? defaults.ConstraintId ?? globalConstraintId,
                BackgroundPath       = defaults.BackgroundPath,
                BackgroundMode       = defaults.BackgroundMode,
                MaskPath             = defaults.MaskPath,
                MaskMode             = defaults.MaskMode,
                LogoPath             = defaults.LogoPath,
                Logos                = defaults.Logos
            });
        }

        return result.AsReadOnly();
    }

    private static async Task<Dictionary<int, TblScheduleEvent?>> LoadActiveEventsAsync(
        IDirectorDBContext db, List<int> displayIds, DateTime atTime)
    {
        var currentTime = TimeOnly.FromDateTime(atTime);
        var currentDate = atTime;
        var currentDayOfWeek = atTime.DayOfWeek;

        // Load all airline schedule events for these displays
        var events = await db.TblScheduleEvents
            .AsNoTracking()
            .Where(e => displayIds.Contains(e.DisplayId) && e.UserGroupId != null)
            .Where(e =>
                (e.StartDate == null && e.EndDate == null) ||
                (e.StartDate != null && e.EndDate == null && e.StartDate <= currentDate) ||
                (e.StartDate == null && e.EndDate != null && e.EndDate >= currentDate) ||
                (e.StartDate != null && e.EndDate != null && e.StartDate <= currentDate && e.EndDate >= currentDate))
            .ToListAsync();

        // For each display pick the best matching event (most specific time range that covers atTime)
        var result = new Dictionary<int, TblScheduleEvent?>();
        foreach (var displayId in displayIds)
        {
            var displayEvents = events
                .Where(e => e.DisplayId == displayId && IsDayActive(e, currentDayOfWeek))
                .Where(e => IsTimeActive(e, currentTime))
                .OrderByDescending(e => e.StartTime.HasValue ? 1 : 0) // prefer time-specific events
                .ToList();

            result[displayId] = displayEvents.FirstOrDefault();
        }

        return result;
    }

    private static IEnumerable<(int Start, int End, int? UserGroupId, int? OverrideConstraintId, string? BgMode)>
        BuildRuns(Models.PanoramaGroup group, Dictionary<int, TblScheduleEvent?> activeEvents)
    {
        if (group.DisplayIds.Count == 0) yield break;

        int? currentGroupId = null;
        int? currentOverrideConstraintId = null;
        string? currentBgMode = null;
        int runStart = 0;

        for (int i = 0; i < group.DisplayIds.Count; i++)
        {
            var displayId = group.DisplayIds[i];
            activeEvents.TryGetValue(displayId, out var ev);
            var groupId = ev?.UserGroupId;
            var overrideConstraintId = ev?.OverrideConstraintId;

            var isNewRun = i == 0 || groupId != currentGroupId;

            if (isNewRun && i > 0)
            {
                yield return (runStart, i - 1, currentGroupId, currentOverrideConstraintId, currentBgMode);
                runStart = i;
            }

            currentGroupId = groupId;
            // First-event-wins for override within a run: later counters in the same run
            // do not overwrite a non-null override set by the first counter.
            if (i == runStart)
                currentOverrideConstraintId = overrideConstraintId;
            currentBgMode = null; // resolved by default service
        }

        yield return (runStart, group.DisplayIds.Count - 1, currentGroupId, currentOverrideConstraintId, currentBgMode);
    }

    private static bool IsDayActive(TblScheduleEvent e, DayOfWeek day)
    {
        bool anySet = e.Sunday || e.Monday || e.Tuesday || e.Wednesday || e.Thursday || e.Friday || e.Saturday;
        if (!anySet) return true;
        return day switch
        {
            DayOfWeek.Sunday => e.Sunday,
            DayOfWeek.Monday => e.Monday,
            DayOfWeek.Tuesday => e.Tuesday,
            DayOfWeek.Wednesday => e.Wednesday,
            DayOfWeek.Thursday => e.Thursday,
            DayOfWeek.Friday => e.Friday,
            DayOfWeek.Saturday => e.Saturday,
            _ => false
        };
    }

    private static bool IsTimeActive(TblScheduleEvent e, TimeOnly current)
        => new DailyWindow(e.StartTime, e.EndTime).Contains(current);

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _cache.Where(kv => kv.Value.Expiry <= now).Select(kv => kv.Key).ToList())
            _cache.TryRemove(key, out _);
    }
}
