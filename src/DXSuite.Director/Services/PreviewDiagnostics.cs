using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Services;

/// <summary>
/// One sentence naming a field and both sides of why it excluded content. <see cref="Field"/> renders
/// emphasised. A blank case names the rule builder control that decided it and that control's setting, so the
/// reader knows where to change it.
/// </summary>
public sealed record PreviewReason(string Field, string Detail, string? Control = null, string? Setting = null);

/// <summary>Which empty message the preview shows when nothing plays for the previewed player.</summary>
public enum PreviewEmptyState
{
    /// <summary>Something plays, so there is no empty message.</summary>
    None,

    /// <summary>A key's Nothing rule excludes the player before any content is matched.</summary>
    Gate,

    /// <summary>No content at the location reaches the player at any time. A valid configuration.</summary>
    NothingTargeted,

    /// <summary>Content reaches the player, but none of it plays at the selected time.</summary>
    NotScheduled,
}

/// <summary>
/// The pure decisions behind the content preview's targeting diagnostic (spec section 6). The wording is built
/// from the evaluator's own verdicts, so it cannot describe a decision the evaluator did not make.
/// </summary>
public static class PreviewDiagnostics
{
    // These match the rule builder's column headers and option labels, so a reason points at a control by the
    // name the reader will find on screen.
    public const string ContentWithNoValue = "Content with no value";
    public const string PlayersWithNoValue = "Players with no value";
    public const string NoPlayers = "No players";
    public const string UntaggedContent = "Untagged content";
    public const string Nothing = "Nothing";

    public const string AnyModeLine = "Matching any field is enough, and no field matched.";
    public const string AllModeLine = "Every field must match, so any one of these excludes it on its own.";

    /// <summary>
    /// The participating key whose Nothing rule excludes the player before matching, or null. The first such key
    /// by key order is named; one is enough to exclude.
    /// </summary>
    public static string? GateKey(LocationMetadataRules rules, IReadOnlyDictionary<string, IReadOnlySet<string>> player) =>
        rules.Participating
            .FirstOrDefault(r => r.UntaggedPlayers == UntaggedPlayerPolicy.None && ValuesFor(player, r.Key).Count == 0)
            ?.Key;

    /// <summary>
    /// One reason per key that returned NoMatch, in the verdicts' order. Abstaining and matching keys say nothing.
    /// Takes verdicts from <see cref="MetadataEvaluator.Evaluate"/>, which never reaches a verdict for a player
    /// its gate excluded, so a blank player on a Nothing key is the gate's to explain, not a row's.
    /// </summary>
    public static IReadOnlyList<PreviewReason> Reasons(
        LocationMetadataRules rules,
        IEnumerable<MetadataKeyVerdict> verdicts,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? asset,
        IReadOnlyDictionary<string, IReadOnlySet<string>> player,
        Func<string, string> displayName)
    {
        var reasons = new List<PreviewReason>();
        foreach (var verdict in verdicts.Where(v => v.Verdict == MetadataVerdict.NoMatch))
        {
            var rule = rules.Participating.FirstOrDefault(r => MetadataEvaluator.KeyComparer.Equals(r.Key, verdict.Key));
            if (rule == null) continue;

            var a = Ordered(asset == null ? null : ValuesFor(asset, rule.Key));
            var s = Ordered(ValuesFor(player, rule.Key));
            var field = displayName(rule.Key);

            reasons.Add((a.Count > 0, s.Count > 0) switch
            {
                (true, true) => new PreviewReason(field, $"content is {OrList(a)}, player is {OrList(s)}"),
                (false, true) => new PreviewReason(field, "content has no value, and", ContentWithNoValue, NoPlayers),
                (true, false) => new PreviewReason(field, "player has no value, and", PlayersWithNoValue,
                    rule.UntaggedPlayers == UntaggedPlayerPolicy.None ? Nothing : UntaggedContent),
                _ => new PreviewReason(field, "neither the content nor the player has a value, and", ContentWithNoValue, NoPlayers),
            });
        }
        return reasons;
    }

    /// <summary>
    /// The combine mode's explanation, when it earns one. Under ANY it leads, because no single field explains
    /// the exclusion. Under ALL it trails, and only when more than one field failed.
    /// </summary>
    public static (string? Leading, string? Trailing) CombineLines(MetadataCombineMode mode, int reasonCount) => mode switch
    {
        MetadataCombineMode.Any when reasonCount > 0 => (AnyModeLine, null),
        MetadataCombineMode.All when reasonCount > 1 => (null, AllModeLine),
        _ => (null, null),
    };

    /// <summary>
    /// Why approval holds content back from the preview, or null when it does not. Pending content passes when the
    /// preview treats pending as approved.
    /// </summary>
    public static string? ApprovalReason(int? heldApprovalStatus) => heldApprovalStatus switch
    {
        (int)ApprovalStatus.Pending => "Pending approval. The preview is showing approved content only.",
        (int)ApprovalStatus.Declined => "Declined in the approval chain. Declined content never plays.",
        _ => null,
    };

    /// <summary>
    /// Which empty message shows. Between nothing targeted and not scheduled, targeting alone decides, ignoring time,
    /// so the nothing-targeted message never names a time and a player that does receive content is told it is
    /// simply not scheduled now. A location with no content at all has nothing to target.
    /// </summary>
    public static PreviewEmptyState EmptyState(bool targetingActive, string? gateKey, int playingNow, bool hasContent, bool anyReachesPlayer)
    {
        if (playingNow > 0) return PreviewEmptyState.None;
        if (targetingActive && gateKey != null) return PreviewEmptyState.Gate;
        if (targetingActive && hasContent && !anyReachesPlayer) return PreviewEmptyState.NothingTargeted;
        return PreviewEmptyState.NotScheduled;
    }

    /// <summary>Why Preview is disabled, matching the empty message shown beneath it so the two never disagree.</summary>
    public static string? PreviewDisabledReason(PreviewEmptyState state) => state switch
    {
        PreviewEmptyState.None => null,
        PreviewEmptyState.Gate => "This player receives no content at this location, so there is nothing to preview.",
        PreviewEmptyState.NothingTargeted => "No content here reaches this player, so there is nothing to preview.",
        _ => "Nothing plays at the selected time, so there is nothing to preview.",
    };

    /// <summary>The noun that agrees with <paramref name="count"/>: <paramref name="singular"/> for exactly 1, otherwise <paramref name="plural"/>.</summary>
    public static string Noun(int count, string singular, string plural) => count == 1 ? singular : plural;

    /// <summary>"A", "A or B", "A, B or C".</summary>
    public static string OrList(IReadOnlyList<string> values) => values.Count switch
    {
        0 => "",
        1 => values[0],
        _ => string.Join(", ", values.Take(values.Count - 1)) + " or " + values[^1],
    };

    private static IReadOnlyList<string> Ordered(IReadOnlySet<string>? values) =>
        MetadataEvaluator.NormalizeValues(values).Order(StringComparer.OrdinalIgnoreCase).ToList();

    private static IReadOnlySet<string> ValuesFor(IReadOnlyDictionary<string, IReadOnlySet<string>> values, string key)
    {
        foreach (var (k, v) in values)
            if (MetadataEvaluator.KeyComparer.Equals(MetadataEvaluator.NormalizeKey(k), key))
                return MetadataEvaluator.NormalizeValues(v);
        return MetadataEvaluator.NormalizeValues(null);
    }
}
