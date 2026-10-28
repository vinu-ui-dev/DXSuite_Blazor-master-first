using DXSuite.Director.Services;
using DXSuite.Director.Services.ContentPackage;

namespace DXSuite.Director.Tests;

/// <summary>
/// The importer's per-record decisions: ownership halves resolve independently and never fall back to the
/// system user, and every value the target does not recognise degrades to the pre-targeting default, loudly.
/// </summary>
public class ContentPackageImportRulesTests
{
    [Fact]
    public void Ownership_AResolvedGroupSurvivesAnUnresolvedUser()
    {
        var ownership = ContentPackageImportRules.ResolveOwnership("gone@example.com", null, "Delta", 12);

        Assert.Null(ownership.UserId);
        Assert.Equal(12, ownership.GroupId);
        Assert.True(ownership.UserUnresolved);
        Assert.False(ownership.GroupUnresolved);
    }

    [Fact]
    public void Ownership_AResolvedUserSurvivesAnUnresolvedGroup()
    {
        var ownership = ContentPackageImportRules.ResolveOwnership("pat@example.com", 7, "Gone", null);

        Assert.Equal(7, ownership.UserId);
        Assert.Null(ownership.GroupId);
        Assert.False(ownership.UserUnresolved);
        Assert.True(ownership.GroupUnresolved);
    }

    [Fact]
    public void Ownership_WithNoNamesIsUnownedAndNothingIsUnresolved()
    {
        var ownership = ContentPackageImportRules.ResolveOwnership(null, 99, " ", 98);

        Assert.Equal(new AssetOwnershipResolution(null, null, false, false), ownership);
    }

    [Theory]
    [InlineData("view", "all", "all", 0)]
    [InlineData("view", "none", "none", 0)]
    [InlineData("view", null, "none", 1)]
    [InlineData("view", "some", "none", 1)]
    [InlineData("partial", null, null, 0)]
    [InlineData("full", "all", null, 1)]
    public void ViewScope_OnlyViewRulesCarryOne_AndAnUnknownOneIsTheNarrowerReading(
        string accessLevel, string? viewScope, string? expected, int warningCount)
    {
        var warnings = new List<string>();

        var result = ContentPackageImportRules.NormalizeViewScope(
            new LocationAccessRuleRecord { AccessLevel = accessLevel, ViewScope = viewScope, UserGroupName = "Delta" }, warnings);

        Assert.Equal(expected, result);
        Assert.Equal(warningCount, warnings.Count);
    }

    [Theory]
    [InlineData("all", "all", 0)]
    [InlineData("any", "any", 0)]
    [InlineData("ANY", "all", 1)]
    [InlineData(null, "all", 1)]
    public void MatchMode_UnknownReadsAsAll(string? matchMode, string expected, int warningCount)
    {
        var warnings = new List<string>();

        Assert.Equal(expected, ContentPackageImportRules.NormalizeMatchMode("Gate B", matchMode, warnings));
        Assert.Equal(warningCount, warnings.Count);
    }

    [Fact]
    public void Policies_UnknownValuesReadAsAll_EachWithAWarning()
    {
        var warnings = new List<string>();

        var (content, player) = ContentPackageImportRules.NormalizePolicies("Gate B", "Carrier", "sometimes", "untaggedOnly", warnings);
        var (content2, player2) = ContentPackageImportRules.NormalizePolicies("Gate B", "Carrier", "none", "never", warnings);

        Assert.Equal(("all", "untaggedOnly"), (content, player));
        Assert.Equal(("none", "all"), (content2, player2));
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void KeysNoPlayerHolds_IgnoresCaseAndBlankValues()
    {
        var players = new[]
        {
            MetadataSubjectResolver.GroupValues([("carrier", "Delta"), ("Terminal", " ")]),
            MetadataSubjectResolver.GroupValues([("Gate", "B4")])
        };

        var missing = ContentPackageImportRules.KeysNoPlayerHolds(["Carrier", "Terminal", "Gate", "Zone"], players);

        Assert.Equal(new[] { "Terminal", "Zone" }, missing);
    }

    [Fact]
    public void KeysNoPlayerHolds_WithNoPlayersListsEveryKey()
    {
        Assert.Equal(new[] { "Carrier" }, ContentPackageImportRules.KeysNoPlayerHolds(["Carrier"], []));
    }
}
