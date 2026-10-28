using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>Per-role logo content for one named logo role (keyed by LogoRoleKey).</summary>
public sealed record AirlineLogoContent(string? ContentPath, string ContentMode = "fit");

/// <summary>
/// One ordered side-by-side layer segment (tblUserGroupLocationLayerContent).
/// IntrinsicWidthPx zero means unknown — the segment fills the remaining cluster width.
/// </summary>
public sealed record AirlineLayerSegment(string ContentPath, int IntrinsicWidthPx);

/// <summary>
/// Resolved content defaults for an airline group at a VideoWall location.
/// LogoPath remains the legacy single logo used by zones without a role key;
/// Logos holds per-role content keyed by the wall's LogoRoleDefinition keys.
/// </summary>
public sealed record AirlineWallDefaults(
    int? ConstraintId,
    string? BackgroundPath, string BackgroundMode,
    string? MaskPath, string MaskMode,
    string? LogoPath,
    IReadOnlyDictionary<string, AirlineLogoContent> Logos,
    int BackgroundOpacityPct = 100,
    int MaskOpacityPct = 100)
{
    public static readonly IReadOnlyDictionary<string, AirlineLogoContent> NoLogos =
        new Dictionary<string, AirlineLogoContent>();
}

public interface IUserGroupLocationDefaultService
{
    /// <summary>
    /// Returns the resolved content defaults for an airline group at a VideoWall location,
    /// evaluated for the active content state at <paramref name="atTime"/> (null = now).
    /// Preference order: per-event override → active state → default state → location
    /// GlobalConstraintId → null. Call sites pass only (userGroupId, locationId); per-event
    /// override resolution is handled by IClusterResolutionService which calls this method
    /// for the fallback tiers.
    /// </summary>
    Task<AirlineWallDefaults> GetDefaultForGroupAsync(int userGroupId, int locationId, DateTime? atTime = null);

    Task<List<TblUserGroupLocationDefault>> GetDefaultsForGroupAsync(int userGroupId);

    /// <summary>All content-state rows for an airline at a wall, default state first, with children.</summary>
    Task<List<TblUserGroupLocationDefault>> GetStatesAsync(int userGroupId, int locationId);

    /// <summary>
    /// Upserts the named state's wall content. When <paramref name="logos"/> is non-null it is
    /// the complete desired per-role set: rows for roles absent from the dictionary (or with
    /// empty ContentPath) are removed. The segment lists behave the same way for the
    /// background/mask side-by-side sequences (order = list order). Null parameters leave
    /// the corresponding child rows untouched. A newly created row becomes the default state
    /// only when no sibling states exist; <paramref name="makeDefaultState"/> true reasserts
    /// the flag and clears it from siblings (false leaves flags untouched).
    /// </summary>
    Task UpsertAsync(int userGroupId, int locationId, int? constraintId,
                     string? backgroundPath, string backgroundMode,
                     string? maskPath, string maskMode,
                     string? logoPath = null,
                     IReadOnlyDictionary<string, AirlineLogoContent>? logos = null,
                     IReadOnlyList<AirlineLayerSegment>? backgroundSegments = null,
                     IReadOnlyList<AirlineLayerSegment>? maskSegments = null,
                     int backgroundOpacityPct = 100,
                     int maskOpacityPct = 100,
                     string stateName = "Default",
                     string? stateScheduleJson = null,
                     bool makeDefaultState = false);

    /// <summary>Creates a new state, copying content and children from <paramref name="copyFromRowId"/> when set. Returns the new row id.</summary>
    Task<int> AddStateAsync(int userGroupId, int locationId, string stateName, int? copyFromRowId);

    /// <summary>Deletes one state row; when it was the default, the first remaining sibling becomes default.</summary>
    Task DeleteStateAsync(int userGroupLocationDefaultId);

    /// <summary>Makes the row the default state and clears the flag from its siblings.</summary>
    Task SetDefaultStateAsync(int userGroupLocationDefaultId);

    /// <summary>Copies the source state's content columns and child rows onto every sibling state.</summary>
    Task SyncStateContentAsync(int sourceRowId);

    Task DeleteAsync(int userGroupLocationDefaultId);
}
