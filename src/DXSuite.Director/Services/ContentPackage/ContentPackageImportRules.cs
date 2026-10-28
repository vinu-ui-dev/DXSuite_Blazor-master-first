using System;
using System.Collections.Generic;
using System.Linq;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>A location as the Playlist display check sees it once the package is applied.</summary>
/// <param name="ContentPackageId">The location's cross-environment id.</param>
/// <param name="FinalTypeId">The package's type for a location it carries, otherwise the target's current type.</param>
/// <param name="InPackage">Whether the package carries the location record, which is what replace mode prunes under.</param>
public sealed record PackageLocationState(Guid ContentPackageId, string? Name, int FinalTypeId, bool InPackage);

/// <summary>A display, already in the target or arriving in the package, and the location it sits under.</summary>
public sealed record PackageDisplayState(Guid ContentPackageId, string? Name, Guid LocationContentPackageId);

/// <summary>An imported asset's owner in the target, and which named halves did not resolve.</summary>
public sealed record AssetOwnershipResolution(int? UserId, int? GroupId, bool UserUnresolved, bool GroupUnresolved);

/// <summary>A Playlist location the package would leave with displays, and those displays' names.</summary>
public sealed record PlaylistDisplayConflict(string? LocationName, IReadOnlyList<string> DisplayNames)
{
    public string Message =>
        $"Location '{LocationName}' is a Playlist location and would have displays: {string.Join(", ", DisplayNames)}. A Playlist location cannot have displays.";
}

/// <summary>
/// The pure decisions behind importing a content package. Queries and writes stay in
/// <see cref="ContentPackageBundleImporter"/>.
/// </summary>
public static class ContentPackageImportRules
{
    public const int PlaylistLocationTypeId = 7;

    private static readonly HashSet<string> ContentPolicies = new(StringComparer.Ordinal) { "all", "none" };
    private static readonly HashSet<string> PlayerPolicies = new(StringComparer.Ordinal) { "all", "untaggedOnly", "none" };
    private static readonly HashSet<string> MatchModes = new(StringComparer.Ordinal) { "all", "any" };

    public static string? PrincipalName(LocationAccessRuleRecord rule) => rule.UserPrincipalName ?? rule.UserGroupName;

    /// <summary>
    /// Every Playlist location the package would leave with a display. A display lands under a Playlist
    /// location either because the package puts it there or because the package retypes the location it
    /// already sits under. Replace mode removes a carried location's displays the package does not list, so
    /// those do not count.
    /// </summary>
    /// <param name="locations">Every location a display in either list sits under, keyed by ContentPackageId.</param>
    /// <param name="targetDisplays">Displays already in the target under those locations.</param>
    /// <param name="packageDisplays">Displays the package carries; each replaces the target display with its id.</param>
    public static IReadOnlyList<PlaylistDisplayConflict> PlaylistDisplayConflicts(
        IEnumerable<PackageLocationState> locations,
        IEnumerable<PackageDisplayState> targetDisplays,
        IEnumerable<PackageDisplayState> packageDisplays,
        bool replaceMode)
    {
        var locationsById = locations.GroupBy(l => l.ContentPackageId).ToDictionary(g => g.Key, g => g.First());
        var incoming = packageDisplays.GroupBy(d => d.ContentPackageId).Select(g => g.Last()).ToList();
        var incomingIds = incoming.Select(d => d.ContentPackageId).ToHashSet();

        var kept = targetDisplays
            .Where(d => !incomingIds.Contains(d.ContentPackageId))
            .Where(d => !(replaceMode && locationsById.TryGetValue(d.LocationContentPackageId, out var l) && l.InPackage));

        return kept.Concat(incoming)
            .Where(d => locationsById.TryGetValue(d.LocationContentPackageId, out var l) && l.FinalTypeId == PlaylistLocationTypeId)
            .GroupBy(d => d.LocationContentPackageId)
            .Select(g => new PlaylistDisplayConflict(
                locationsById[g.Key].Name,
                g.Select(d => d.Name ?? d.ContentPackageId.ToString()).Order(StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(c => c.LocationName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// An asset's owner, each half resolved on its own. A name that does not resolve leaves its half empty,
    /// flagged for a warning, and never falls back to the system user, because ownership decides who may
    /// change the asset and nobody is a member of the system user. A group with no user is legal here and
    /// only here: control reads live group membership, so the group half is still correct on its own.
    /// </summary>
    public static AssetOwnershipResolution ResolveOwnership(
        string? userPrincipalName, int? resolvedUserId, string? groupName, int? resolvedGroupId)
    {
        var userId = string.IsNullOrWhiteSpace(userPrincipalName) ? null : resolvedUserId;
        var groupId = string.IsNullOrWhiteSpace(groupName) ? null : resolvedGroupId;
        return new AssetOwnershipResolution(
            userId,
            groupId,
            UserUnresolved: !string.IsNullOrWhiteSpace(userPrincipalName) && userId == null,
            GroupUnresolved: !string.IsNullOrWhiteSpace(groupName) && groupId == null);
    }

    /// <summary>
    /// A rule's ViewScope as it is written. A view rule that cannot say it sees everything sees its scope, the
    /// narrower reading; every other access level carries none.
    /// </summary>
    public static string? NormalizeViewScope(LocationAccessRuleRecord rule, List<string> warnings)
    {
        if (rule.AccessLevel != "view")
        {
            if (!string.IsNullOrEmpty(rule.ViewScope))
                warnings.Add($"Access rule for '{PrincipalName(rule)}' ({rule.AccessLevel}): view scope '{rule.ViewScope}' only applies to view rules and was dropped.");
            return null;
        }

        if (rule.ViewScope is ContentScope.ViewScopeAll or ContentScope.ViewScopeNone)
            return rule.ViewScope;

        warnings.Add($"Access rule for '{PrincipalName(rule)}' (view): scope '{rule.ViewScope}' is not recognised and was imported as '{ContentScope.ViewScopeNone}'.");
        return ContentScope.ViewScopeNone;
    }

    /// <summary>A location's match mode as it is written: a value the target does not know reads as <c>all</c>, loudly.</summary>
    public static string NormalizeMatchMode(string? locationName, string? matchMode, List<string> warnings)
    {
        if (matchMode != null && MatchModes.Contains(matchMode)) return matchMode;
        warnings.Add($"Location '{locationName}': metadata match mode '{matchMode}' is not recognised and was imported as 'all'.");
        return "all";
    }

    /// <summary>
    /// A rule's two policies as they are written. An unknown value falls back to <c>all</c>, which preserves
    /// pre-targeting behaviour, with a warning.
    /// </summary>
    public static (string ContentPolicy, string PlayerPolicy) NormalizePolicies(
        string? locationName, string key, string? contentPolicy, string? playerPolicy, List<string> warnings)
    {
        var content = contentPolicy != null && ContentPolicies.Contains(contentPolicy) ? contentPolicy : null;
        var player = playerPolicy != null && PlayerPolicies.Contains(playerPolicy) ? playerPolicy : null;

        if (content == null)
            warnings.Add($"Location '{locationName}' key '{key}': untagged content policy '{contentPolicy}' is not recognised and was imported as 'all'.");
        if (player == null)
            warnings.Add($"Location '{locationName}' key '{key}': untagged player policy '{playerPolicy}' is not recognised and was imported as 'all'.");

        return (content ?? "all", player ?? "all");
    }

    /// <summary>
    /// The participating keys no player at the location holds a value for, in the order given. Devices never
    /// travel in a package, so a targeting location usually arrives with none of these set.
    /// </summary>
    public static IReadOnlyList<string> KeysNoPlayerHolds(
        IEnumerable<string> participatingKeys,
        IEnumerable<IReadOnlyDictionary<string, IReadOnlySet<string>>> deviceValues)
    {
        var held = new HashSet<string>(
            deviceValues.SelectMany(v => v.Where(kv => kv.Value.Count > 0).Select(kv => MetadataEvaluator.NormalizeKey(kv.Key))),
            MetadataEvaluator.KeyComparer);

        return participatingKeys
            .Select(MetadataEvaluator.NormalizeKey)
            .Where(k => k.Length > 0 && !held.Contains(k))
            .Distinct(MetadataEvaluator.KeyComparer)
            .ToList();
    }
}
