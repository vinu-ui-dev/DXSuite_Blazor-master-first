using System.Text.Json;
using System.Text.Json.Serialization;
using DXSuite.Director.Services;
using DXSuite.Director.Services.ContentPackage;

namespace DXSuite.Director.Tests;

/// <summary>
/// Every bundle in existence predates SchemaVersion 4, so reading one must apply the metadata targeting
/// migrations to it: participating keys leave ExtendedData for rule records, a missing match mode reads as
/// all, and the access rule sentinel becomes a ViewScope.
/// </summary>
public class ContentPackageManifestUpgradeTests
{
    private static readonly Guid LocationId = Guid.NewGuid();

    private static ContentPackageManifest Manifest(int schemaVersion, string? extendedData = null, string? matchMode = null) => new()
    {
        SchemaVersion = schemaVersion,
        Locations = { new LocationRecord { ContentPackageId = LocationId, LocationName = "Gate B", ExtendedData = extendedData, MetadataMatchMode = matchMode } }
    };

    private static LocationAccessRuleRecord Rule(string accessLevel, string? legacy) =>
        new() { UserGroupName = "Delta", AccessLevel = accessLevel, LegacyMetadataFilterKey = legacy };

    [Theory]
    [InlineData("__all__", "all")]
    [InlineData("__none__", "none")]
    public void PreV4ViewSentinels_BecomeViewScope(string sentinel, string viewScope)
    {
        var manifest = Manifest(3);
        manifest.LocationAccessRules.Add(Rule("view", sentinel));

        var warnings = ContentPackageManifestUpgrade.Apply(manifest);

        Assert.Equal(viewScope, manifest.LocationAccessRules[0].ViewScope);
        Assert.Null(manifest.LocationAccessRules[0].LegacyMetadataFilterKey);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("partial", "Concourse")]
    [InlineData("partial", "__all__")]
    public void PreV4MetadataKeys_AreDroppedWithAWarningNamingTheRule(string accessLevel, string legacy)
    {
        var manifest = Manifest(3);
        manifest.LocationAccessRules.Add(Rule(accessLevel, legacy));

        var warnings = ContentPackageManifestUpgrade.Apply(manifest);

        Assert.Null(manifest.LocationAccessRules[0].ViewScope);
        Assert.Contains("Delta", Assert.Single(warnings));
    }

    [Fact]
    public void APreV4ViewRuleWithAnUnknownValue_ImportsAsNone_WithOneWarningNamingTheValue()
    {
        var manifest = Manifest(3);
        manifest.LocationAccessRules.Add(Rule("view", "Concourse"));

        var warnings = ContentPackageManifestUpgrade.Apply(manifest);
        var normalizeWarnings = new List<string>();
        var viewScope = ContentPackageImportRules.NormalizeViewScope(manifest.LocationAccessRules[0], normalizeWarnings);

        Assert.Equal(ContentScope.ViewScopeNone, viewScope);
        Assert.Contains("Concourse", Assert.Single(warnings));
        Assert.Empty(normalizeWarnings);
    }

    [Fact]
    public void V4Rules_KeepTheirViewScopeAndIgnoreTheLegacyField()
    {
        var manifest = Manifest(4);
        manifest.LocationAccessRules.Add(new LocationAccessRuleRecord { AccessLevel = "view", ViewScope = "all", LegacyMetadataFilterKey = "__none__" });

        var warnings = ContentPackageManifestUpgrade.Apply(manifest);

        Assert.Equal("all", manifest.LocationAccessRules[0].ViewScope);
        Assert.Empty(warnings);
    }

    [Fact]
    public void PreV4CustomMetadataFields_BecomeRulesWithDefaultPolicies_AndLeaveTheBlob()
    {
        var manifest = Manifest(3, """{"useVideoMask":true,"customMetadataFields":[{"name":" Carrier "},{"name":"carrier"},{"Name":"Terminal"},{"name":""}]}""");

        ContentPackageManifestUpgrade.Apply(manifest);

        Assert.Equal(new[] { "Carrier", "Terminal" }, manifest.LocationMetadataRules.Select(r => r.MetadataKey));
        Assert.All(manifest.LocationMetadataRules, r =>
        {
            Assert.Equal(LocationId, r.LocationContentPackageId);
            Assert.Equal("all", r.UntaggedContentPolicy);
            Assert.Equal("all", r.UntaggedPlayerPolicy);
        });
        Assert.Equal("""{"useVideoMask":true}""", manifest.Locations[0].ExtendedData);
    }

    [Fact]
    public void PreV4CustomMetadataFields_StripBothSpellings()
    {
        var manifest = Manifest(3, """{"CustomMetadataFields":[{"Name":"Gate"}],"customMetadataFields":[{"name":"Zone"}]}""");

        ContentPackageManifestUpgrade.Apply(manifest);

        Assert.Equal(new[] { "Gate", "Zone" }, manifest.LocationMetadataRules.Select(r => r.MetadataKey).Order());
        Assert.Equal("{}", manifest.Locations[0].ExtendedData);
    }

    [Fact]
    public void V4CustomMetadataFields_AreStrippedButNotTranslated()
    {
        var manifest = Manifest(4, """{"customMetadataFields":[{"name":"Carrier"}]}""");

        ContentPackageManifestUpgrade.Apply(manifest);

        Assert.Empty(manifest.LocationMetadataRules);
        Assert.Equal("{}", manifest.Locations[0].ExtendedData);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("""{ "wallLayout" : { "displays" : [ "dx-display:abc" ] } }""")]
    public void ExtendedDataWithoutTheProperty_IsLeftByteForByte(string? extendedData)
    {
        var manifest = Manifest(3, extendedData);

        ContentPackageManifestUpgrade.Apply(manifest);

        Assert.Equal(extendedData, manifest.Locations[0].ExtendedData);
        Assert.Empty(manifest.LocationMetadataRules);
    }

    [Theory]
    [InlineData(3, null, "all")]
    [InlineData(4, "", "all")]
    [InlineData(4, "any", "any")]
    public void MissingMatchMode_ReadsAsAll(int schemaVersion, string? matchMode, string expected)
    {
        var manifest = Manifest(schemaVersion, matchMode: matchMode);

        ContentPackageManifestUpgrade.Apply(manifest);

        Assert.Equal(expected, manifest.Locations[0].MetadataMatchMode);
    }

    [Fact]
    public void ThePreV4FieldName_DeserializesIntoTheLegacyProperty_AndIsNeverWritten()
    {
        var read = JsonSerializer.Deserialize<LocationAccessRuleRecord>(
            """{"accessLevel":"view","metadataFilterKey":"__all__"}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var written = JsonSerializer.Serialize(
            new LocationAccessRuleRecord { AccessLevel = "view", ViewScope = "all" },
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

        Assert.Equal("__all__", read.LegacyMetadataFilterKey);
        Assert.DoesNotContain("MetadataFilterKey", written);
    }
}
