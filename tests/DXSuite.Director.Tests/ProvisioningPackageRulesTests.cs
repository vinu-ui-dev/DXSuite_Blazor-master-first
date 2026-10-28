using DXSuite.Director.Entities;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The vocabularies a provisioning package is validated against. One package is one family and one
/// DWS position, and a package "set" quietly breaks the single fingerprint, SHA-256 and bootstrap
/// password that a card is supposed to have exactly one of.
/// </summary>
public class ProvisioningPackageRulesTests
{
    [Theory]
    [InlineData("hd4")]
    [InlineData("xt4")]
    [InlineData("xt5")]
    public void TheThreeShippingFamiliesAreValid(string family)
    {
        Assert.True(HardwareFamilies.IsValid(family));
    }

    [Theory]
    [InlineData("hd5")]
    [InlineData("HD4")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsNot(string? family)
    {
        // Case-sensitive deliberately: the family string is the image selector the player reports
        // from GET /api/v1/info, and a card built for the wrong one is family-specific waste.
        Assert.False(HardwareFamilies.IsValid(family));
    }

    [Theory]
    [InlineData("OnAlways")]
    [InlineData("OnAtPark")]
    [InlineData("OffEntirely")]
    public void TheThreeDwsPositionsAreValid(string position)
    {
        Assert.True(DwsPositions.IsValid(position));
    }

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData(null)]
    public void AnUnknownDwsPositionIsRefused(string? position)
    {
        Assert.False(DwsPositions.IsValid(position));
    }

    [Fact]
    public void ADefaultPackageLeavesTheWebInspectorOffAndTheDwsClosed()
    {
        // Off in every shipped package, and the DWS is not assumed present.
        var package = new TblProvisioningPackage();

        Assert.False(package.WebInspectorEnabled);
        Assert.Equal(DwsPositions.OffEntirely, package.DwsPosition);
        Assert.False(package.IsRevoked);
    }

    [Fact]
    public void AnAuthorizationStartsArmed()
    {
        Assert.Equal(ProvisioningAuthorizationStates.Armed, new TblProvisioningAuthorization().State);
    }

    [Theory]
    [InlineData("Armed")]
    [InlineData("Converted")]
    [InlineData("Disarmed")]
    [InlineData("Expired")]
    public void TheAuthorizationLifecycleIsClosed(string state)
    {
        Assert.True(ProvisioningAuthorizationStates.IsValid(state));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("")]
    [InlineData(null)]
    public void NothingElseIsAnAuthorizationState(string? state)
    {
        Assert.False(ProvisioningAuthorizationStates.IsValid(state));
    }
}
