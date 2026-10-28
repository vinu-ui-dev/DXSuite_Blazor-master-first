namespace DXSuite.Director.Services;

/// <summary>
/// Group metadata contract for video wall scheduling: which walls a user group
/// appears on as a schedulable airline in the Ticket Counter Scheduler (schedule
/// blocks and the airline content tab). The value is "all" (every wall, including
/// walls created later) or a CSV of LocationIds. Groups without the metadata key
/// do not appear on any wall — most groups are not airlines.
/// </summary>
public static class GroupVideoWallAssignments
{
    public const string MetadataKey = "videoWallAssignments";
    public const string AllWalls = "all";

    /// <summary>
    /// The one assignment value a group holds. Group metadata is multi-value, but this key is not: two
    /// rows would silently change which walls an airline appears on, so that fails loudly instead.
    /// </summary>
    public static string? SingleValue(int userGroupId, IReadOnlyCollection<string> values) =>
        values.Count <= 1
            ? values.FirstOrDefault()
            : throw new InvalidOperationException(
                $"User group {userGroupId} has {values.Count} {MetadataKey} values; exactly one is expected.");

    public static bool AppliesTo(string? value, int locationId)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals(AllWalls, StringComparison.OrdinalIgnoreCase)) return true;
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(s => int.TryParse(s, out var id) && id == locationId);
    }
}
