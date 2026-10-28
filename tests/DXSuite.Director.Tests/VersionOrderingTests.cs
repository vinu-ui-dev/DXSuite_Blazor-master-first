using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The bootloader floor is the one place this pipeline orders versions rather than comparing them
/// for equality, and getting it wrong either bricks a player or blocks a healthy fleet.
/// </summary>
public class VersionOrderingTests
{
    [Theory]
    [InlineData("9.1.151", "9.1.151")]
    [InlineData("9.1.152", "9.1.151")]
    [InlineData("10.0.0", "9.9.9")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("2.0", "1.99.99")]
    public void AHighEnoughVersionSatisfiesTheFloor(string candidate, string minimum)
    {
        Assert.True(VersionOrdering.Satisfies(candidate, minimum));
    }

    [Theory]
    [InlineData("9.1.99", "9.1.151")]
    [InlineData("9.0.211", "9.1.0")]
    [InlineData("1.2.0", "1.2.1")]
    public void ATooLowVersionDoesNot(string candidate, string minimum)
    {
        Assert.False(VersionOrdering.Satisfies(candidate, minimum));
    }

    [Fact]
    public void SegmentsAreComparedNumericallyNotAsText()
    {
        // The case the whole helper exists for: as strings, "9.1.99" sorts above "9.1.151".
        Assert.True(VersionOrdering.Satisfies("9.1.151", "9.1.99"));
        Assert.False(VersionOrdering.Satisfies("9.1.99", "9.1.151"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFloorIsSatisfiedByAnything(string? minimum)
    {
        Assert.True(VersionOrdering.Satisfies("1.0.0", minimum));
        Assert.True(VersionOrdering.Satisfies(null, minimum));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("1.0.0-rc1")]
    public void AnUnreadableCandidateSatisfiesNothing(string? candidate)
    {
        // "Unknown" and "old enough to brick it" look the same from here, so the unreadable case
        // must fail closed rather than be treated as zero or as current.
        Assert.False(VersionOrdering.Satisfies(candidate, "9.1.151"));
    }

    [Fact]
    public void AnUnreadableFloorGatesNothing()
    {
        // A floor nobody can parse must not strand a fleet; the mistake belongs to whoever typed it.
        Assert.True(VersionOrdering.Satisfies("9.1.151", "not-a-version"));
    }

    [Fact]
    public void TheComparerSortsLowestFirstAndPutsJunkLast()
    {
        var sorted = new[] { "9.1.151", "junk", "9.1.99", "10.0.0" }
            .OrderBy(v => v, VersionOrdering.Comparer)
            .ToArray();

        Assert.Equal(["9.1.99", "9.1.151", "10.0.0", "junk"], sorted);
    }
}
