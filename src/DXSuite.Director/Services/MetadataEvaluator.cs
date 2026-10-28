namespace DXSuite.Director.Services;

/// <summary>How a location combines its per-key verdicts. Shared by targeting and scope.</summary>
public enum MetadataCombineMode
{
    /// <summary>Exclude if any key returned NoMatch.</summary>
    All,

    /// <summary>Include if any key returned Match, otherwise include only if no key returned NoMatch.</summary>
    Any,
}

/// <summary>Governs assets holding no value for a key. Stored as <c>all</c> or <c>none</c>.</summary>
public enum UntaggedContentPolicy
{
    All,
    None,
}

/// <summary>Governs subjects holding no value for a key. Stored as <c>all</c>, <c>untaggedOnly</c> or <c>none</c>.</summary>
public enum UntaggedPlayerPolicy
{
    All,
    UntaggedOnly,

    /// <summary>A player blank on this key receives nothing at the location. Applied as the stage 0 gate, not as a verdict.</summary>
    None,
}

/// <summary>The one subject-side divergence between the two mechanisms.</summary>
public enum BlankSubjectPolicy
{
    /// <summary>Content targeting: a blank player is governed by the key's <see cref="UntaggedPlayerPolicy"/>.</summary>
    ApplyKeyRules,

    /// <summary>Content scope: a user blank on a key is unscoped on that dimension, guarded by the stage 0 gate.</summary>
    AlwaysAbstain,
}

/// <summary>
/// Three states rather than a boolean, because "this key does not match" and "this key has nothing
/// to say" combine differently under ALL and ANY.
/// </summary>
public enum MetadataVerdict
{
    Match,
    NoMatch,
    Abstain,
}

/// <summary>One participating key's blank policies. The coded defaults are the pair that preserves pre-targeting playback.</summary>
public sealed record MetadataKeyRule(
    string Key,
    UntaggedContentPolicy UntaggedContent = UntaggedContentPolicy.All,
    UntaggedPlayerPolicy UntaggedPlayers = UntaggedPlayerPolicy.All);

public sealed record MetadataKeyVerdict(string Key, MetadataVerdict Verdict);

/// <param name="Included">The metadata decision only. Targeting call sites AND approval onto this themselves.</param>
/// <param name="Gated">Stage 0 excluded the subject before any key was examined; <paramref name="Verdicts"/> is then empty.</param>
/// <param name="Verdicts">Per-key verdicts ordered by key, including an Abstain for each orphaned key.</param>
public sealed record MetadataEvaluation(bool Included, bool Gated, IReadOnlyList<MetadataKeyVerdict> Verdicts);

/// <param name="Receiving">Players the asset would reach.</param>
/// <param name="Gated">Players the stage 0 gate excluded before matching, counted apart because the fix differs.</param>
/// <param name="Total">Every player evaluated, the denominator for the other two.</param>
public sealed record MetadataReach(int Receiving, int Gated, int Total);

/// <summary>
/// A location's rule set as the evaluator reads it. Participation is row presence plus registry
/// membership: a rule whose key the registry no longer defines is orphaned, abstains, and is surfaced
/// so the location can warn rather than silently change what plays.
/// </summary>
public sealed class LocationMetadataRules
{
    // Stored sentinels on view access rules, which a rule set can be handed by a careless caller.
    private static readonly HashSet<string> Sentinels = new(StringComparer.OrdinalIgnoreCase) { "__all__", "__none__" };

    public MetadataCombineMode Combine { get; }

    /// <summary>Rules whose key exists in the registry, ordered by key.</summary>
    public IReadOnlyList<MetadataKeyRule> Participating { get; }

    /// <summary>Keys named by a rule but absent from the registry, ordered. The location shows a warning naming them.</summary>
    public IReadOnlyList<string> OrphanedKeys { get; }

    public LocationMetadataRules(
        MetadataCombineMode combine,
        IEnumerable<MetadataKeyRule> rules,
        IEnumerable<string> registryKeys)
    {
        var registry = new HashSet<string>(
            registryKeys.Select(MetadataEvaluator.NormalizeKey).Where(k => k.Length > 0),
            MetadataEvaluator.KeyComparer);

        var named = rules
            .Select(r => r with { Key = MetadataEvaluator.NormalizeKey(r.Key) })
            .Where(r => r.Key.Length > 0 && !Sentinels.Contains(r.Key))
            .DistinctBy(r => r.Key, MetadataEvaluator.KeyComparer)
            .OrderBy(r => r.Key, MetadataEvaluator.KeyComparer)
            .ToList();

        Combine = combine;
        Participating = named.Where(r => registry.Contains(r.Key)).ToList();
        OrphanedKeys = named.Where(r => !registry.Contains(r.Key)).Select(r => r.Key).ToList();
    }
}

/// <summary>
/// The single metadata evaluator behind content targeting (asset against player) and content scope
/// (asset against user). Pure: no I/O, no async, no approval. See docs/spec/metadata-content-targeting.md
/// section 3; the 12-row table in <see cref="Verdict"/> is section 3.4.
/// </summary>
public static class MetadataEvaluator
{
    /// <summary>
    /// Values compare case-insensitively, trimmed, ordinal: ordinal so locale casing rules cannot change
    /// what plays. Every value set is materialized through <see cref="NormalizeValues"/> first.
    /// </summary>
    public static readonly StringComparer ValueComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Key columns are CI_AS in the database, so keys that collide there collide here.</summary>
    public static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(ValueComparer);

    public static string NormalizeKey(string? key) => key?.Trim() ?? string.Empty;

    /// <summary>
    /// Trims each value and drops blanks, so null, empty and whitespace-only all mean "no value".
    /// Never splits: a comma is part of the value.
    /// </summary>
    public static IReadOnlySet<string> NormalizeValues(IEnumerable<string?>? values)
    {
        if (values is null)
            return Empty;

        var set = new HashSet<string>(ValueComparer);
        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                set.Add(trimmed);
        }
        return set;
    }

    /// <summary>
    /// A user's subject values: her own unioned with those of every group she belongs to. The caller
    /// passes current membership; nothing here is retained between evaluations.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> UserSubject(
        IReadOnlyDictionary<string, IReadOnlySet<string>>? userValues,
        IEnumerable<IReadOnlyDictionary<string, IReadOnlySet<string>>> currentGroupValues)
    {
        var sources = new List<IReadOnlyDictionary<string, IReadOnlySet<string>>>();
        if (userValues is not null)
            sources.Add(userValues);
        sources.AddRange(currentGroupValues);
        return Normalize(sources);
    }

    /// <summary>Whether scope applies at all. It never runs for <c>full</c> rules, so neither does its gate.</summary>
    public static bool IsScopeEvaluated(string? accessLevel) =>
        string.Equals(accessLevel, "partial", StringComparison.OrdinalIgnoreCase)
        || string.Equals(accessLevel, "view", StringComparison.OrdinalIgnoreCase);

    /// <param name="assetValues">
    /// Null for a file on disk with no asset record. Such a file is legacy content and is included
    /// untargeted, because excluding it would black out a location the moment it opts in; the rule
    /// builder counts these instead.
    /// </param>
    public static MetadataEvaluation Evaluate(
        LocationMetadataRules rules,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? assetValues,
        IReadOnlyDictionary<string, IReadOnlySet<string>> subjectValues,
        BlankSubjectPolicy blankSubjectPolicy)
    {
        if (assetValues is null)
            return new MetadataEvaluation(true, false, []);

        var subject = Normalize([subjectValues]);
        if (IsGatedNormalized(rules, subject, blankSubjectPolicy))
            return new MetadataEvaluation(false, true, []);

        return EvaluateUngated(rules, Normalize([assetValues]), subject, blankSubjectPolicy);
    }

    /// <summary>
    /// Stage 0, evaluated once per subject. Targeting: gated when any participating key sets
    /// <see cref="UntaggedPlayerPolicy.None"/> and the player is blank on it, and the gate beats every
    /// other key. Scope: gated when the user holds no value on any participating key, so a scoped user
    /// with no values sees nothing rather than everything.
    /// </summary>
    public static bool IsGated(
        LocationMetadataRules rules,
        IReadOnlyDictionary<string, IReadOnlySet<string>> subjectValues,
        BlankSubjectPolicy blankSubjectPolicy) =>
        IsGatedNormalized(rules, Normalize([subjectValues]), blankSubjectPolicy);

    /// <summary>
    /// How many players one asset would reach, from values rather than rows, for the upload form's reach
    /// line and the rule builder's coverage counters. Always targeting.
    /// </summary>
    public static MetadataReach Reach(
        LocationMetadataRules rules,
        IReadOnlyDictionary<string, IReadOnlySet<string>> assetValues,
        IEnumerable<IReadOnlyDictionary<string, IReadOnlySet<string>>> playerValues)
    {
        var asset = Normalize([assetValues]);
        int receiving = 0, gated = 0, total = 0;

        foreach (var player in playerValues)
        {
            total++;
            var subject = Normalize([player]);
            if (IsGatedNormalized(rules, subject, BlankSubjectPolicy.ApplyKeyRules))
                gated++;
            else if (EvaluateUngated(rules, asset, subject, BlankSubjectPolicy.ApplyKeyRules).Included)
                receiving++;
        }

        return new MetadataReach(receiving, gated, total);
    }

    /// <summary>
    /// Stage 1, one key. <paramref name="assetValues"/> and <paramref name="subjectValues"/> are normalized
    /// here, so any set may be passed.
    /// </summary>
    public static MetadataVerdict Verdict(
        MetadataKeyRule rule,
        IReadOnlySet<string> assetValues,
        IReadOnlySet<string> subjectValues,
        BlankSubjectPolicy blankSubjectPolicy) =>
        VerdictNormalized(rule, NormalizeValues(assetValues), NormalizeValues(subjectValues), blankSubjectPolicy);

    private static MetadataVerdict VerdictNormalized(
        MetadataKeyRule rule,
        IReadOnlySet<string> a,
        IReadOnlySet<string> s,
        BlankSubjectPolicy blankSubjectPolicy)
    {
        // Row 1: any-of-any in both directions.
        if (a.Count > 0 && s.Count > 0)
            return a.Overlaps(s) ? MetadataVerdict.Match : MetadataVerdict.NoMatch;

        // Rows 2, 3.
        if (a.Count == 0 && s.Count > 0)
            return rule.UntaggedContent == UntaggedContentPolicy.All ? MetadataVerdict.Abstain : MetadataVerdict.NoMatch;

        if (blankSubjectPolicy == BlankSubjectPolicy.AlwaysAbstain)
        {
            // Row 6, then rows 11 and 12: the asset-side policy still governs an untagged asset.
            if (a.Count > 0)
                return MetadataVerdict.Abstain;
            return rule.UntaggedContent == UntaggedContentPolicy.All ? MetadataVerdict.Abstain : MetadataVerdict.NoMatch;
        }

        // A player blank on a None key never reaches a verdict through Evaluate, which gates it first.
        // Standalone callers get NoMatch, the answer the gate gives.
        if (rule.UntaggedPlayers == UntaggedPlayerPolicy.None)
            return MetadataVerdict.NoMatch;

        // Rows 4, 5.
        if (a.Count > 0)
            return rule.UntaggedPlayers == UntaggedPlayerPolicy.All ? MetadataVerdict.Abstain : MetadataVerdict.NoMatch;

        // Rows 9, 10, then 7 and 8.
        if (rule.UntaggedContent == UntaggedContentPolicy.None)
            return MetadataVerdict.NoMatch;
        return rule.UntaggedPlayers == UntaggedPlayerPolicy.UntaggedOnly ? MetadataVerdict.Match : MetadataVerdict.Abstain;
    }

    /// <summary>Stage 2. Abstains count neither way, so a location with no participating keys includes everything.</summary>
    public static bool Combine(MetadataCombineMode mode, IEnumerable<MetadataVerdict> verdicts)
    {
        bool anyMatch = false, anyNoMatch = false;
        foreach (var verdict in verdicts)
        {
            anyMatch |= verdict == MetadataVerdict.Match;
            anyNoMatch |= verdict == MetadataVerdict.NoMatch;
        }

        return mode == MetadataCombineMode.All
            ? !anyNoMatch
            : anyMatch || !anyNoMatch;
    }

    private static bool IsGatedNormalized(
        LocationMetadataRules rules,
        IReadOnlyDictionary<string, IReadOnlySet<string>> subject,
        BlankSubjectPolicy blankSubjectPolicy)
    {
        if (blankSubjectPolicy == BlankSubjectPolicy.AlwaysAbstain)
            return !rules.Participating.Any(r => ValuesFor(subject, r.Key).Count > 0);

        return rules.Participating.Any(r =>
            r.UntaggedPlayers == UntaggedPlayerPolicy.None && ValuesFor(subject, r.Key).Count == 0);
    }

    private static MetadataEvaluation EvaluateUngated(
        LocationMetadataRules rules,
        IReadOnlyDictionary<string, IReadOnlySet<string>> asset,
        IReadOnlyDictionary<string, IReadOnlySet<string>> subject,
        BlankSubjectPolicy blankSubjectPolicy)
    {
        var verdicts = rules.Participating
            .Select(r => new MetadataKeyVerdict(r.Key, VerdictNormalized(r,ValuesFor(asset, r.Key), ValuesFor(subject, r.Key), blankSubjectPolicy)))
            .Concat(rules.OrphanedKeys.Select(k => new MetadataKeyVerdict(k, MetadataVerdict.Abstain)))
            .OrderBy(v => v.Key, KeyComparer)
            .ToList();

        return new MetadataEvaluation(Combine(rules.Combine, verdicts.Select(v => v.Verdict)), false, verdicts);
    }

    private static IReadOnlySet<string> ValuesFor(IReadOnlyDictionary<string, IReadOnlySet<string>> values, string key) =>
        values.TryGetValue(key, out var set) ? set : Empty;

    // Re-keys under KeyComparer and unions keys that collide once trimmed, so a caller's dictionary
    // comparer cannot decide whether a key is found.
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Normalize(
        IEnumerable<IReadOnlyDictionary<string, IReadOnlySet<string>>> sources)
    {
        var merged = new Dictionary<string, List<string>>(KeyComparer);
        foreach (var source in sources)
        {
            foreach (var (rawKey, values) in source)
            {
                var key = NormalizeKey(rawKey);
                if (key.Length == 0)
                    continue;
                if (!merged.TryGetValue(key, out var list))
                    merged[key] = list = [];
                list.AddRange(values);
            }
        }

        return merged.ToDictionary(kv => kv.Key, kv => NormalizeValues(kv.Value), KeyComparer);
    }
}
