using System.Text.Json;
using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

/// <summary>
/// The evaluator asserted once, per docs/spec/metadata-content-targeting.md section 15.3. Call sites get
/// their own tests proving they AND approval and pass the right subject; they never re-run this table.
/// </summary>
public class MetadataEvaluatorTests
{
    private const string Concourse = "Concourse";
    private const string Airline = "Airline";
    private const string Language = "Language";

    private sealed record VerdictVector(
        int Row,
        string Name,
        string[] Asset,
        string[] Subject,
        string UntaggedContent,
        string UntaggedPlayers,
        string BlankSubjectPolicy,
        string Expected);

    private static VerdictVector[] LoadVectors()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "MetadataEvaluatorVectors.json"));
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("verdict").Deserialize<VerdictVector[]>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    // Row and name lead so a red case names its spec row.
    public static TheoryData<int, string, string[], string[], string, string, string, string> TruthTable()
    {
        var data = new TheoryData<int, string, string[], string[], string, string, string, string>();
        foreach (var v in LoadVectors())
            data.Add(v.Row, v.Name, v.Asset, v.Subject, v.UntaggedContent, v.UntaggedPlayers, v.BlankSubjectPolicy, v.Expected);
        return data;
    }

    // ---- Stage 1: the section 3.4 table, as data ----

    [Theory]
    [MemberData(nameof(TruthTable))]
    public void Verdict_matches_the_truth_table(
        int row, string name, string[] asset, string[] subject,
        string untaggedContent, string untaggedPlayers, string blankSubjectPolicy, string expected)
    {
        var rule = new MetadataKeyRule(
            Concourse,
            Enum.Parse<UntaggedContentPolicy>(untaggedContent, ignoreCase: true),
            Enum.Parse<UntaggedPlayerPolicy>(untaggedPlayers, ignoreCase: true));

        var verdict = MetadataEvaluator.Verdict(
            rule,
            new HashSet<string>(asset, StringComparer.Ordinal),
            new HashSet<string>(subject, StringComparer.Ordinal),
            Enum.Parse<BlankSubjectPolicy>(blankSubjectPolicy, ignoreCase: true));

        Assert.True(Enum.Parse<MetadataVerdict>(expected, ignoreCase: true) == verdict,
            $"row {row}, {name}: expected {expected}, got {verdict}");
    }

    [Fact]
    public void Truth_table_covers_every_row_and_every_named_case()
    {
        var vectors = LoadVectors();

        Assert.Equal(Enumerable.Range(1, 12), vectors.Select(v => v.Row).Distinct().Order());
        Assert.Equal(vectors.Length, vectors.Select(v => v.Name).Distinct().Count());

        (string Name, int Row)[] named =
        [
            ("Key_BothTagged_Intersecting_Matches", 1),
            ("Key_BothTagged_Disjoint_NoMatch", 1),
            ("Key_AssetMultiValue_DeviceSingleValue_IntersectionMatches", 1),
            ("Key_UserMultiValue_AssetMultiValue_AnyOfAnyMatches", 1),
            ("Key_CaseVariantValues_Match", 1),
            ("Key_WhitespacePaddedValues_Match", 1),
            ("Key_AssetHoldsCaseVariantDuplicates_CollapseToOneValue", 1),
            ("Key_ValueContainingComma_NotSplit", 1),
            ("Key_UntaggedAsset_TaggedDevice_AllPlayers_Abstains", 2),
            ("Key_UntaggedAsset_TaggedDevice_NoPlayers_NoMatch", 3),
            ("Key_TaggedAsset_BlankDevice_AllContent_Abstains", 4),
            ("Key_TaggedAsset_BlankDevice_UntaggedOnly_NoMatch", 5),
            ("Key_TaggedAsset_BlankUser_AlwaysAbstains", 6),
            ("Key_BothUntagged_AllPlayers_AllContent_Abstains", 7),
            ("Key_BothUntagged_AllPlayers_UntaggedOnly_Matches", 8),
            ("Key_BothUntagged_NoPlayers_AllContent_NoMatch", 9),
            ("Key_BothUntagged_NoPlayers_UntaggedOnly_NoMatch", 10),
            ("Key_BothUntagged_AllPlayers_Scope_Abstains", 11),
            ("Key_BothUntagged_NoPlayers_Scope_NoMatch", 12),
        ];

        Assert.All(named, n => Assert.Contains(vectors, v => v.Name == n.Name && v.Row == n.Row));
    }

    [Fact]
    public void Key_AssetHoldsCaseVariantDuplicates_CountsOneValue()
    {
        var values = MetadataEvaluator.NormalizeValues(["Green", "green", " GREEN "]);

        Assert.Single(values);
    }

    // ---- Stage 0: the subject gate ----

    [Fact]
    public void Gate_DeviceBlankOnNothingKey_ReceivesNoAssets()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Language, UntaggedPlayers: UntaggedPlayerPolicy.None));

        var result = MetadataEvaluator.Evaluate(rules, Values(), Values(), BlankSubjectPolicy.ApplyKeyRules);

        Assert.False(result.Included);
        Assert.True(result.Gated);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void Gate_DeviceBlankOnNothingKey_AnotherKeyMatching_StillReceivesNoAssets()
    {
        var rules = Rules(
            MetadataCombineMode.Any,
            new MetadataKeyRule(Concourse),
            new MetadataKeyRule(Language, UntaggedPlayers: UntaggedPlayerPolicy.None));

        var result = MetadataEvaluator.Evaluate(
            rules,
            Values((Concourse, ["Concourse B"])),
            Values((Concourse, ["Concourse B"])),
            BlankSubjectPolicy.ApplyKeyRules);

        Assert.False(result.Included);
        Assert.True(result.Gated);
    }

    [Fact]
    public void Gate_DeviceHasValueOnNothingKey_NotGated()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Language, UntaggedPlayers: UntaggedPlayerPolicy.None));

        var result = MetadataEvaluator.Evaluate(rules, Values(), Values((Language, ["English"])), BlankSubjectPolicy.ApplyKeyRules);

        Assert.True(result.Included);
        Assert.False(result.Gated);
    }

    [Fact]
    public void Gate_DeviceWhitespaceOnlyOnNothingKey_ReceivesNoAssets()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Language, UntaggedPlayers: UntaggedPlayerPolicy.None));

        Assert.True(MetadataEvaluator.IsGated(rules, Values((Language, ["  "])), BlankSubjectPolicy.ApplyKeyRules));
    }

    [Fact]
    public void Gate_ScopedUserWithNoValuesOnAnyParticipatingKey_SeesNothing()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Concourse), new MetadataKeyRule(Airline));

        // An untagged asset with every key abstaining would otherwise be visible: Scoped becoming Full by omission.
        var result = MetadataEvaluator.Evaluate(
            rules,
            Values(),
            Values(("Unrelated", ["x"])),
            BlankSubjectPolicy.AlwaysAbstain);

        Assert.False(result.Included);
        Assert.True(result.Gated);
    }

    [Fact]
    public void Gate_ScopedUserWithValuesOnOneKeyOnly_NotGated()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Concourse), new MetadataKeyRule(Airline));

        var result = MetadataEvaluator.Evaluate(
            rules,
            Values((Concourse, ["Concourse B"]), (Airline, ["Delta"])),
            Values((Concourse, ["Concourse B"])),
            BlankSubjectPolicy.AlwaysAbstain);

        Assert.True(result.Included);
        Assert.False(result.Gated);
        Assert.Equal(
            new[] { new MetadataKeyVerdict(Airline, MetadataVerdict.Abstain), new MetadataKeyVerdict(Concourse, MetadataVerdict.Match) },
            result.Verdicts);
    }

    [Fact]
    public void Gate_ScopeGateIgnoresNothingPlayersPolicy()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Concourse), new MetadataKeyRule(Language, UntaggedPlayers: UntaggedPlayerPolicy.None));

        Assert.False(MetadataEvaluator.IsGated(rules, Values((Concourse, ["Concourse B"])), BlankSubjectPolicy.AlwaysAbstain));
    }

    [Theory]
    [InlineData("full", false)]
    [InlineData("partial", true)]
    [InlineData("view", true)]
    [InlineData("Partial", true)]
    [InlineData(null, false)]
    public void Gate_FullAccessRule_ScopeNotEvaluated(string? accessLevel, bool evaluated)
    {
        Assert.Equal(evaluated, MetadataEvaluator.IsScopeEvaluated(accessLevel));
    }

    // ---- Stage 2: combination ----

    [Fact]
    public void Combine_All_OneNoMatch_Excludes()
    {
        Assert.False(MetadataEvaluator.Combine(MetadataCombineMode.All, [MetadataVerdict.Abstain, MetadataVerdict.NoMatch]));
    }

    [Fact]
    public void Combine_All_MatchAndAbstain_Includes()
    {
        Assert.True(MetadataEvaluator.Combine(MetadataCombineMode.All, [MetadataVerdict.Match, MetadataVerdict.Abstain]));
    }

    [Fact]
    public void Combine_All_MatchAndNoMatch_Excludes()
    {
        Assert.False(MetadataEvaluator.Combine(MetadataCombineMode.All, [MetadataVerdict.Match, MetadataVerdict.NoMatch]));
    }

    [Fact]
    public void Combine_Any_MatchAndNoMatch_Includes()
    {
        Assert.True(MetadataEvaluator.Combine(MetadataCombineMode.Any, [MetadataVerdict.Match, MetadataVerdict.NoMatch]));
    }

    [Fact]
    public void Combine_Any_AllAbstain_Includes()
    {
        Assert.True(MetadataEvaluator.Combine(MetadataCombineMode.Any, [MetadataVerdict.Abstain, MetadataVerdict.Abstain]));
    }

    [Fact]
    public void Combine_Any_NoMatchAndAbstainOnly_Excludes()
    {
        Assert.False(MetadataEvaluator.Combine(MetadataCombineMode.Any, [MetadataVerdict.NoMatch, MetadataVerdict.Abstain]));
    }

    [Theory]
    [InlineData(MetadataCombineMode.All)]
    [InlineData(MetadataCombineMode.Any)]
    public void Combine_NoParticipatingKeys_Includes(MetadataCombineMode mode)
    {
        var rules = Rules(mode);

        var result = MetadataEvaluator.Evaluate(
            rules,
            Values((Concourse, ["Concourse B"])),
            Values((Concourse, ["Concourse A"])),
            BlankSubjectPolicy.ApplyKeyRules);

        Assert.True(result.Included);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void Combine_VerdictsOrderedByKey()
    {
        var rules = Rules(
            MetadataCombineMode.All,
            new MetadataKeyRule(Language),
            new MetadataKeyRule("airline"),
            new MetadataKeyRule(Concourse));

        var result = MetadataEvaluator.Evaluate(rules, Values(), Values(), BlankSubjectPolicy.ApplyKeyRules);

        Assert.Equal(new[] { "airline", Concourse, Language }, result.Verdicts.Select(v => v.Key));
    }

    // The section 3.5 example: the toggle's entire purpose, end to end through Evaluate.
    [Theory]
    [InlineData(MetadataCombineMode.Any, true)]
    [InlineData(MetadataCombineMode.All, false)]
    public void Combine_ConcourseMismatchAirlineMatch_DependsOnMode(MetadataCombineMode mode, bool included)
    {
        var rules = Rules(mode, new MetadataKeyRule(Concourse), new MetadataKeyRule(Airline));

        var result = MetadataEvaluator.Evaluate(
            rules,
            Values((Concourse, ["Concourse B"]), (Airline, ["Delta"])),
            Values((Concourse, ["Concourse C"]), (Airline, ["Delta"])),
            BlankSubjectPolicy.ApplyKeyRules);

        Assert.Equal(included, result.Included);
    }

    // ---- Subject resolution and participation ----

    [Fact]
    public void Subject_UserValuesUnionedWithGroupValues()
    {
        var subject = MetadataEvaluator.UserSubject(
            Values((Concourse, ["Concourse A"])),
            [Values((Concourse, ["concourse a ", "Concourse B"])), Values((Airline, ["Delta"]))]);

        Assert.Equal(new[] { "Concourse A", "Concourse B" }, subject[Concourse].Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new[] { "Delta" }, subject[Airline]);
    }

    [Fact]
    public void Subject_GroupMembershipReadCurrent_NotSnapshotted()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Concourse));
        var asset = Values((Concourse, ["Concourse B"]));
        var concourseAOps = Values((Concourse, ["Concourse A"]));
        var concourseBOps = Values((Concourse, ["Concourse B"]));

        var before = MetadataEvaluator.Evaluate(
            rules, asset, MetadataEvaluator.UserSubject(null, [concourseAOps]), BlankSubjectPolicy.AlwaysAbstain);
        var afterMove = MetadataEvaluator.Evaluate(
            rules, asset, MetadataEvaluator.UserSubject(null, [concourseBOps]), BlankSubjectPolicy.AlwaysAbstain);
        var afterRemoval = MetadataEvaluator.Evaluate(
            rules, asset, MetadataEvaluator.UserSubject(null, []), BlankSubjectPolicy.AlwaysAbstain);

        Assert.False(before.Included);
        Assert.True(afterMove.Included);
        Assert.True(afterRemoval.Gated);
    }

    [Fact]
    public void Participation_KeyOnAssetButNotInLocationRules_Ignored()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Concourse));

        var result = MetadataEvaluator.Evaluate(
            rules,
            Values((Concourse, ["Concourse B"]), (Airline, ["Delta"])),
            Values((Concourse, ["Concourse B"]), (Airline, ["United"])),
            BlankSubjectPolicy.ApplyKeyRules);

        Assert.True(result.Included);
        Assert.Equal(new[] { new MetadataKeyVerdict(Concourse, MetadataVerdict.Match) }, result.Verdicts);
    }

    [Fact]
    public void Participation_KeyInRulesButNotInRegistry_Abstains()
    {
        var rules = new LocationMetadataRules(
            MetadataCombineMode.All,
            [new MetadataKeyRule(Concourse, UntaggedContentPolicy.None, UntaggedPlayerPolicy.None)],
            registryKeys: []);

        var result = MetadataEvaluator.Evaluate(
            rules,
            Values(),
            Values(),
            BlankSubjectPolicy.ApplyKeyRules);

        Assert.True(result.Included);
        Assert.False(result.Gated);
        Assert.Equal(new[] { new MetadataKeyVerdict(Concourse, MetadataVerdict.Abstain) }, result.Verdicts);
    }

    [Theory]
    [InlineData("__all__")]
    [InlineData("__none__")]
    public void Participation_SentinelAllOrNone_NotTreatedAsKey(string sentinel)
    {
        var rules = new LocationMetadataRules(
            MetadataCombineMode.All,
            [new MetadataKeyRule(sentinel, UntaggedContentPolicy.None, UntaggedPlayerPolicy.None)],
            registryKeys: [sentinel]);

        var result = MetadataEvaluator.Evaluate(rules, Values(), Values(), BlankSubjectPolicy.ApplyKeyRules);

        Assert.Empty(rules.Participating);
        Assert.Empty(rules.OrphanedKeys);
        Assert.True(result.Included);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void Participation_KeysMatchCaseInsensitivelyAndTrimmed()
    {
        var rules = new LocationMetadataRules(
            MetadataCombineMode.All,
            [new MetadataKeyRule(" concourse ")],
            registryKeys: [Concourse]);

        var result = MetadataEvaluator.Evaluate(
            rules,
            Values(("CONCOURSE", ["Concourse B"])),
            Values((Concourse, ["Concourse A"])),
            BlankSubjectPolicy.ApplyKeyRules);

        Assert.False(result.Included);
        Assert.Equal(MetadataVerdict.NoMatch, Assert.Single(result.Verdicts).Verdict);
    }

    // ---- Visible holes ----

    [Fact]
    public void Hole_FileWithNoAssetRow_Included()
    {
        var rules = Rules(MetadataCombineMode.All, new MetadataKeyRule(Concourse, UntaggedContentPolicy.None));

        var result = MetadataEvaluator.Evaluate(rules, assetValues: null, Values((Concourse, ["Concourse A"])), BlankSubjectPolicy.ApplyKeyRules);

        Assert.True(result.Included);
        Assert.Empty(result.Verdicts);
    }

    [Fact]
    public void Hole_OrphanedRegistryKey_AbstainsAndWarns()
    {
        var rules = new LocationMetadataRules(
            MetadataCombineMode.All,
            [new MetadataKeyRule(Concourse), new MetadataKeyRule("Retired", UntaggedContentPolicy.None, UntaggedPlayerPolicy.None)],
            registryKeys: [Concourse]);

        var result = MetadataEvaluator.Evaluate(
            rules,
            Values((Concourse, ["Concourse B"]), ("Retired", ["x"])),
            Values((Concourse, ["Concourse B"]), ("Retired", ["y"])),
            BlankSubjectPolicy.ApplyKeyRules);

        Assert.Equal(new[] { "Retired" }, rules.OrphanedKeys);
        Assert.True(result.Included);
        Assert.Equal(
            new[] { new MetadataKeyVerdict(Concourse, MetadataVerdict.Match), new MetadataKeyVerdict("Retired", MetadataVerdict.Abstain) },
            result.Verdicts);
    }

    // ---- Reach: one asset, many players ----

    [Fact]
    public void Reach_CountsReceivingGatedAndTotalSeparately()
    {
        var rules = Rules(
            MetadataCombineMode.All,
            new MetadataKeyRule(Concourse),
            new MetadataKeyRule(Language, UntaggedPlayers: UntaggedPlayerPolicy.None));

        var reach = MetadataEvaluator.Reach(
            rules,
            Values((Concourse, ["Concourse B"])),
            [
                Values((Concourse, ["Concourse B"]), (Language, ["English"])),
                Values((Concourse, ["Concourse A"]), (Language, ["English"])),
                Values((Concourse, ["Concourse B"])),
                Values((Language, ["French"])),
            ]);

        Assert.Equal(new MetadataReach(Receiving: 2, Gated: 1, Total: 4), reach);
    }

    [Fact]
    public void Reach_NoPlayers_IsZeroOfZero()
    {
        var reach = MetadataEvaluator.Reach(Rules(MetadataCombineMode.All, new MetadataKeyRule(Concourse)), Values(), []);

        Assert.Equal(new MetadataReach(0, 0, 0), reach);
    }

    private static LocationMetadataRules Rules(MetadataCombineMode mode, params MetadataKeyRule[] rules) =>
        new(mode, rules, rules.Select(r => r.Key));

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Values(params (string Key, string[] Values)[] entries) =>
        entries.ToDictionary(e => e.Key, e => (IReadOnlySet<string>)new HashSet<string>(e.Values, StringComparer.Ordinal), StringComparer.Ordinal);
}
