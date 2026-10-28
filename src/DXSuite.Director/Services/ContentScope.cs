using DXSuite.Shared.Authorization;

namespace DXSuite.Director.Services;

/// <summary>The access level and view scope one access rule contributes to a resolve.</summary>
public sealed record AccessRuleGrant(string AccessLevel, string? ViewScope);

/// <summary>
/// Content scope: which assets a user can see at a location, decided by <see cref="MetadataEvaluator"/> with
/// the user as subject. Pure; the queries live in <see cref="ContentScopeService"/>. See
/// docs/spec/metadata-content-targeting.md sections 3.3, 3.7 and 4.4.
/// </summary>
public static class ContentScope
{
    /// <summary>A <c>view</c> rule whose holder sees every asset at the location, read-only.</summary>
    public const string ViewScopeAll = "all";

    /// <summary>A <c>view</c> rule whose holder sees what her values scope her to, read-only.</summary>
    public const string ViewScopeNone = "none";

    public static bool IsValidViewScope(string? viewScope) => viewScope is ViewScopeAll or ViewScopeNone;

    /// <summary>
    /// Highest level wins: full, then partial, then view. A <c>view</c> rule set to <c>all</c> widens what a
    /// partial holder sees without granting control, which stays with ownership. Null when no rule applies.
    /// </summary>
    public static AccessRuleGrant? Resolve(IEnumerable<AccessRuleGrant> matchingRules)
    {
        var rules = matchingRules.ToList();
        if (rules.Count == 0)
            return null;

        if (rules.Any(r => r.AccessLevel == "full"))
            return new AccessRuleGrant("full", null);

        var viewRules = rules.Where(r => r.AccessLevel == "view").ToList();
        string? viewScope = viewRules.Count == 0
            ? null
            : viewRules.Any(r => r.ViewScope == ViewScopeAll) ? ViewScopeAll : ViewScopeNone;

        return rules.Any(r => r.AccessLevel == "partial")
            ? new AccessRuleGrant("partial", viewScope)
            : new AccessRuleGrant("view", viewScope);
    }

    /// <summary>
    /// Whether the evaluator decides visibility. It does not for full access, for no rule (the location is
    /// open or the caller is an admin), or for a view rule set to <c>all</c>.
    /// </summary>
    public static bool IsEvaluated(string? accessLevel, string? viewScope) =>
        MetadataEvaluator.IsScopeEvaluated(accessLevel) && viewScope != ViewScopeAll;

    public static bool IsEvaluated(ResolvedAccess? access) => IsEvaluated(access?.AccessLevel, access?.ViewScope);

    /// <summary>
    /// A scoped rule whose subject holds no value on any participating key is a configuration error, not a
    /// grant: at runtime it sees nothing, so the access rule editor refuses to save it.
    /// </summary>
    public static bool ResolvesToNothing(
        string accessLevel, string? viewScope,
        LocationMetadataRules rules,
        IReadOnlyDictionary<string, IReadOnlySet<string>> subjectValues) =>
        IsEvaluated(accessLevel, viewScope)
        && MetadataEvaluator.IsGated(rules, subjectValues, BlankSubjectPolicy.AlwaysAbstain);
}

/// <summary>
/// Everything a scope call site needs for one user at one location, loaded once per page. Approval is
/// deliberately absent: a scoped manager sees Pending content in her scope, and ownership decides what she
/// may do with it.
/// </summary>
public sealed class ContentScopeContext
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> NoValues =
        new Dictionary<string, IReadOnlySet<string>>(MetadataEvaluator.KeyComparer);

    public ResolvedAccess? Access { get; }

    /// <summary>The location's rule set. Read whether or not its targeting switch is on, which gates targeting only.</summary>
    public LocationMetadataRules Rules { get; }

    public IReadOnlyDictionary<string, IReadOnlySet<string>> Subject { get; }

    public ContentScopeContext(
        ResolvedAccess? access,
        LocationMetadataRules rules,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? subject)
    {
        Access = access;
        Rules = rules;
        Subject = subject ?? NoValues;
    }

    /// <summary>A context that shows everything, for callers with no rule restricting them.</summary>
    public static ContentScopeContext Unrestricted(LocationMetadataRules rules) => new(null, rules, null);

    public bool IsEvaluated => ContentScope.IsEvaluated(Access);

    /// <summary>The participating keys, ordered, for filter lists and columns.</summary>
    public IReadOnlyList<string> ParticipatingKeys => Rules.Participating.Select(r => r.Key).ToList();

    /// <param name="assetValues">Null for a file with no asset record, which stays visible as legacy content.</param>
    public bool IsVisible(IReadOnlyDictionary<string, IReadOnlySet<string>>? assetValues) =>
        !IsEvaluated
        || MetadataEvaluator.Evaluate(Rules, assetValues, Subject, BlankSubjectPolicy.AlwaysAbstain).Included;
}
