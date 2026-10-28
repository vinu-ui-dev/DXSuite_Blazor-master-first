namespace DXSuite.Shared.Services.Identity;

/// <summary>
/// The field types a registry key may declare. Anything else is refused on write, so an editor meeting an
/// unknown type is looking at data written around the registry.
/// </summary>
public static class MetadataFieldTypes
{
    public const string String = "string";
    public const string Int = "int";
    public const string List = "list";
    public const string MultiSelect = "multi-select";

    public static IReadOnlyList<string> All { get; } = [String, Int, List, MultiSelect];

    public static bool IsKnown(string? fieldType) => fieldType is not null && All.Contains(fieldType);
}

/// <summary>
/// The two ways a store uses a key, reported apart because they fail differently. An orphaned tag is a
/// value nothing can pick any more and is invisible; an orphaned reference is configuration that names a
/// key the registry no longer describes, and degrades a screen.
/// </summary>
public enum MetadataUsageKind
{
    Tag,
    Reference
}

/// <summary>How many rows of one store use a key, or a key's values.</summary>
public sealed record MetadataUsageCount(MetadataUsageKind Kind, string Singular, string Plural, int Count)
{
    public override string ToString() => $"{Count} {(Count == 1 ? Singular : Plural)}";
}

/// <summary>
/// Counts one module's own stores for the registry. The registry lives in Shared and cannot read Director
/// tables, so each module that stores keys contributes its counts instead.
/// </summary>
public interface IMetadataUsageContributor
{
    /// <summary>Rows in this module's stores that use the key, tags and references alike.</summary>
    Task<IReadOnlyList<MetadataUsageCount>> CountKeyAsync(string key, CancellationToken ct = default);

    /// <summary>Rows in this module's stores holding any of <paramref name="values"/> for the key.</summary>
    Task<IReadOnlyList<MetadataUsageCount>> CountValuesAsync(string key, IReadOnlyCollection<string> values, CancellationToken ct = default);
}

/// <summary>Every store's use of a key, or of some of its values, across modules.</summary>
public class KeyUsageSummary
{
    public IReadOnlyList<MetadataUsageCount> Counts { get; init; } = [];

    public bool IsUsed => Counts.Any(c => c.Count > 0);

    /// <summary>"44 assets and 1 device", or null when no tag store uses it.</summary>
    public string? DescribeTags() => Describe(MetadataUsageKind.Tag);

    /// <summary>"50 displays and 4 access schedules", or null when no reference store uses it.</summary>
    public string? DescribeReferences() => Describe(MetadataUsageKind.Reference);

    private string? Describe(MetadataUsageKind kind)
    {
        var parts = Counts.Where(c => c.Kind == kind && c.Count > 0).Select(c => c.ToString()).ToList();
        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}"
        };
    }
}
