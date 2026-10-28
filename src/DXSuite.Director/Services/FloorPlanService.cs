using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Shared.Entities.Identity;
using static DXSuite.Director.Services.ExtendedDataHelper;

namespace DXSuite.Director.Services;

public class FloorPlanService : IFloorPlanService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScheduleEvaluator _scheduleEvaluator;
    private readonly ILogger<FloorPlanService> _logger;

    public FloorPlanService(
        IServiceScopeFactory scopeFactory,
        ScheduleEvaluator scheduleEvaluator,
        ILogger<FloorPlanService> logger)
    {
        _scopeFactory = scopeFactory;
        _scheduleEvaluator = scheduleEvaluator;
        _logger = logger;
    }

    public async Task<List<TblFloorPlanElement>> GetElementsForLocationAsync(int locationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblFloorPlanElements
            .Include(e => e.ChildLocation)
            .Include(e => e.ChildDisplay)
                .ThenInclude(d => d.OverrideAsset)
            .Include(e => e.ChildDisplay)
                .ThenInclude(d => d.TblScheduleEvents)
            .Where(e => e.LocationId == locationId)
            .OrderBy(e => e.SortOrder)
            .ToListAsync();
    }

    public async Task<TblFloorPlanElement> CreateElementAsync(TblFloorPlanElement element)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        element.CreateDate = DateTime.UtcNow;
        db.TblFloorPlanElements.Add(element);
        await db.SaveChangesAsync();

        return element;
    }

    public async Task UpdateElementAsync(TblFloorPlanElement element)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var existing = await db.TblFloorPlanElements.FindAsync(element.FloorPlanElementId);
        if (existing == null) throw new InvalidOperationException($"Element {element.FloorPlanElementId} not found.");

        existing.ChildLocationId = element.ChildLocationId;
        existing.ChildDisplayId = element.ChildDisplayId;
        existing.PositionTop = element.PositionTop;
        existing.PositionLeft = element.PositionLeft;
        existing.ElementWidth = element.ElementWidth;
        existing.ElementHeight = element.ElementHeight;
        existing.IconPath = element.IconPath;
        existing.IconLabel = element.IconLabel;
        existing.ShowLivePreview = element.ShowLivePreview;
        existing.SortOrder = element.SortOrder;
        existing.LastEditDate = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task UpdateElementPositionAsync(int elementId, double top, double left, double width, double height)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var element = await db.TblFloorPlanElements.FindAsync(elementId);
        if (element == null) return;

        element.PositionTop = top;
        element.PositionLeft = left;
        element.ElementWidth = width;
        element.ElementHeight = height;
        element.LastEditDate = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task DeleteElementAsync(int elementId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var element = await db.TblFloorPlanElements.FindAsync(elementId);
        if (element != null)
        {
            db.TblFloorPlanElements.Remove(element);
            await db.SaveChangesAsync();
        }
    }

    public async Task<Dictionary<int, string>> GetActiveIconsAsync(int locationId, DateTime now)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var elements = await db.TblFloorPlanElements
            .Include(e => e.IconSchedules)
            .Where(e => e.LocationId == locationId)
            .ToListAsync();

        var result = new Dictionary<int, string>();

        // Phase 1: Group-derived icons from access rule schedules
        var iconLibraryId = await GetIconLibraryLocationIdAsync(db, locationId);
        if (iconLibraryId.HasValue)
        {
            var groupIcons = await ResolveGroupDerivedIconsAsync(db, elements, iconLibraryId.Value, now);
            foreach (var kvp in groupIcons)
                result[kvp.Key] = kvp.Value;
        }

        // Phase 2: Manual icon schedules (fill in elements not resolved above)
        foreach (var element in elements)
        {
            if (result.ContainsKey(element.FloorPlanElementId))
                continue;

            if (element.IconSchedules == null || element.IconSchedules.Count == 0)
                continue;

            // Active schedule with lowest SortOrder wins
            var activeSchedule = element.IconSchedules
                .Where(s => _scheduleEvaluator.IsActive(s, now))
                .OrderBy(s => s.SortOrder)
                .FirstOrDefault();

            if (activeSchedule == null)
                continue;

            // Resolve icon path
            string? iconPath = null;
            if (activeSchedule.IconAssetId.HasValue)
            {
                var asset = await db.TblAssets.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.AssetId == activeSchedule.IconAssetId.Value);
                iconPath = asset?.AssetPath;
            }
            else if (activeSchedule.AssetLibraryLocationId.HasValue &&
                     !string.IsNullOrEmpty(activeSchedule.MetadataKey) &&
                     !string.IsNullOrEmpty(activeSchedule.MetadataValue))
            {
                var matchingAssetId = await db.TblAssetContentMetadata
                    .Where(m => m.MetadataKey == activeSchedule.MetadataKey && m.MetadataValue == activeSchedule.MetadataValue)
                    .Join(db.TblAssets.Where(a => a.LocationId == activeSchedule.AssetLibraryLocationId.Value),
                        m => m.AssetId, a => a.AssetId, (m, a) => a.AssetPath)
                    .FirstOrDefaultAsync();
                iconPath = matchingAssetId;
            }

            if (!string.IsNullOrEmpty(iconPath))
            {
                result[element.FloorPlanElementId] = iconPath;
            }
        }

        return result;
    }

    private static async Task<int?> GetIconLibraryLocationIdAsync(IDirectorDBContext db, int locationId)
    {
        var location = await db.TblLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == locationId);
        if (location == null) return null;

        return GetProperty<LocationExtendedData, int?>(location.ExtendedData, ed => ed.IconLibraryLocationId);
    }

    private async Task<Dictionary<int, string>> ResolveGroupDerivedIconsAsync(
        IDirectorDBContext db,
        List<TblFloorPlanElement> elements,
        int iconLibraryLocationId,
        DateTime now)
    {
        var result = new Dictionary<int, string>();

        // Collect all target location IDs we need access rules for
        var targetLocationIds = new HashSet<int>();
        var displayLocationMap = new Dictionary<int, int>(); // displayId -> locationId

        foreach (var el in elements)
        {
            if (el.ChildLocationId.HasValue)
                targetLocationIds.Add(el.ChildLocationId.Value);
            else if (el.ChildDisplayId.HasValue)
            {
                var display = await db.TblDisplays.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.DisplayId == el.ChildDisplayId.Value);
                if (display != null)
                {
                    targetLocationIds.Add(display.LocationId);
                    displayLocationMap[display.DisplayId] = display.LocationId;
                }
            }
        }

        if (targetLocationIds.Count == 0) return result;

        // Batch-load all group-based access rules for target locations
        var accessRules = await db.TblLocationAccessRules
            .Include(r => r.AccessSchedules)
            .Include(r => r.UserGroup)
            .Where(r => targetLocationIds.Contains(r.LocationId) && r.UserGroupId != null)
            .AsNoTracking()
            .ToListAsync();

        // Pre-load icon library assets tagged with "Group" metadata
        var iconAssets = await db.TblAssetContentMetadata
            .Where(m => m.MetadataKey == "Group")
            .Join(db.TblAssets.Where(a => a.LocationId == iconLibraryLocationId && !a.IsDeleted),
                m => m.AssetId, a => a.AssetId, (m, a) => new { m.MetadataValue, a.AssetPath })
            .ToListAsync();

        var iconByGroup = iconAssets
            .GroupBy(x => x.MetadataValue)
            .ToDictionary(g => g.Key, g => g.First().AssetPath);

        foreach (var el in elements)
        {
            int? targetLocId = null;
            int? displayId = null;

            if (el.ChildLocationId.HasValue)
                targetLocId = el.ChildLocationId.Value;
            else if (el.ChildDisplayId.HasValue)
            {
                displayId = el.ChildDisplayId.Value;
                displayLocationMap.TryGetValue(displayId.Value, out var locId);
                targetLocId = locId > 0 ? locId : null;
            }

            if (!targetLocId.HasValue) continue;

            var rulesForLocation = accessRules.Where(r => r.LocationId == targetLocId.Value).ToList();

            string? activeGroupName = null;

            if (displayId.HasValue)
            {
                // Display-specific: check rules with display-scoped active schedules first
                activeGroupName = FindActiveGroupForDisplay(rulesForLocation, displayId.Value, now);
            }

            // Fall back to location-wide rules
            activeGroupName ??= FindActiveGroupForLocation(rulesForLocation, now);

            // Look up icon by group name, or __default__ when no group is active
            if (activeGroupName != null && iconByGroup.TryGetValue(activeGroupName, out var path))
                result[el.FloorPlanElementId] = path;
            else if (iconByGroup.TryGetValue("__default__", out var defaultPath))
                result[el.FloorPlanElementId] = defaultPath;
        }

        return result;
    }

    /// <summary>
    /// Find the active group from rules that have display-specific schedules matching the given display.
    /// </summary>
    private string? FindActiveGroupForDisplay(List<TblLocationAccessRule> rules, int displayId, DateTime now)
    {
        var activeGroups = new List<string>();

        foreach (var rule in rules)
        {
            if (rule.UserGroup == null) continue;

            // Only consider schedules scoped to this specific display
            var displaySchedules = rule.AccessSchedules
                .Where(s => s.DisplayId == displayId)
                .ToList();

            if (displaySchedules.Count == 0) continue;

            if (displaySchedules.Any(s => _scheduleEvaluator.IsActive(s, now)))
                activeGroups.Add(rule.UserGroup.GroupName);
        }

        // Alphabetical tiebreak for multiple active groups
        return activeGroups.OrderBy(g => g).FirstOrDefault();
    }

    /// <summary>
    /// Find the active group from location-wide rules (schedules without a display scope).
    /// Rules with no schedules are treated as always-active.
    /// </summary>
    private string? FindActiveGroupForLocation(List<TblLocationAccessRule> rules, DateTime now)
    {
        var activeGroups = new List<string>();

        foreach (var rule in rules)
        {
            if (rule.UserGroup == null) continue;

            var locationWideSchedules = rule.AccessSchedules
                .Where(s => s.DisplayId == null)
                .ToList();

            // No schedules = always active
            if (locationWideSchedules.Count == 0 ||
                locationWideSchedules.Any(s => _scheduleEvaluator.IsActive(s, now)))
            {
                activeGroups.Add(rule.UserGroup.GroupName);
            }
        }

        return activeGroups.OrderBy(g => g).FirstOrDefault();
    }
}
