using DXSuite.Director.Services;

namespace DXSuite.Director.Tests;

/// <summary>
/// Which of an asset's tags the per-asset editor lists as orphaned. Every other tag is a field, so a key in
/// neither set would be invisible and unclearable anywhere in the product.
/// </summary>
public class AssetMetadataEditTests
{
    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Values(params string[] tags) =>
        MetadataSubjectResolver.GroupValues(tags.Select(t => t.Split('=')).Select(p => (p[0], p[1])));

    [Fact]
    public void OrphanedTags_ListsEveryHeldKeyThatIsNotParticipating()
    {
        var orphans = AssetMetadataEdit.OrphanedTags(
            Values("Terminal=T1", "HotelNo=12", "Carrier=United", "Carrier=Delta"),
            ["Terminal"]);

        Assert.Equal(new[] { "Carrier", "HotelNo" }, orphans.Select(o => o.Key));
        Assert.Equal(new[] { "Delta", "United" }, orphans[0].Values);
    }

    [Fact]
    public void OrphanedTags_MatchesParticipatingKeysIgnoringCaseAndWhitespace()
    {
        var orphans = AssetMetadataEdit.OrphanedTags(Values("terminal=T1"), [" Terminal "]);

        Assert.Empty(orphans);
    }

    [Fact]
    public void OrphanedTags_WithNoParticipatingKeysListsEveryTag()
    {
        var orphans = AssetMetadataEdit.OrphanedTags(Values("Category=Category 2"), []);

        Assert.Equal("Category", Assert.Single(orphans).Key);
    }

    [Fact]
    public void OrphanedTags_IgnoresKeysWhoseValuesAreAllBlank()
    {
        var orphans = AssetMetadataEdit.OrphanedTags(Values("Carrier= "), []);

        Assert.Empty(orphans);
    }
}
