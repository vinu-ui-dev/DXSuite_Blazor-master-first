namespace DXSuite.Director.Services;

/// <summary>A key an asset still holds that the editor cannot offer as a field, with its values ordered.</summary>
public sealed record OrphanedTag(string Key, IReadOnlyList<string> Values);

/// <summary>
/// The pure decisions behind the per-asset metadata editor (spec section 13.3). Queries live in the modal.
/// </summary>
public static class AssetMetadataEdit
{
    /// <summary>
    /// Keys the asset holds that are not participating at its location, ordered by key. Registry membership
    /// does not matter: a key with no rule row here and a key the registry never defined both belong, and so
    /// does a rule row whose key the registry dropped, since it neither targets nor has a definition to edit.
    /// </summary>
    public static IReadOnlyList<OrphanedTag> OrphanedTags(
        IReadOnlyDictionary<string, IReadOnlySet<string>> assetValues, IEnumerable<string> participatingKeys)
    {
        var participating = new HashSet<string>(
            participatingKeys.Select(MetadataEvaluator.NormalizeKey), MetadataEvaluator.KeyComparer);

        return assetValues
            .Where(kv => kv.Value.Count > 0 && !participating.Contains(MetadataEvaluator.NormalizeKey(kv.Key)))
            .Select(kv => new OrphanedTag(kv.Key, kv.Value.Order(StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(t => t.Key, MetadataEvaluator.KeyComparer)
            .ToList();
    }
}
