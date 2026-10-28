using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Tests;

/// <summary>
/// The registry's delete and option-removal warnings name every store using a key, with tags and
/// references reported apart because they fail differently.
/// </summary>
public class KeyUsageSummaryTests
{
    private static KeyUsageSummary Status() => new()
    {
        Counts =
        [
            new(MetadataUsageKind.Tag, "asset", "assets", 44),
            new(MetadataUsageKind.Tag, "device", "devices", 0),
            new(MetadataUsageKind.Reference, "display", "displays", 50),
            new(MetadataUsageKind.Reference, "access schedule", "access schedules", 4),
            new(MetadataUsageKind.Tag, "group", "groups", 1)
        ]
    };

    [Fact]
    public void References_are_described_apart_from_tags()
    {
        var usage = Status();

        Assert.Equal("50 displays and 4 access schedules", usage.DescribeReferences());
        Assert.Equal("44 assets and 1 group", usage.DescribeTags());
    }

    [Fact]
    public void Three_or_more_stores_are_joined_with_commas_and_a_final_and()
    {
        var usage = new KeyUsageSummary
        {
            Counts =
            [
                new(MetadataUsageKind.Tag, "asset", "assets", 2),
                new(MetadataUsageKind.Tag, "device", "devices", 3),
                new(MetadataUsageKind.Tag, "user", "users", 1)
            ]
        };

        Assert.Equal("2 assets, 3 devices and 1 user", usage.DescribeTags());
    }

    [Fact]
    public void A_store_with_no_rows_is_left_out_and_an_unused_key_reads_as_unused()
    {
        var usage = new KeyUsageSummary
        {
            Counts = [new(MetadataUsageKind.Tag, "asset", "assets", 0), new(MetadataUsageKind.Reference, "display", "displays", 0)]
        };

        Assert.False(usage.IsUsed);
        Assert.Null(usage.DescribeTags());
        Assert.Null(usage.DescribeReferences());
    }

    [Theory]
    [InlineData("string", true)]
    [InlineData("int", true)]
    [InlineData("list", true)]
    [InlineData("multi-select", true)]
    [InlineData("List", false)]
    [InlineData("csv", false)]
    [InlineData(null, false)]
    public void Only_the_four_registry_field_types_are_known(string? fieldType, bool expected)
        => Assert.Equal(expected, MetadataFieldTypes.IsKnown(fieldType));
}
