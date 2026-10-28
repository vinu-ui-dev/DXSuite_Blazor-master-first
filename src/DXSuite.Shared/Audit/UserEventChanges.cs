namespace DXSuite.Shared.Audit;

/// <summary>
/// A field-by-field before/after diff for a <see cref="UserEvent"/>'s <c>Details</c> (AUD-08: every edit logs the
/// old and new value of each field it actually changed). Fluent builder methods add an entry and return this, so a
/// caller chains one call per field; the plain dictionary indexer is also available for a single ad-hoc entry.
/// </summary>
public sealed class UserEventChanges : Dictionary<string, UserEventChanges.Change>
{
    /// <param name="Before">The old value, or null for a field a new row has no prior value for.</param>
    /// <param name="After">The new value.</param>
    public sealed record Change(object? Before, object? After);

    /// <summary>Adds <paramref name="field"/> unconditionally, even when <paramref name="before"/> equals
    /// <paramref name="after"/> — used for a field whose presence alone (not its value) is the thing being logged,
    /// and for every field of a brand new row.</summary>
    public UserEventChanges Always(string field, object? before, object? after)
    {
        this[field] = new Change(before, after);
        return this;
    }

    /// <summary>Adds <paramref name="field"/> only when <paramref name="before"/> and <paramref name="after"/>
    /// differ, so an edit that left a field untouched does not log a no-op change for it.</summary>
    public UserEventChanges Field(string field, object? before, object? after)
    {
        if (!Equals(before, after)) this[field] = new Change(before, after);
        return this;
    }

    /// <summary>Adds <paramref name="field"/> only when the two sequences differ, in order — these lists (e.g.
    /// Message Type languages, asset slots) are order-significant, so a reorder alone is a change.</summary>
    public UserEventChanges List<T>(string field, IReadOnlyCollection<T>? before, IReadOnlyCollection<T>? after)
    {
        var a = before ?? [];
        var b = after ?? [];
        if (!a.SequenceEqual(b)) this[field] = new Change(a, b);
        return this;
    }
}
