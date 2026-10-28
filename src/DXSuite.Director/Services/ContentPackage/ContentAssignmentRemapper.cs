using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Remaps the asset ids embedded in a TblScheduleEvent.ContentAssignment JSON blob
/// (<c>{ "Content": [ { "AssetId": int, ... } ] }</c>) between environments during content packaging.
///
/// Export tokenizes each AssetId to that asset's ContentPackageId (a GUID string); import resolves the
/// GUID back to the target asset id. Content items whose asset cannot be resolved in the target are
/// dropped rather than left pointing at a non-existent asset (which would render as a broken display).
/// All other JSON is preserved via JsonNode round-tripping.
/// </summary>
internal static class ContentAssignmentRemapper
{
    /// <summary>Returns the integer asset ids referenced under Content[].AssetId.</summary>
    public static IReadOnlyList<int> ExtractAssetIds(string? contentAssignment)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(contentAssignment)) return ids;

        JsonNode? root;
        try { root = JsonNode.Parse(contentAssignment); }
        catch { return ids; }

        var content = GetArrayProp(root, "Content");
        if (content == null) return ids;

        foreach (var item in content)
        {
            if (item is not JsonObject obj) continue;
            var key = GetKey(obj, "AssetId");
            if (key != null && obj[key] is JsonValue v && v.TryGetValue<int>(out var id))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>Returns the asset ContentPackageId GUID tokens currently in Content[].AssetId.</summary>
    public static IReadOnlyList<Guid> ExtractAssetGuids(string? contentAssignment)
    {
        var guids = new List<Guid>();
        if (string.IsNullOrWhiteSpace(contentAssignment)) return guids;

        JsonNode? root;
        try { root = JsonNode.Parse(contentAssignment); }
        catch { return guids; }

        var content = GetArrayProp(root, "Content");
        if (content == null) return guids;

        foreach (var item in content)
        {
            if (item is not JsonObject obj) continue;
            var key = GetKey(obj, "AssetId");
            if (key != null && obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var guid))
                guids.Add(guid);
        }
        return guids;
    }

    /// <summary>Replaces each Content[].AssetId with the asset's ContentPackageId GUID (as a string).</summary>
    public static string? TokenizeAssetIds(string? contentAssignment, IReadOnlyDictionary<int, Guid> assetIdToGuid)
    {
        if (string.IsNullOrWhiteSpace(contentAssignment)) return contentAssignment;

        JsonNode? root;
        try { root = JsonNode.Parse(contentAssignment); }
        catch { return contentAssignment; }

        var content = GetArrayProp(root, "Content");
        if (content == null) return contentAssignment;

        bool changed = false;
        foreach (var item in content)
        {
            if (item is not JsonObject obj) continue;
            var key = GetKey(obj, "AssetId");
            if (key != null && obj[key] is JsonValue v && v.TryGetValue<int>(out var id)
                && assetIdToGuid.TryGetValue(id, out var guid))
            {
                obj[key] = JsonValue.Create(guid.ToString());
                changed = true;
            }
        }
        return changed ? root!.ToJsonString() : contentAssignment;
    }

    /// <summary>
    /// Replaces each Content[].AssetId GUID token with its target asset id. Items whose GUID does not
    /// resolve (their asset is absent from the target) are dropped and reported in <paramref name="unresolved"/>.
    /// Returns the original string unchanged when there are no tokens to resolve.
    /// </summary>
    public static string? DetokenizeAssetIds(
        string? contentAssignment,
        IReadOnlyDictionary<Guid, int> guidToAssetId,
        out List<Guid> unresolved)
    {
        unresolved = new List<Guid>();
        if (string.IsNullOrWhiteSpace(contentAssignment)) return contentAssignment;

        JsonNode? root;
        try { root = JsonNode.Parse(contentAssignment); }
        catch { return contentAssignment; }

        if (root is not JsonObject rootObj) return contentAssignment;
        var contentKey = GetKey(rootObj, "Content");
        if (contentKey == null || rootObj[contentKey] is not JsonArray content) return contentAssignment;

        bool changed = false;
        var kept = new JsonArray();
        foreach (var item in content)
        {
            if (item is not JsonObject obj) { kept.Add(item?.DeepClone()); continue; }

            var key = GetKey(obj, "AssetId");
            if (key != null && obj[key] is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var guid))
            {
                changed = true;
                if (guidToAssetId.TryGetValue(guid, out var newId))
                {
                    var clone = (JsonObject)obj.DeepClone();
                    clone[key] = JsonValue.Create(newId);
                    kept.Add(clone);
                }
                else
                {
                    unresolved.Add(guid); // asset not present in target — drop this item
                }
            }
            else
            {
                kept.Add(obj.DeepClone()); // already an int / no AssetId — keep as-is
            }
        }

        if (!changed) return contentAssignment;
        rootObj[contentKey] = kept;
        return rootObj.ToJsonString();
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
