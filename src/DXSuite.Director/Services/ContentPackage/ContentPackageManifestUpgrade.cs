using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Brings a manifest read from a bundle up to the current schema before anything compares or writes it. A
/// pre-v4 bundle is a snapshot of a database from before the metadata targeting migrations, so this applies
/// those migrations to the bundle: participating keys leave <c>ExtendedData</c> for rule records, a missing
/// match mode reads as <c>all</c>, and the access rule sentinel becomes a ViewScope.
/// </summary>
public static class ContentPackageManifestUpgrade
{
    private const string ViewScopeAllSentinel = "__all__";
    private const string ViewScopeNoneSentinel = "__none__";

    // Blobs are written camelCase but were read case-insensitively, so both spellings exist in the wild.
    private static readonly string[] CustomFieldsProperties = ["customMetadataFields", "CustomMetadataFields"];

    /// <summary>Upgrades <paramref name="manifest"/> in place and returns a warning for everything it could not carry.</summary>
    public static List<string> Apply(ContentPackageManifest manifest)
    {
        var warnings = new List<string>();
        var preV4 = manifest.SchemaVersion < 4;

        foreach (var location in manifest.Locations)
        {
            var (strippedExtendedData, legacyKeys) = StripCustomMetadataFields(location.ExtendedData);
            location.ExtendedData = strippedExtendedData;

            if (preV4)
            {
                foreach (var key in legacyKeys.Where(k => !manifest.LocationMetadataRules.Any(r =>
                             r.LocationContentPackageId == location.ContentPackageId
                             && MetadataEvaluator.KeyComparer.Equals(MetadataEvaluator.NormalizeKey(r.MetadataKey), k))))
                {
                    manifest.LocationMetadataRules.Add(new LocationMetadataRuleRecord
                    {
                        ContentPackageId = Guid.NewGuid(),
                        LocationContentPackageId = location.ContentPackageId,
                        MetadataKey = key
                    });
                }
            }

            if (string.IsNullOrWhiteSpace(location.MetadataMatchMode))
                location.MetadataMatchMode = "all";
        }

        foreach (var rule in manifest.LocationAccessRules)
        {
            if (preV4)
                rule.ViewScope = TranslateLegacyFilterKey(rule, warnings);
            rule.LegacyMetadataFilterKey = null;
        }

        return warnings;
    }

    /// <summary>
    /// Removes the retired participating-key list from a location blob and returns the keys it held, trimmed,
    /// distinct and in order. A blob without the property, or one that is not a JSON object, is returned
    /// unchanged so a wall layout's tokens are never reformatted for nothing.
    /// </summary>
    public static (string? ExtendedData, IReadOnlyList<string> Keys) StripCustomMetadataFields(string? extendedData)
    {
        if (string.IsNullOrWhiteSpace(extendedData)) return (extendedData, []);

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(extendedData) as JsonObject;
        }
        catch (JsonException)
        {
            return (extendedData, []);
        }
        if (root == null || !CustomFieldsProperties.Any(root.ContainsKey)) return (extendedData, []);

        var keys = new List<string>();
        foreach (var property in CustomFieldsProperties)
        {
            if (root[property] is JsonArray fields)
            {
                foreach (var field in fields.OfType<JsonObject>())
                {
                    var name = (field["name"] ?? field["Name"]) is JsonValue value && value.TryGetValue<string>(out var s)
                        ? MetadataEvaluator.NormalizeKey(s)
                        : "";
                    if (name.Length > 0 && !keys.Contains(name, MetadataEvaluator.KeyComparer))
                        keys.Add(name);
                }
            }
            root.Remove(property);
        }

        return (root.ToJsonString(), keys);
    }

    private static string? TranslateLegacyFilterKey(LocationAccessRuleRecord rule, List<string> warnings)
    {
        var legacy = rule.LegacyMetadataFilterKey;
        if (string.IsNullOrEmpty(legacy)) return null;

        if (rule.AccessLevel == "view")
        {
            if (legacy == ViewScopeAllSentinel) return ContentScope.ViewScopeAll;
            if (legacy == ViewScopeNoneSentinel) return ContentScope.ViewScopeNone;

            // The narrower reading: a view rule that cannot say it sees everything sees its scope.
            warnings.Add($"Access rule for '{ContentPackageImportRules.PrincipalName(rule)}' (view): scope '{legacy}' is not recognised and was imported as '{ContentScope.ViewScopeNone}'.");
            return ContentScope.ViewScopeNone;
        }

        warnings.Add($"Access rule for '{ContentPackageImportRules.PrincipalName(rule)}' ({rule.AccessLevel}): metadata filter key '{legacy}' is no longer used and was dropped.");
        return null;
    }
}
