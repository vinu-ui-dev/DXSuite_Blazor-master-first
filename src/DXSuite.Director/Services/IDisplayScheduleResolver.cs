namespace DXSuite.Director.Services;

/// <summary>
/// Resolves the active content constraint for a display based on access schedules.
/// Used by SignageController to filter assets during content delivery.
/// </summary>
public interface IDisplayScheduleResolver
{
    /// <summary>
    /// Returns the active constraint ID for a display at the given time.
    /// Returns null if no constraint-scoped schedule is active.
    /// </summary>
    int? ResolveActiveConstraint(string tenantId, int displayId);
}
