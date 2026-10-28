using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The rule that decides whether a takeover converges or loops forever.
///
/// The OS checks the network recovery override BEFORE it runs the card's autorun. An armed serial
/// that is served an install bootstrap on every boot therefore never runs the autorun it was just
/// given, and conversion is the only thing that would stop the serving. Measured on the LS424: the
/// courier committed the card correctly and the player still rebooted into the courier, forever.
/// </summary>
public class BootstrapHoldOffTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AServialNeverServedGetsABootstrap()
    {
        Assert.Null(RecoveryHandlerService.BootstrapHoldOff(null, Now));
    }

    [Fact]
    public void AServialServedAMomentAgoIsHeldOff()
    {
        // This is the boot immediately after the courier wrote the card. Holding off here is what
        // lets the autorun run at all.
        var holdOff = RecoveryHandlerService.BootstrapHoldOff(Now.AddSeconds(-30), Now);

        Assert.NotNull(holdOff);
        Assert.True(holdOff > TimeSpan.Zero);
    }

    [Fact]
    public void TheHoldOffLapsesSoAFailedInstallIsRetried()
    {
        // Consume on confirmation, not on serve: a bootstrap that never took must be sent again,
        // or a player whose install failed is stranded until an operator notices.
        Assert.Null(RecoveryHandlerService.BootstrapHoldOff(Now.AddHours(-1), Now));
    }

    [Fact]
    public void TheHoldOffIsNeverShorterThanTheArmedRetry()
    {
        // Retry-After is honoured in last-resort too, so a value of seconds would put a broken
        // player straight back on the endpoint.
        var holdOff = RecoveryHandlerService.BootstrapHoldOff(Now.AddMinutes(-19).AddSeconds(-59), Now);

        Assert.NotNull(holdOff);
        Assert.True(holdOff >= TimeSpan.FromMinutes(2), $"hold-off was {holdOff}");
    }

    [Fact]
    public void AClockThatMovedBackwardsStillYieldsAUsableRetry()
    {
        // A player with no battery-backed RTC boots in 1970 and corrects itself once it has the
        // network, so a stamp in the future is ordinary rather than exceptional.
        var holdOff = RecoveryHandlerService.BootstrapHoldOff(Now.AddHours(1), Now);

        Assert.NotNull(holdOff);
        Assert.True(holdOff > TimeSpan.Zero, $"hold-off was {holdOff}");
    }
}
