using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// Resolution answers "what is targeted at this device", which is not the same question as
/// "should this device move". Conflating them let a deployment naming an older build roll every
/// device it covered backwards onto it, and on the bench that landed two players on a bundle that
/// could not authenticate, which no later deployment could reach.
///
/// These pin the ordering the refusal is built on. The refusal itself is one comparison against
/// <see cref="VersionOrdering.Satisfies"/>, so what can go wrong is the comparison, not the branch.
/// </summary>
public class DowngradeRefusalTests
{
    /// <summary>True when moving from <paramref name="running"/> to <paramref name="target"/> goes backwards.</summary>
    private static bool IsBackwards(string? running, string? target)
    {
        if (!VersionOrdering.TryParse(running, out _) || !VersionOrdering.TryParse(target, out _))
            return false;

        return !VersionOrdering.Satisfies(target, running);
    }

    [Theory]
    [InlineData("2.0.3", "2.0.1")]
    [InlineData("2.0.3", "2.0.2")]
    [InlineData("2.0.0", "1.0.8")]
    [InlineData("10.0.0", "9.9.9")]
    [InlineData("9.1.151", "9.1.99")]
    public void AnOlderTargetIsBackwards(string running, string target)
    {
        Assert.True(IsBackwards(running, target));
    }

    [Theory]
    [InlineData("2.0.1", "2.0.3")]
    [InlineData("1.0.8", "2.0.0")]
    [InlineData("9.1.99", "9.1.151")]
    public void ANewerTargetIsNot(string running, string target)
    {
        Assert.False(IsBackwards(running, target));
    }

    [Theory]
    [InlineData("2.0.3", "2.0.3")]
    [InlineData("1.2", "1.2.0")]
    public void TheSameVersionIsNotBackwards(string running, string target)
    {
        // The no-op case. A deployment naming what a device already runs must resolve to nothing
        // at all, not to a refusal an operator then has to interpret.
        Assert.False(IsBackwards(running, target));
    }

    [Theory]
    [InlineData(null, "2.0.1")]
    [InlineData("", "2.0.1")]
    [InlineData("unknown", "2.0.1")]
    [InlineData("2.0.3", "2.0.1-rc1")]
    public void AnUnreadableVersionOnEitherSideIsNotTreatedAsBackwards(string? running, string target)
    {
        // Deliberately permissive, and the opposite of the bootloader floor's fail-closed rule.
        // A floor protects a device from a bundle it cannot run, so guessing there risks a brick.
        // Here the risk runs the other way: refusing on an unreadable version would strand a device
        // that an operator is explicitly trying to move, on no evidence at all. If we cannot tell
        // which direction the move goes, we are not entitled to block it.
        Assert.False(IsBackwards(running, target));
    }

    [Fact]
    public void TheComparisonIsNumericNotTextual()
    {
        // As strings "2.0.10" sorts below "2.0.9", which would refuse a real upgrade.
        Assert.False(IsBackwards("2.0.9", "2.0.10"));
        Assert.True(IsBackwards("2.0.10", "2.0.9"));
    }
}
