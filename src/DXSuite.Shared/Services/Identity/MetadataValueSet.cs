namespace DXSuite.Shared.Services.Identity;

/// <summary>
/// The rows to write so that one key on one subject holds exactly a desired set of values.
/// </summary>
/// <param name="Updates">Stored rows whose value changes, by index into the stored list.</param>
/// <param name="Removes">Stored rows to delete, by index into the stored list.</param>
/// <param name="Adds">Values that need a new row.</param>
public sealed record MetadataValueReconciliation(
    IReadOnlyList<(int StoredIndex, string Value)> Updates,
    IReadOnlyList<int> Removes,
    IReadOnlyList<string> Adds)
{
    public bool IsEmpty => Updates.Count == 0 && Removes.Count == 0 && Adds.Count == 0;
}

/// <summary>
/// How every metadata store (asset, device, user, group) turns an edited value set into rows. A key
/// holds a set of values, one row each; nothing is comma-joined. Values are trimmed on write and nothing
/// else: comparison is already case-insensitive, so folding case would only corrupt what is displayed,
/// and "042" and "42" are different values.
/// </summary>
public static class MetadataValueSet
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Trimmed, non-blank and distinct, in the order given. Two values differing only in case are one
    /// value to the database's case-insensitive collation, so the first spelling is kept.
    /// </summary>
    public static List<string> Normalize(IEnumerable<string?>? values)
    {
        var seen = new HashSet<string>(Comparer);
        var result = new List<string>();
        foreach (var raw in values ?? [])
        {
            var value = raw?.Trim();
            if (!string.IsNullOrEmpty(value) && seen.Add(value))
                result.Add(value);
        }
        return result;
    }

    /// <summary>
    /// Plans the writes that turn <paramref name="stored"/> into <paramref name="desired"/>.
    ///
    /// A stored value matching a desired one case-insensitively after trimming keeps its row, rewritten
    /// only if the spelling differs. Leftover rows are then reused for leftover values before any are
    /// deleted. Matching first means no update can collide with another stored row under a unique index,
    /// because every leftover value differs from every stored value.
    /// </summary>
    public static MetadataValueReconciliation Reconcile(IReadOnlyList<string> stored, IEnumerable<string?>? desired)
    {
        var wanted = Normalize(desired);
        var updates = new List<(int, string)>();
        var unmatchedRows = new List<int>();

        for (var i = 0; i < stored.Count; i++)
        {
            var current = stored[i] ?? "";
            var match = wanted.FindIndex(v => Comparer.Equals(v, current.Trim()));
            if (match < 0)
            {
                unmatchedRows.Add(i);
                continue;
            }

            if (!string.Equals(current, wanted[match], StringComparison.Ordinal))
                updates.Add((i, wanted[match]));
            wanted.RemoveAt(match);
        }

        var reused = Math.Min(unmatchedRows.Count, wanted.Count);
        for (var i = 0; i < reused; i++)
            updates.Add((unmatchedRows[i], wanted[i]));

        return new MetadataValueReconciliation(
            updates,
            unmatchedRows.Skip(reused).ToList(),
            wanted.Skip(reused).ToList());
    }
}
