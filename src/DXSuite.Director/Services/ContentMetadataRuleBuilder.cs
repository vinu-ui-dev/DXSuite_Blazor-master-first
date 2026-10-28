namespace DXSuite.Director.Services;

/// <summary>A piece of content as the rule builder counts it.</summary>
/// <param name="Values">Null for a file with no asset record, which is included untargeted everywhere.</param>
public sealed record RuleBuilderAsset(IReadOnlyDictionary<string, IReadOnlySet<string>>? Values);

public sealed record RuleBuilderPlayer(string Name, IReadOnlyDictionary<string, IReadOnlySet<string>> Values);

/// <summary>
/// The rule builder's blast radius. The two failure counts are kept apart because they are fixed
/// differently: a matched-by-nothing player needs content or a looser rule, a gated player needs a value.
/// </summary>
/// <param name="MatchedByNoContent">Players the matrix left with nothing.</param>
/// <param name="ExcludedBeforeMatching">Players the stage 0 gate left with nothing.</param>
/// <param name="Receiving">Players receiving at least one asset.</param>
/// <param name="Total">Every player at the location, the denominator for the other three.</param>
public sealed record RuleCoverage(
    IReadOnlyList<string> MatchedByNoContent,
    IReadOnlyList<string> ExcludedBeforeMatching,
    int Receiving,
    int Total)
{
    public int ReceivingNothing => MatchedByNoContent.Count + ExcludedBeforeMatching.Count;
}

/// <summary>A key tagged on content at the location that no rule names.</summary>
public sealed record UndeclaredKey(string Key, int Assets);

/// <summary>
/// The pure decisions behind the content metadata rule builder (spec section 5). Queries live in
/// <see cref="ContentMetadataRuleEditor"/>; every count here goes through <see cref="MetadataEvaluator"/>.
/// </summary>
public static class ContentMetadataRuleBuilder
{
    public static RuleCoverage Coverage(
        LocationMetadataRules rules,
        IReadOnlyList<RuleBuilderAsset> assets,
        IReadOnlyList<RuleBuilderPlayer> players)
    {
        var matchedByNothing = new List<string>();
        var gated = new List<string>();

        // A file with no asset record plays untargeted and escapes the gate too, exactly as delivery does.
        var hasRecordlessFile = assets.Any(a => a.Values == null);
        var tagged = assets.Where(a => a.Values != null).ToList();

        foreach (var player in players)
        {
            if (hasRecordlessFile)
                continue;
            if (MetadataEvaluator.IsGated(rules, player.Values, BlankSubjectPolicy.ApplyKeyRules))
                gated.Add(player.Name);
            else if (!tagged.Any(a => MetadataEvaluator.Evaluate(rules, a.Values, player.Values, BlankSubjectPolicy.ApplyKeyRules).Included))
                matchedByNothing.Add(player.Name);
        }

        return new RuleCoverage(matchedByNothing, gated, players.Count - matchedByNothing.Count - gated.Count, players.Count);
    }

    /// <summary>Asset records carrying at least one value for <paramref name="key"/>. Files with no record carry none.</summary>
    public static int AssetsTagged(string key, IEnumerable<RuleBuilderAsset> assets) =>
        assets.Count(a => a.Values != null && HasValue(a.Values, key));

    public static int PlayersTagged(string key, IEnumerable<RuleBuilderPlayer> players) =>
        players.Count(p => HasValue(p.Values, key));

    /// <summary>
    /// Keys on content here that no rule names, ordered by key, whether or not the registry defines them. They
    /// are ignored until a rule names one, and then every asset carrying it is matched at once.
    /// </summary>
    public static IReadOnlyList<UndeclaredKey> UndeclaredKeys(IEnumerable<string> ruleKeys, IEnumerable<RuleBuilderAsset> assets)
    {
        var declared = new HashSet<string>(ruleKeys.Select(MetadataEvaluator.NormalizeKey), MetadataEvaluator.KeyComparer);
        return assets
            .Where(a => a.Values != null)
            .SelectMany(a => a.Values!.Where(kv => kv.Value.Count > 0).Select(kv => MetadataEvaluator.NormalizeKey(kv.Key)).Distinct(MetadataEvaluator.KeyComparer))
            .Where(k => k.Length > 0 && !declared.Contains(k))
            .GroupBy(k => k, MetadataEvaluator.KeyComparer)
            .Select(g => new UndeclaredKey(g.Key, g.Count()))
            .OrderBy(u => u.Key, MetadataEvaluator.KeyComparer)
            .ToList();
    }

    /// <summary>Asset records with no value for <paramref name="key"/>, which a <c>none</c> content policy excludes.</summary>
    public static int AssetsUntagged(string key, IEnumerable<RuleBuilderAsset> assets) =>
        assets.Count(a => a.Values != null && !HasValue(a.Values, key));

    private static bool HasValue(IReadOnlyDictionary<string, IReadOnlySet<string>> values, string key) =>
        values.Any(kv => MetadataEvaluator.KeyComparer.Equals(MetadataEvaluator.NormalizeKey(kv.Key), MetadataEvaluator.NormalizeKey(key))
                         && kv.Value.Any(v => !string.IsNullOrWhiteSpace(v)));
}
