using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

/// <summary>
/// The rule builder's counters. Which assets reach which player is the evaluator's truth table; these tests
/// pin how the builder turns that into its three coverage counts and its per-field counts.
/// </summary>
public class ContentMetadataRuleBuilderTests
{
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Values(params string[] tags) =>
        MetadataSubjectResolver.GroupValues(tags.Select(t => t.Split('=')).Select(p => (p[0], p[1])));

    private static RuleBuilderAsset Asset(params string[] tags) => new(Values(tags));
    private static RuleBuilderPlayer Player(string name, params string[] tags) => new(name, Values(tags));

    private static LocationMetadataRules Rules(params MetadataKeyRule[] rules) =>
        new(MetadataCombineMode.All, rules, rules.Select(r => r.Key));

    [Fact]
    public void Coverage_CountsGatedApartFromMatchedByNothing()
    {
        var rules = Rules(
            new MetadataKeyRule("Carrier", UntaggedPlayers: UntaggedPlayerPolicy.None),
            new MetadataKeyRule("Terminal", UntaggedContentPolicy.None));
        var assets = new[] { Asset("Carrier=Delta", "Terminal=T1") };
        var players = new[]
        {
            Player("receives", "Carrier=Delta", "Terminal=T1"),
            Player("starved", "Carrier=United", "Terminal=T1"),
            Player("gated", "Terminal=T1")
        };

        var coverage = ContentMetadataRuleBuilder.Coverage(rules, assets, players);

        Assert.Equal(new[] { "starved" }, coverage.MatchedByNoContent);
        Assert.Equal(new[] { "gated" }, coverage.ExcludedBeforeMatching);
        Assert.Equal(1, coverage.Receiving);
        Assert.Equal(3, coverage.Total);
        Assert.Equal(2, coverage.ReceivingNothing);
    }

    [Fact]
    public void Coverage_FileWithNoAssetRecordReachesEveryPlayer_AsDeliveryDoes()
    {
        var rules = Rules(new MetadataKeyRule("Carrier", UntaggedContentPolicy.None, UntaggedPlayerPolicy.None));
        var assets = new[] { new RuleBuilderAsset(null) };
        var players = new[] { Player("tagged", "Carrier=Delta"), Player("gated") };
        var targeting = new LocationTargeting(true, rules);

        var coverage = ContentMetadataRuleBuilder.Coverage(rules, assets, players);

        Assert.Equal(2, coverage.Receiving);
        Assert.All(players, p => Assert.True(PlaylistManifestRules.IsTargetedAt(targeting, null, p.Values)));
    }

    [Fact]
    public void Coverage_NoPlayers_AllZero()
    {
        var coverage = ContentMetadataRuleBuilder.Coverage(Rules(new MetadataKeyRule("Carrier")), [Asset()], []);

        Assert.Empty(coverage.MatchedByNoContent);
        Assert.Empty(coverage.ExcludedBeforeMatching);
        Assert.Equal(0, coverage.Receiving);
        Assert.Equal(0, coverage.Total);
    }

    [Fact]
    public void FieldCounts_TaggedIgnoresBlankValuesAndRecordlessFiles()
    {
        var assets = new[] { Asset("Carrier=Delta"), Asset("Carrier= "), Asset(), new RuleBuilderAsset(null) };
        var players = new[] { Player("a", "carrier=Delta"), Player("b") };

        Assert.Equal(1, ContentMetadataRuleBuilder.AssetsTagged("Carrier", assets));
        Assert.Equal(2, ContentMetadataRuleBuilder.AssetsUntagged("Carrier", assets));
        Assert.Equal(1, ContentMetadataRuleBuilder.PlayersTagged("Carrier", players));
    }

    [Fact]
    public void UndeclaredKeys_CountsAssetsPerKeyNoRuleNames()
    {
        var assets = new[] { Asset("Gate=C12", "Carrier=Delta"), Asset("gate=A4"), Asset("Colors=Green", "Colors=Blue") };

        var undeclared = ContentMetadataRuleBuilder.UndeclaredKeys(["Carrier"], assets);

        Assert.Equal(new[] { new UndeclaredKey("Colors", 1), new UndeclaredKey("Gate", 2) }, undeclared);
    }
}
