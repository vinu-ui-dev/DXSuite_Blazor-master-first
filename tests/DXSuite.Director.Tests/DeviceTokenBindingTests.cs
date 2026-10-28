using DXSuite.Director.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// A device token authenticated but was never scoped: the bound DeviceId travelled downstream as a
/// claim nobody compared, so a leaked token read any device's content in the tenant.
/// </summary>
public class DeviceTokenBindingTests
{
    private static HttpContext ContextBoundTo(string? deviceId)
    {
        var context = new DefaultHttpContext();
        if (deviceId != null)
            context.Items["DeviceId"] = deviceId;
        return context;
    }

    [Fact]
    public void ATokenMayActForTheDeviceItIsBoundTo()
    {
        Assert.True(DeviceTokenBinding.Matches(ContextBoundTo("player-1"), "player-1"));
    }

    [Fact]
    public void ATokenMayNotActForAnotherDevice()
    {
        Assert.False(DeviceTokenBinding.Matches(ContextBoundTo("player-1"), "player-2"));
    }

    [Fact]
    public void TheComparisonIgnoresCase()
    {
        // Device ids are client-chosen and have historically been hostnames, whose case varies.
        Assert.True(DeviceTokenBinding.Matches(ContextBoundTo("Player-1"), "player-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnUnboundTokenPasses(string? bound)
    {
        // Binding happens on the first heartbeat, after the device row exists, so a freshly
        // paired player legitimately arrives unbound exactly once.
        Assert.True(DeviceTokenBinding.Matches(ContextBoundTo(bound), "player-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ABoundTokenIsRefusedARequestNamingNoDevice(string? requested)
    {
        Assert.False(DeviceTokenBinding.Matches(ContextBoundTo("player-1"), requested));
    }
}
