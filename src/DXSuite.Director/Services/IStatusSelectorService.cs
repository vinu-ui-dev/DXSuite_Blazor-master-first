using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Service for StatusSelector display workflow — simplified "click to change content" operations.
/// Content is stored as TblScheduleEvent records; StandardDisplay's schedule resolution handles rendering.
/// </summary>
public interface IStatusSelectorService
{
    /// <summary>
    /// Sets the active content for a display via schedule events.
    /// When scheduleContext is null, updates the DefaultContent event.
    /// When scheduleContext is provided, creates a one-shot temp event bounded to today's access window.
    /// </summary>
    Task SetActiveContentAsync(int displayId, int? assetId, string? metadataValue, string editUser, ScheduleContext? scheduleContext = null, int? constraintId = null, string? metadataKeyOverride = null);

    /// <summary>
    /// Clears the active content selection for a display.
    /// When scheduleContext is null, resets the DefaultContent event to empty (no asset shown).
    /// When scheduleContext is provided, removes the matching temp event so the default takes over.
    /// </summary>
    Task ClearActiveContentAsync(int displayId, string editUser, ScheduleContext? scheduleContext = null);

    /// <summary>
    /// Gets assets from the linked Asset Library, optionally grouped by metadata key.
    /// Returns a flat list when no metadata key, or grouped by value when key is specified.
    /// </summary>
    Task<StatusSelectorContent> GetContentAsync(int displayId, int? constraintId = null, string? metadataKeyOverride = null);

    /// <summary>
    /// Gets the current active asset ID from the DefaultContent schedule event for a display.
    /// </summary>
    Task<int?> GetDefaultAssetIdAsync(int displayId);

    /// <summary>
    /// Gets the list of constraint-bearing schedule contexts for the display's location.
    /// Empty list means no dropdown is needed (simple mode).
    /// </summary>
    Task<List<ScheduleContext>> GetScheduleContextsAsync(int displayId);

    /// <summary>
    /// Resolves the asset-library constraint a counter display should currently offer for selection,
    /// based on the airline whose allocation block is active on it. For a counter on a video wall this
    /// is the active allocation event's UserGroup; for a transient-cluster-owned display it is the
    /// owning group. The constraint is the group's "Counter Content" selection
    /// (TblUserGroupLocationDefault.DefaultConstraintId) for the wall location.
    /// Returns null when no airline block is active — callers then fall back to the display's own library.
    /// </summary>
    Task<int?> ResolveActiveAirlineConstraintAsync(int displayId);
}

/// <summary>
/// Represents an access schedule window with a constraint, used for the context dropdown.
/// </summary>
public class ScheduleContext
{
    public int AccessScheduleId { get; set; }
    public string GroupName { get; set; } = "";
    public int ConstraintId { get; set; }
    public string? ConstraintName { get; set; }
    public string? MetadataKey { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public bool Sunday { get; set; }
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }

    public string GetDayLabel()
    {
        var days = new List<string>();
        if (Sunday) days.Add("Sun");
        if (Monday) days.Add("Mon");
        if (Tuesday) days.Add("Tue");
        if (Wednesday) days.Add("Wed");
        if (Thursday) days.Add("Thu");
        if (Friday) days.Add("Fri");
        if (Saturday) days.Add("Sat");

        if (days.Count == 0 || days.Count == 7) return "Every day";
        if (days.Count == 5 && Monday && Tuesday && Wednesday && Thursday && Friday) return "Mon\u2013Fri";
        if (days.Count == 2 && Saturday && Sunday) return "Sat\u2013Sun";
        return string.Join(", ", days);
    }

    public string GetTimeLabel()
    {
        if (StartTime == null && EndTime == null) return "All day";
        var start = StartTime.HasValue ? StartTime.Value.ToString("h:mm tt") : "Start";
        var end = EndTime.HasValue ? EndTime.Value.ToString("h:mm tt") : "End";
        return $"{start}\u2013{end}";
    }

    public string GetDisplayLabel() => $"{GroupName} ({GetDayLabel()} {GetTimeLabel()})";
}

/// <summary>
/// Content model for StatusSelector display. Contains either flat assets or grouped-by-metadata assets.
/// </summary>
public class StatusSelectorContent
{
    public int DisplayId { get; set; }
    public int? AssetLibraryLocationId { get; set; }
    public string? MetadataKey { get; set; }
    public int? CurrentActiveAssetId { get; set; }

    /// <summary>
    /// Flat asset list (when no metadata key is configured)
    /// </summary>
    public List<StatusSelectorAsset> Assets { get; set; } = new();

    /// <summary>
    /// Assets grouped by metadata value (when metadata key is configured)
    /// </summary>
    public Dictionary<string, List<StatusSelectorAsset>> GroupedAssets { get; set; } = new();
}

public class StatusSelectorAsset
{
    public int AssetId { get; set; }
    public string AssetName { get; set; } = "";
    public string? AssetPath { get; set; }
    public int AssetTypeId { get; set; }
    public string? MetadataValue { get; set; }
}
