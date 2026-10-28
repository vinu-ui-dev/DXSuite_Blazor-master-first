using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Services;

/// <summary>
/// One participating key as the upload form shows it.
/// </summary>
/// <param name="Options">What may be chosen. For a scoped identity, only the values it holds.</param>
/// <param name="Selected">The values the form starts with.</param>
/// <param name="IsBlocking">The identity has no value and the key sends content with no value nowhere, so the upload cannot go ahead.</param>
/// <param name="Reason">Why the field is set, disabled or blocking, as a sentence for the form. Null when there is nothing to explain.</param>
public sealed record UploadField(
    string Key,
    string Label,
    string FieldType,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> Selected,
    bool IsDisabled,
    bool IsBlocking,
    string? Reason)
{
    public bool IsMulti => FieldType == MetadataFieldTypes.MultiSelect;

    /// <summary>The value set by default for a single-valued key with several to choose from, marked in its option list.</summary>
    public string? DefaultValue => !IsMulti && !IsDisabled && Options.Count > 1 ? Options[0] : null;
}

/// <summary>
/// The upload form's decisions, pure so every creation site renders and enforces them the same way.
/// </summary>
public static class UploadFormRules
{
    /// <summary>
    /// How a participating key renders for the uploading identity. Cardinality follows the key's type: a
    /// multi-select key starts with every value the identity holds, any other key with the first.
    /// </summary>
    /// <param name="identity">The picked identity, or null for an unrestricted upload, where the registry alone decides.</param>
    /// <param name="held">The identity's values for this key.</param>
    /// <param name="isLibrary">Whether the location is an Asset Library, where a missing value affects visibility rather than players.</param>
    public static UploadField Resolve(
        MetadataKeyRule rule,
        MetadataDefinitionDto? definition,
        UploadCandidate? identity,
        IReadOnlySet<string>? held,
        bool isLibrary)
    {
        var label = definition?.DisplayName is { Length: > 0 } name ? name : rule.Key;
        var type = definition?.FieldType ?? MetadataFieldTypes.String;

        if (identity is null)
            return new UploadField(rule.Key, label, type, definition?.Options ?? [], [], false, false, null);

        // "Myself" labels an identity; the sentence subject is "You", with a verb that agrees.
        var subject = identity.IsMyself ? "You" : identity.Label;
        var has = identity.IsMyself ? "have" : "has";
        var holds = identity.IsMyself ? "hold" : "holds";
        var target = identity.IsMyself ? "your account" : identity.Label;

        var values = (held ?? new HashSet<string>())
            .Order(StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v, StringComparer.Ordinal)
            .ToList();

        if (values.Count == 0)
        {
            if (rule.UntaggedContent == UntaggedContentPolicy.None)
            {
                var outcome = isLibrary
                    ? "this location hides content with no value from your view"
                    : "this location sends content with no value to no players";
                return new UploadField(rule.Key, label, type, [], [], true, true,
                    $"{label}: {subject} {has} no value for this field, and {outcome}. Ask an administrator to add a {label} value to {target}.");
            }

            var harmless = isLibrary
                ? "Content with no value stays visible to you, so this stays empty"
                : "Content with no value plays on all players, so this stays empty";
            return new UploadField(rule.Key, label, type, [], [], true, false,
                $"{label}: {subject} {has} no value for this field. {harmless}.");
        }

        if (values.Count == 1)
            return new UploadField(rule.Key, label, type, values, values, true, false,
                $"{subject} {has} one {label} value, so there is nothing to choose.");

        if (type == MetadataFieldTypes.MultiSelect)
            return new UploadField(rule.Key, label, type, values, values, false, false,
                $"{subject} {holds} {values.Count} {label} values. {label} takes any number, so all of them are used.");

        return new UploadField(rule.Key, label, type, values, [values[0]], false, false,
            $"{subject} {holds} {values.Count} values for this field and {label} takes one, so one is set by default. Change it if it is not the right one.");
    }

    /// <summary>
    /// The tags a scoped upload is written with, from what the form picked, never beyond what the identity
    /// holds now. A key the form did not pick takes the identity's default; a multi-select key may be
    /// narrowed to nothing; a single-valued key picked off the identity's values falls back to the default.
    /// </summary>
    /// <param name="picks">Values chosen per key, or null when no form was shown.</param>
    public static Dictionary<string, List<string>> ConstrainPicks(
        IReadOnlyCollection<string> participatingKeys,
        IReadOnlyDictionary<string, string> fieldTypes,
        IReadOnlyDictionary<string, IReadOnlySet<string>> held,
        IReadOnlyDictionary<string, List<string>>? picks)
    {
        var defaults = UploadIdentityRules.ValuesFor(participatingKeys, fieldTypes, held);
        var result = new Dictionary<string, List<string>>(MetadataEvaluator.KeyComparer);

        foreach (var key in participatingKeys)
        {
            defaults.TryGetValue(key, out var fallback);
            List<string>? picked = null;
            var wasPicked = picks != null && picks.TryGetValue(key, out picked);
            if (!wasPicked || !held.TryGetValue(key, out var allowed))
            {
                if (fallback is { Count: > 0 }) result[key] = fallback;
                continue;
            }

            var chosen = MetadataValueSet.Normalize(picked).Where(allowed.Contains).ToList();
            var isMulti = fieldTypes.TryGetValue(key, out var type) && type == MetadataFieldTypes.MultiSelect;

            if (isMulti)
            {
                if (chosen.Count > 0) result[key] = chosen;
            }
            else if (chosen.Count > 0)
            {
                result[key] = [chosen[0]];
            }
            else if (fallback is { Count: > 0 })
            {
                result[key] = fallback;
            }
        }

        return result;
    }
}

/// <summary>
/// Who may edit, replace, delete, re-tag or resubmit an asset. Approval itself is not gated.
/// </summary>
public static class AssetOwnership
{
    public const string FullAccessOnlyReason = "Only full access users can change this content.";

    /// <param name="accessLevel">The user's access at the asset's location: "full", "partial", "view", or null when no rule applies, which is full.</param>
    /// <param name="memberGroupIds">The user's current group memberships, read live rather than from the upload.</param>
    public static bool CanControl(string? accessLevel, int userId, IReadOnlySet<int> memberGroupIds, int? ownerUserId, int? ownerGroupId) =>
        accessLevel switch
        {
            null or "full" => true,
            "partial" => ownerGroupId is int group
                ? memberGroupIds.Contains(group)
                : ownerUserId is int owner && owner == userId,
            _ => false
        };

    /// <summary>
    /// The line shown beside disabled controls. It names the owning group, never a person, and is null when
    /// nothing is taken away or when view access hides the controls entirely.
    /// </summary>
    public static string? LockReason(string? accessLevel, int userId, IReadOnlySet<int> memberGroupIds, int? ownerUserId, int? ownerGroupId, string? ownerGroupName)
    {
        if (accessLevel is not "partial" || CanControl(accessLevel, userId, memberGroupIds, ownerUserId, ownerGroupId))
            return null;

        if (ownerGroupId is not null)
            return $"Owned by {ownerGroupName ?? "another group"}.";

        return ownerUserId is null ? FullAccessOnlyReason : "Owned by another user.";
    }
}
