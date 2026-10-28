using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// Guards the rule that a device's platform comes from what it declares and never from its
/// manufacturer. TblDevice.PlatformId is write-once, so a wrong resolution is not self-correcting.
/// </summary>
public class PlatformResolutionTests
{
    private static readonly Dictionary<string, int> Lookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EngageBrightSign"] = 9,
        ["WebOS"] = 2
    };

    [Fact]
    public void DeclaredPlatformResolves()
    {
        Assert.Equal(9, HeartbeatService.ResolvePlatformId("EngageBrightSign", Lookup));
    }

    [Fact]
    public void DeclaredPlatformIsMatchedCaseInsensitively()
    {
        Assert.Equal(9, HeartbeatService.ResolvePlatformId("engagebrightsign", Lookup));
    }

    [Theory]
    [InlineData("BrightSign")]
    [InlineData("SignageOS")]
    public void AnUnknownPlatformResolvesToNull(string declared)
    {
        // An applet player declaring "SignageOS", or one whose only identity is the manufacturer
        // string "BrightSign", must land on null rather than on a real row: null is the only
        // PlatformId that a DEVICE- or LOCATION-scoped deployment cannot target.
        Assert.Null(HeartbeatService.ResolvePlatformId(declared, Lookup));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentPlatformResolvesToNullWithoutThrowing(string? declared)
    {
        Assert.Null(HeartbeatService.ResolvePlatformId(declared, Lookup));
    }
}
