using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Shared.Multitenancy;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services;

public class StatusSelectorService : IStatusSelectorService
{
    private readonly IDirectorDBContext _db;
    private readonly IManifestCacheService _manifestCache;
    private readonly IPublicationInvalidator _publicationInvalidator;
    private readonly ILogger<StatusSelectorService> _logger;

    public StatusSelectorService(
        IDirectorDBContext db,
        IManifestCacheService manifestCache,
        ILogger<StatusSelectorService> logger,
        IPublicationInvalidator publicationInvalidator)
    {
        _publicationInvalidator = publicationInvalidator;
        _db = db;
        _manifestCache = manifestCache;
        _logger = logger;
    }

    public async Task SetActiveContentAsync(int displayId, int? assetId, string? metadataValue, string editUser, ScheduleContext? scheduleContext = null, int? constraintId = null, string? metadataKeyOverride = null)
    {
        var display = await _db.TblDisplays.FirstOrDefaultAsync(d => d.DisplayId == displayId);
        if (display == null) throw new InvalidOperationException($"Display {displayId} not found.");

        int? assetLocationId = display.AssetLibraryLocationId;
        if (constraintId.HasValue)
        {
            var constraint = await _db.TblConstraints.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ConstraintId == constraintId.Value);
            if (constraint?.IsGlobal == true && constraint.OwningLocationId.HasValue)
                assetLocationId = constraint.OwningLocationId.Value;
        }

        int? resolvedAssetId = assetId;
        if (!resolvedAssetId.HasValue && !string.IsNullOrEmpty(metadataValue))
        {
            var metadataKey = metadataKeyOverride ?? display.StatusSelectorMetadataKey;
            var matchingAsset = await GetFirstAssetForMetadataAsync(_db, assetLocationId, constraintId, metadataValue, metadataKey);
            resolvedAssetId = matchingAsset?.AssetId;
        }

        if (!resolvedAssetId.HasValue)
        {
            _logger.LogWarning("StatusSelector: No asset resolved for display {DisplayId}", displayId);
            return;
        }

        if (assetLocationId.HasValue)
        {
            var excludedIds = await GetExcludedAssetIdsAsync(_db, assetLocationId.Value);
            if (excludedIds != null && excludedIds.Contains(resolvedAssetId.Value))
            {
                _logger.LogWarning("StatusSelector: Blocked selection of unapproved asset {AssetId} for display {DisplayId}",
                    resolvedAssetId.Value, displayId);
                throw new InvalidOperationException("The selected content has not been approved.");
            }
        }

        if (scheduleContext != null)
            await CreateTempScheduleEventAsync(_db, displayId, resolvedAssetId.Value, scheduleContext);
        else
            await UpdateDefaultEventAsync(_db, displayId, resolvedAssetId.Value);

        display.DisplayMode = (int)Types.DisplayMode.StatusSelector;

        await _db.SaveChangesAsync();

        _publicationInvalidator.DisplayChanged(displayId);

        _logger.LogInformation("StatusSelector: Display {DisplayId} set to asset {AssetId} (context: {Context})",
            displayId, resolvedAssetId, scheduleContext?.GroupName ?? "default");
    }

    public async Task ClearActiveContentAsync(int displayId, string editUser, ScheduleContext? scheduleContext = null)
    {
        var display = await _db.TblDisplays.FirstOrDefaultAsync(d => d.DisplayId == displayId);
        if (display == null) throw new InvalidOperationException($"Display {displayId} not found.");

        if (scheduleContext != null)
        {
            // Remove any temp event matching this schedule window so the DefaultContent takes over.
            var today = DateTime.Today;
            var temps = await _db.TblScheduleEvents
                .Where(e => e.DisplayId == displayId
                            && e.DefaultContent == false
                            && e.StartDate == today
                            && e.StartTime == scheduleContext.StartTime
                            && e.EndTime == scheduleContext.EndTime)
                .ToListAsync();
            foreach (var temp in temps)
                _db.TblScheduleEvents.Remove(temp);
        }
        else
        {
            // Set the DefaultContent event to empty JSON so no asset is rendered.
            var defaultEvent = await _db.TblScheduleEvents
                .FirstOrDefaultAsync(e => e.DisplayId == displayId && e.DefaultContent == true);
            if (defaultEvent != null)
                defaultEvent.ContentAssignment = "{}";
            // If there was no DefaultContent event there is nothing to clear — that is already "empty."
        }

        await _db.SaveChangesAsync();

        _publicationInvalidator.DisplayChanged(displayId);

        _logger.LogInformation("StatusSelector: Display {DisplayId} content cleared (context: {Context})",
            displayId, scheduleContext?.GroupName ?? "default");
    }

    public async Task<StatusSelectorContent> GetContentAsync(int displayId, int? constraintId = null, string? metadataKeyOverride = null)
    {
        var display = await _db.TblDisplays.AsNoTracking().FirstOrDefaultAsync(d => d.DisplayId == displayId);
        if (display == null) return new StatusSelectorContent { DisplayId = displayId };

        var metadataKey = metadataKeyOverride ?? display.StatusSelectorMetadataKey;
        var activeAssetId = await GetActiveAssetIdFromScheduleAsync(_db, displayId);

        var result = new StatusSelectorContent
        {
            DisplayId = displayId,
            AssetLibraryLocationId = display.AssetLibraryLocationId,
            MetadataKey = metadataKey,
            CurrentActiveAssetId = activeAssetId
        };

        if (!display.AssetLibraryLocationId.HasValue)
            return result;

        int assetLocationId = display.AssetLibraryLocationId.Value;

        if (constraintId.HasValue)
        {
            var constraint = await _db.TblConstraints.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ConstraintId == constraintId.Value);
            if (constraint?.IsGlobal == true && constraint.OwningLocationId.HasValue)
                assetLocationId = constraint.OwningLocationId.Value;
        }

        var assetsQuery = _db.TblAssets
            .AsNoTracking()
            .Where(a => a.LocationId == assetLocationId && !a.IsDeleted);

        if (constraintId.HasValue)
            assetsQuery = assetsQuery.Where(a => a.ConstraintId == constraintId.Value);

        var assets = await assetsQuery
            .OrderBy(a => a.SortOrder).ThenBy(a => a.AssetName)
            .ToListAsync();

        var excludedIds = await GetExcludedAssetIdsAsync(_db, assetLocationId);
        if (excludedIds != null)
            assets = assets.Where(a => !excludedIds.Contains(a.AssetId)).ToList();

        if (string.IsNullOrEmpty(metadataKey))
        {
            result.Assets = assets.Select(a => new StatusSelectorAsset
            {
                AssetId = a.AssetId,
                AssetName = a.AssetName ?? Path.GetFileNameWithoutExtension(a.AssetPath) ?? "",
                AssetPath = a.AssetPath,
                AssetTypeId = a.AssetTypeId
            }).ToList();
        }
        else
        {
            var assetIds = assets.Select(a => a.AssetId).ToList();
            var metadata = await _db.TblAssetContentMetadata
                .AsNoTracking()
                .Where(m => assetIds.Contains(m.AssetId) && m.MetadataKey == metadataKey)
                .ToListAsync();

            // The key is typed list, so an asset holds one value. Should it ever hold more, the page
            // still renders on the earliest one instead of failing to load.
            var metadataMap = metadata
                .GroupBy(m => m.AssetId)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.AssetContentMetadataId).First().MetadataValue);

            var grouped = new Dictionary<string, List<StatusSelectorAsset>>();
            foreach (var asset in assets)
            {
                var value = metadataMap.GetValueOrDefault(asset.AssetId, "Uncategorized");
                if (!grouped.ContainsKey(value))
                    grouped[value] = new List<StatusSelectorAsset>();

                grouped[value].Add(new StatusSelectorAsset
                {
                    AssetId = asset.AssetId,
                    AssetName = asset.AssetName ?? Path.GetFileNameWithoutExtension(asset.AssetPath) ?? "",
                    AssetPath = asset.AssetPath,
                    AssetTypeId = asset.AssetTypeId,
                    MetadataValue = value
                });
            }

            result.GroupedAssets = grouped;
        }

        return result;
    }

    public async Task<int?> GetDefaultAssetIdAsync(int displayId)
    {
        var defaultEvent = await _db.TblScheduleEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.DisplayId == displayId && e.DefaultContent == true);

        if (defaultEvent == null) return null;

        try
        {
            var assignment = JsonConvert.DeserializeObject<ContentAssignment>(defaultEvent.ContentAssignment);
            return assignment?.Content?.FirstOrDefault()?.AssetId;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<ScheduleContext>> GetScheduleContextsAsync(int displayId)
    {
        var display = await _db.TblDisplays.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DisplayId == displayId);
        if (display == null) return new List<ScheduleContext>();

        var contexts = await _db.TblAccessSchedules
            .AsNoTracking()
            .Where(s => s.LocationAccessRule.LocationId == display.LocationId && s.ConstraintId != null)
            .Select(s => new ScheduleContext
            {
                AccessScheduleId = s.AccessScheduleId,
                GroupName = s.LocationAccessRule.UserGroup != null
                    ? s.LocationAccessRule.UserGroup.GroupName
                    : s.LocationAccessRule.User != null
                        ? s.LocationAccessRule.User.FriendlyName ?? "User"
                        : "Unknown",
                ConstraintId = s.ConstraintId!.Value,
                ConstraintName = s.Constraint != null ? s.Constraint.ConstraintName : null,
                MetadataKey = s.MetadataKey,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Sunday = s.Sunday,
                Monday = s.Monday,
                Tuesday = s.Tuesday,
                Wednesday = s.Wednesday,
                Thursday = s.Thursday,
                Friday = s.Friday,
                Saturday = s.Saturday
            })
            .ToListAsync();

        return contexts;
    }

    public async Task<int?> ResolveActiveAirlineConstraintAsync(int displayId)
    {
        var display = await _db.TblDisplays.AsNoTracking()
            .Include(d => d.Location)
            .FirstOrDefaultAsync(d => d.DisplayId == displayId);
        if (display == null) return null;

        int? groupId;
        int wallLocationId;

        if (display.Location?.IsTransientCluster == true
            && display.Location.ParentVideoWallLocationId.HasValue)
        {
            // Display owned directly by a transient cluster — the airline is the owning group.
            groupId        = display.Location.OwningUserGroupId;
            wallLocationId = display.Location.ParentVideoWallLocationId.Value;
        }
        else
        {
            // Counter living on the wall itself — the airline is whichever allocation block is
            // active right now. Allocation events carry UserGroupId and DefaultContent = false.
            var now = DateTime.Now;
            var events = await _db.TblScheduleEvents.AsNoTracking()
                .Where(e => e.DisplayId == displayId && !e.DefaultContent && e.UserGroupId != null)
                .ToListAsync();
            groupId        = events.FirstOrDefault(e => IsAllocationActive(e, now))?.UserGroupId;
            wallLocationId = display.LocationId;
        }

        if (groupId == null) return null;

        var def = await _db.TblUserGroupLocationDefaults.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserGroupId == groupId.Value && x.LocationId == wallLocationId);
        return def?.DefaultConstraintId;
    }

    /// <summary>
    /// True when an airline allocation event is active at the given time: within its date range,
    /// on an enabled day of week (no days set = every day), and inside its time window (supports
    /// overnight windows that wrap past midnight; no times set = active all day).
    /// </summary>
    private static bool IsAllocationActive(TblScheduleEvent ev, DateTime atTime)
    {
        if (ev.StartDate.HasValue && atTime < ev.StartDate.Value) return false;
        if (ev.EndDate.HasValue   && atTime > ev.EndDate.Value)   return false;

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

        if (ev.StartTime == null && ev.EndTime == null) return true;

        return ScheduleTime.IsWithinWindow(TimeOnly.FromDateTime(atTime), ev.StartTime, ev.EndTime);
    }

    private async Task UpdateDefaultEventAsync(IDirectorDBContext db, int displayId, int assetId)
    {
        var existingDefault = await db.TblScheduleEvents
            .FirstOrDefaultAsync(e => e.DisplayId == displayId && e.DefaultContent == true);

        var contentAssignment = new ContentAssignment
        {
            Content = new List<ContentItem> { new ContentItem { AssetId = assetId, Duration = 10 } }
        };
        var json = JsonConvert.SerializeObject(contentAssignment);

        if (existingDefault != null)
            existingDefault.ContentAssignment = json;
        else
            db.TblScheduleEvents.Add(new TblScheduleEvent
            {
                DisplayId = displayId,
                DefaultContent = true,
                ScheduleEventTypeId = 1,
                ContentAssignment = json,
                CreateDate = DateTime.UtcNow
            });
    }

    private async Task CreateTempScheduleEventAsync(IDirectorDBContext db, int displayId, int assetId, ScheduleContext context)
    {
        var today = DateTime.Today;
        var existingTemps = await db.TblScheduleEvents
            .Where(e => e.DisplayId == displayId
                        && e.DefaultContent == false
                        && e.StartDate == today
                        && e.StartTime == context.StartTime
                        && e.EndTime == context.EndTime)
            .ToListAsync();

        foreach (var temp in existingTemps)
            db.TblScheduleEvents.Remove(temp);

        var contentAssignment = new ContentAssignment
        {
            Content = new List<ContentItem> { new ContentItem { AssetId = assetId, Duration = 10 } }
        };

        var endDate = today;
        if (context.StartTime.HasValue && context.EndTime.HasValue && context.StartTime > context.EndTime)
            endDate = today.AddDays(1);

        db.TblScheduleEvents.Add(new TblScheduleEvent
        {
            DisplayId = displayId,
            DefaultContent = false,
            ScheduleEventTypeId = 1,
            ContentAssignment = JsonConvert.SerializeObject(contentAssignment),
            StartDate = today,
            EndDate = endDate.AddDays(1),
            StartTime = context.StartTime,
            EndTime = context.EndTime,
            Sunday = context.Sunday,
            Monday = context.Monday,
            Tuesday = context.Tuesday,
            Wednesday = context.Wednesday,
            Thursday = context.Thursday,
            Friday = context.Friday,
            Saturday = context.Saturday,
            CreateDate = DateTime.UtcNow
        });
    }

    private async Task<int?> GetActiveAssetIdFromScheduleAsync(IDirectorDBContext db, int displayId)
    {
        var now = DateTime.Now;
        var events = await db.TblScheduleEvents
            .AsNoTracking()
            .Where(e => e.DisplayId == displayId)
            .ToListAsync();

        foreach (var evt in events.Where(e => !e.DefaultContent))
        {
            // Whole-day comparison, so an event fires on the last day of its range rather than
            // expiring at midnight on the morning of it.
            bool inDateRange = (evt.StartDate == null || evt.StartDate.Value.Date <= now.Date) &&
                               (evt.EndDate == null || evt.EndDate.Value.Date >= now.Date);
            if (!inDateRange) continue;

            bool inTimeRange = new DailyWindow(evt.StartTime, evt.EndTime)
                .Contains(TimeOnly.FromDateTime(now));

            if (inTimeRange)
            {
                try
                {
                    var assignment = JsonConvert.DeserializeObject<ContentAssignment>(evt.ContentAssignment);
                    var id = assignment?.Content?.FirstOrDefault()?.AssetId;
                    if (id.HasValue) return id;
                }
                catch { }
            }
        }

        var defaultEvt = events.FirstOrDefault(e => e.DefaultContent);
        if (defaultEvt != null)
        {
            try
            {
                var assignment = JsonConvert.DeserializeObject<ContentAssignment>(defaultEvt.ContentAssignment);
                return assignment?.Content?.FirstOrDefault()?.AssetId;
            }
            catch { }
        }

        return null;
    }

    private static async Task<TblAsset?> GetFirstAssetForMetadataAsync(
        IDirectorDBContext db, int? assetLocationId, int? constraintId, string metadataValue, string? metadataKey)
    {
        if (!assetLocationId.HasValue || string.IsNullOrEmpty(metadataKey))
            return null;

        var assetsQuery = db.TblAssets.Where(a => a.LocationId == assetLocationId.Value && !a.IsDeleted);
        if (constraintId.HasValue)
            assetsQuery = assetsQuery.Where(a => a.ConstraintId == constraintId.Value);

        if (assetLocationId.HasValue)
        {
            var excludedIds = await GetExcludedAssetIdsAsync(db, assetLocationId.Value);
            if (excludedIds != null)
                assetsQuery = assetsQuery.Where(a => !excludedIds.Contains(a.AssetId));
        }

        var matchingAssetId = await db.TblAssetContentMetadata
            .Where(m => m.MetadataKey == metadataKey && m.MetadataValue == metadataValue)
            .Join(assetsQuery, m => m.AssetId, a => a.AssetId, (m, a) => a)
            .OrderBy(a => a.SortOrder)
            .Select(a => a.AssetId)
            .FirstOrDefaultAsync();

        if (matchingAssetId == 0) return null;

        return await db.TblAssets.FirstOrDefaultAsync(a => a.AssetId == matchingAssetId);
    }

    /// <summary>
    /// Returns asset IDs excluded due to non-Approved status.
    /// Assets without any approval record (corporate content) are never excluded.
    /// Returns null when approval is not enabled on the location.
    /// </summary>
    private static async Task<HashSet<int>?> GetExcludedAssetIdsAsync(IDirectorDBContext db, int assetLocationId)
    {
        var location = await db.TblLocations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == assetLocationId);

        if (location == null || !location.EnableContentApproval)
            return null;

        var excludedIds = await db.TblAssetApprovals.AsNoTracking()
            .Include(a => a.Asset)
            .Where(a => a.Asset != null
                && a.Asset.LocationId == assetLocationId
                && a.ApprovalStatus != (int)ApprovalStatus.Approved)
            .Select(a => a.AssetId)
            .ToListAsync();

        return excludedIds.ToHashSet();
    }
}
