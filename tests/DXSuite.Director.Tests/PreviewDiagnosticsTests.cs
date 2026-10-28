using DXSuite.Director.Services;
using static DXSuite.Director.Models.Types;

namespace DXSuite.Director.Tests;

/// <summary>
/// The preview's targeting diagnostic. Which keys excluded content is the evaluator's truth table; these pin how
/// the preview words each verdict, when the combine mode explains itself, and which empty message shows.
/// </summary>
public class PreviewDiagnosticsTests
{
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Values(params string[] tags) =>
        MetadataSubjectResolver.GroupValues(tags.Select(t => t.Split('=')).Select(p => (p[0], p[1])));

    private static LocationMetadataRules Rules(MetadataCombineMode mode, params MetadataKeyRule[] rules) =>
        new(mode, rules, rules.Select(r => r.Key));

    private static IReadOnlyList<PreviewReason> ReasonsFor(LocationMetadataRules rules,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? asset, IReadOnlyDictionary<string, IReadOnlySet<string>> player)
    {
        var evaluation = MetadataEvaluator.Evaluate(rules, asset, player, BlankSubjectPolicy.ApplyKeyRules);
        return PreviewDiagnostics.Reasons(rules, evaluation.Verdicts, asset, player, key => key == "Lang" ? "Language" : key);
    }

    [Fact]
    public void AMismatchNamesTheFieldAndBothSides_WithMultipleValuesAsAnOrList()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule("Concourse"));

        var reason = Assert.Single(ReasonsFor(rules,
            Values("Concourse=Concourse C", "Concourse=Concourse B", "Concourse=Concourse D"),
            Values("Concourse=Concourse A")));

        Assert.Equal(new PreviewReason("Concourse", "content is Concourse B, Concourse C or Concourse D, player is Concourse A"), reason);
    }

    [Fact]
    public void BlankContent_NamesTheContentControlAndItsSetting()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule("Lang", UntaggedContentPolicy.None));

        var reason = Assert.Single(ReasonsFor(rules, Values(), Values("Lang=English")));

        Assert.Equal(new PreviewReason("Language", "content has no value, and", "Content with no value", "No players"), reason);
    }

    [Fact]
    public void BlankPlayer_NamesThePlayerControlAndItsSetting()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule("Airline", UntaggedPlayers: UntaggedPlayerPolicy.UntaggedOnly));

        var reason = Assert.Single(ReasonsFor(rules, Values("Airline=Delta"), Values()));

        Assert.Equal(new PreviewReason("Airline", "player has no value, and", "Players with no value", "Untagged content"), reason);
    }

    [Fact]
    public void BothBlank_NamesTheContentControl()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule("Terminal", UntaggedContentPolicy.None));

        var reason = Assert.Single(ReasonsFor(rules, Values(), Values()));

        Assert.Equal("Content with no value", reason.Control);
        Assert.StartsWith("neither the content nor the player", reason.Detail);
    }

    [Fact]
    public void MatchingAndAbstainingKeys_GiveNoReason()
    {
        var rules = Rules(MetadataCombineMode.All,
            new MetadataKeyRule("Airline"),
            new MetadataKeyRule("Concourse"),
            new MetadataKeyRule("Terminal"));

        var reasons = ReasonsFor(rules,
            Values("Airline=Delta", "Concourse=Concourse B"),
            Values("Airline=Delta", "Concourse=Concourse A"));

        Assert.Equal(new[] { "Concourse" }, reasons.Select(r => r.Field));
    }

    [Fact]
    public void AGatedPlayer_CarriesNoPerRowReason_AndTheGateNamesTheKey()
    {
        var rules = Rules(MetadataCombineMode.All,
            new MetadataKeyRule("Airline"),
            new MetadataKeyRule("Terminal", UntaggedPlayers: UntaggedPlayerPolicy.None));
        var player = Values("Airline=United");

        Assert.Empty(ReasonsFor(rules, Values("Airline=Delta"), player));
        Assert.Equal("Terminal", PreviewDiagnostics.GateKey(rules, player));
        Assert.Null(PreviewDiagnostics.GateKey(rules, Values("Terminal=T1")));
    }

    [Theory]
    [InlineData(MetadataCombineMode.Any, 1, PreviewDiagnostics.AnyModeLine, null)]
    [InlineData(MetadataCombineMode.Any, 0, null, null)]
    [InlineData(MetadataCombineMode.All, 1, null, null)]
    [InlineData(MetadataCombineMode.All, 2, null, PreviewDiagnostics.AllModeLine)]
    public void TheCombineModeExplainsItselfOnlyWhenItEarnsALine(MetadataCombineMode mode, int reasons, string? leading, string? trailing)
    {
        Assert.Equal((leading, trailing), PreviewDiagnostics.CombineLines(mode, reasons));
    }

    [Theory]
    [InlineData((int)ApprovalStatus.Pending, "Pending approval")]
    [InlineData((int)ApprovalStatus.Declined, "Declined")]
    public void ApprovalReasons_NameTheState(int status, string prefix)
    {
        Assert.StartsWith(prefix, PreviewDiagnostics.ApprovalReason(status));
    }

    [Fact]
    public void ApprovalReason_IsNullWhenApprovalDoesNotHoldTheContent()
    {
        Assert.Null(PreviewDiagnostics.ApprovalReason(null));
    }

    [Theory]
    [InlineData(true, "Terminal", 3, true, true, PreviewEmptyState.None)]
    [InlineData(true, "Terminal", 0, true, true, PreviewEmptyState.Gate)]
    [InlineData(true, null, 0, true, false, PreviewEmptyState.NothingTargeted)]
    [InlineData(true, null, 0, true, true, PreviewEmptyState.NotScheduled)]
    [InlineData(true, null, 0, false, false, PreviewEmptyState.NotScheduled)]
    [InlineData(false, null, 0, true, false, PreviewEmptyState.NotScheduled)]
    public void TheEmptyState_IsDecidedByTargetingIgnoringTime(
        bool targetingActive, string? gateKey, int playingNow, bool hasContent, bool anyReachesPlayer, PreviewEmptyState expected)
    {
        Assert.Equal(expected, PreviewDiagnostics.EmptyState(targetingActive, gateKey, playingNow, hasContent, anyReachesPlayer));
    }

    [Theory]
    [InlineData(PreviewEmptyState.None, null)]
    [InlineData(PreviewEmptyState.Gate, "This player receives no content at this location, so there is nothing to preview.")]
    [InlineData(PreviewEmptyState.NothingTargeted, "No content here reaches this player, so there is nothing to preview.")]
    [InlineData(PreviewEmptyState.NotScheduled, "Nothing plays at the selected time, so there is nothing to preview.")]
    public void PreviewDisabledReason_NamesTheSameCauseAsTheEmptyMessage(PreviewEmptyState state, string? expected)
    {
        Assert.Equal(expected, PreviewDiagnostics.PreviewDisabledReason(state));
    }

    [Theory]
    [InlineData(0, "items")]
    [InlineData(1, "item")]
    [InlineData(2, "items")]
    public void Noun_AgreesWithTheCount(int count, string expected)
    {
        Assert.Equal(expected, PreviewDiagnostics.Noun(count, "item", "items"));
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A or B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B or C")]
    public void OrList_JoinsTheLastPairWithOr(string[] values, string expected)
    {
        Assert.Equal(expected, PreviewDiagnostics.OrList(values));
    }
}
