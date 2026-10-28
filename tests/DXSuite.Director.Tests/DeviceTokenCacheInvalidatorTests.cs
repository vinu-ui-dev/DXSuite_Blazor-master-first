using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// Revocation has to reach the auth handler's five-minute cache, whose key is a hash of a
/// plaintext token nothing else in the system holds.
/// </summary>
public class DeviceTokenCacheInvalidatorTests
{
    [Fact]
    public void AFreshChangeTokenHasNotFired()
    {
        var invalidator = new DeviceTokenCacheInvalidator();

        Assert.False(invalidator.GetChangeToken(1).HasChanged);
    }

    [Fact]
    public void InvalidatingFiresTheOutstandingChangeToken()
    {
        var invalidator = new DeviceTokenCacheInvalidator();
        var changeToken = invalidator.GetChangeToken(1);

        invalidator.Invalidate(1);

        Assert.True(changeToken.HasChanged);
    }

    [Fact]
    public void InvalidatingOneTokenLeavesTheOthersAlone()
    {
        var invalidator = new DeviceTokenCacheInvalidator();
        var revoked = invalidator.GetChangeToken(1);
        var untouched = invalidator.GetChangeToken(2);

        invalidator.Invalidate(1);

        Assert.True(revoked.HasChanged);
        Assert.False(untouched.HasChanged);
    }

    [Fact]
    public void EveryHolderOfTheSameTokensChangeTokenIsNotified()
    {
        // Two nodes, or two cache entries for the same token, must all drop.
        var invalidator = new DeviceTokenCacheInvalidator();
        var first = invalidator.GetChangeToken(1);
        var second = invalidator.GetChangeToken(1);

        invalidator.Invalidate(1);

        Assert.True(first.HasChanged);
        Assert.True(second.HasChanged);
    }

    [Fact]
    public void ATokenIssuedAfterRevocationStartsClean()
    {
        // The id can be cached again on the next validation, and that entry must not arrive
        // already expired.
        var invalidator = new DeviceTokenCacheInvalidator();
        invalidator.GetChangeToken(1);
        invalidator.Invalidate(1);

        Assert.False(invalidator.GetChangeToken(1).HasChanged);
    }

    [Fact]
    public void InvalidatingAnUnknownTokenIsHarmless()
    {
        var invalidator = new DeviceTokenCacheInvalidator();

        invalidator.Invalidate(99);
    }
}
