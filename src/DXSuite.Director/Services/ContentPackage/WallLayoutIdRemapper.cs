using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Translates the integer TblDisplay primary keys embedded in a VideoWall location's
/// ExtendedData layout JSON between environments during content packaging.
///
/// A VideoWall stores its layout in TblLocation.ExtendedData as
/// <c>{ "layout": { "groups": [{ "displayIds": [int,...] }], "placements": [{ "displayId": int }] } }</c>.
/// Those ids are source-environment primary keys, so a verbatim copy points at the wrong
/// (or non-existent) displays in the target. Export "tokenizes" each id to that display's
/// ContentPackageId (a stable GUID); import "detokenizes" the GUID back to the new target id
/// once every display has been recreated.
///
/// Locations whose ExtendedData has no <c>layout</c> object are left untouched, and all JSON
/// other than the display-id slots is preserved verbatim via JsonNode round-tripping.
/// </summary>
internal static class WallLayoutIdRemapper
{
    /// <summary>
    /// Replaces each source display id under layout.groups[].displayIds and
    /// layout.placements[].displayId with its ContentPackageId GUID (as a string).
    /// Ids not present in <paramref name="displayIdToGuid"/> are left as integers.
    /// Returns the original string unchanged when there is no wall layout or nothing to remap.
    /// </summary>
    public static string? TokenizeDisplayIds(string? extendedData, IReadOnlyDictionary<int, Guid> displayIdToGuid)
    {
        if (string.IsNullOrWhiteSpace(extendedData)) return extendedData;

        JsonNode? root;
        try { root = JsonNode.Parse(extendedData); }
        catch { return extendedData; }

        var layout = GetObjectProp(root, "layout");
        if (layout == null) return extendedData;

        bool changed = false;

        foreach (var idsArray in EnumerateGroupDisplayIdArrays(layout))
        {
            for (int i = 0; i < idsArray.Count; i++)
            {
                if (idsArray[i] is JsonValue v && v.TryGetValue<int>(out var id)
                    && displayIdToGuid.TryGetValue(id, out var guid))
                {
                    idsArray[i] = JsonValue.Create(guid.ToString());
                    changed = true;
                }
            }
        }

        foreach (var (placement, key) in EnumeratePlacementDisplayIdSlots(layout))
        {
            if (placement[key] is JsonValue v && v.TryGetValue<int>(out var id)
                && displayIdToGuid.TryGetValue(id, out var guid))
            {
                placement[key] = JsonValue.Create(guid.ToString());
                changed = true;
            }
        }

        return changed ? root!.ToJsonString() : extendedData;
    }

    /// <summary>
    /// Reverses <see cref="TokenizeDisplayIds"/>: replaces each ContentPackageId GUID token
    /// with its new target display id. GUIDs absent from <paramref name="guidToDisplayId"/> are
    /// collected in <paramref name="unresolved"/> and left in place (they will not resolve at
    /// render time but do not corrupt the JSON). Returns the rewritten JSON when the location is
    /// a tokenized wall layout, or null when there is nothing to detokenize (e.g. a non-wall location).
    /// </summary>
    public static string? DetokenizeDisplayIds(
        string? extendedData,
        IReadOnlyDictionary<Guid, int> guidToDisplayId,
        out List<Guid> unresolved)
    {
        unresolved = new List<Guid>();
        if (string.IsNullOrWhiteSpace(extendedData)) return null;

        JsonNode? root;
        try { root = JsonNode.Parse(extendedData); }
        catch { return null; }

        var layout = GetObjectProp(root, "layout");
        if (layout == null) return null;

        bool changed = false;

        foreach (var idsArray in EnumerateGroupDisplayIdArrays(layout))
        {
            for (int i = 0; i < idsArray.Count; i++)
            {
                if (idsArray[i] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var guid))
                {
                    if (guidToDisplayId.TryGetValue(guid, out var newId)) { idsArray[i] = JsonValue.Create(newId); changed = true; }
                    else unresolved.Add(guid);
                }
            }
        }

        foreach (var (placement, key) in EnumeratePlacementDisplayIdSlots(layout))
        {
            if (placement[key] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var guid))
            {
                if (guidToDisplayId.TryGetValue(guid, out var newId)) { placement[key] = JsonValue.Create(newId); changed = true; }
                else unresolved.Add(guid);
            }
        }

        return (changed || unresolved.Count > 0) ? root!.ToJsonString() : null;
    }

    private static IEnumerable<JsonArray> EnumerateGroupDisplayIdArrays(JsonObject layout)
    {
        var groups = GetArrayProp(layout, "groups");
        if (groups == null) yield break;
        foreach (var group in groups)
        {
            var ids = GetArrayProp(group, "displayIds");
            if (ids != null) yield return ids;
        }
    }

    private static IEnumerable<(JsonObject Placement, string Key)> EnumeratePlacementDisplayIdSlots(JsonObject layout)
    {
        var placements = GetArrayProp(layout, "placements");
        if (placements == null) yield break;
        foreach (var placement in placements)
        {
            if (placement is not JsonObject po) continue;
            var key = GetKey(po, "displayId");
            if (key != null) yield return (po, key);
        }
    }

    // ExtendedData is serialized camelCase, but read elsewhere with case-insensitive options;
    // these lookups match either casing so the remap is robust to future serialization changes.
    private static JsonObject? GetObjectProp(JsonNode? node, string name)
    {
        if (node is not JsonObject obj) return null;
        foreach (var kv in obj)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Value as JsonObject;
        return null;
    }

    private static JsonArray? GetArrayProp(JsonNode? node, string name)
    {
        if (node is not JsonObject obj) return null;
        foreach (var kv in obj)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Value as JsonArray;
        return null;
    }

    private static string? GetKey(JsonObject obj, string name)
    {
        foreach (var kv in obj)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        return null;
    }
}
