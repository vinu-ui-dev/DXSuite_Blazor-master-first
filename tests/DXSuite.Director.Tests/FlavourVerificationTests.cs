using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// Mismatch is derived on read, so these are the whole of the rule. The load-bearing case is the
/// fleet that already exists: 233 devices with no enrolled flavour that all declare one on every
/// heartbeat, and none of them is mismatched.
/// </summary>
public class FlavourVerificationTests
{
    [Fact]
    public void ADeviceRunningWhatItWasEnrolledWithMatches()
    {
        Assert.Equal(FlavourState.Matched,
            FlavourVerification.Evaluate("EngageBrightSign", "EngageBrightSign"));
    }

    [Fact]
    public void TheComparisonIgnoresCase()
    {
        Assert.Equal(FlavourState.Matched,
            FlavourVerification.Evaluate("EngageBrightSign", "engagebrightsign"));
    }

    [Fact]
    public void ADeviceDeclaringSomethingElseIsMismatched()
    {
        Assert.Equal(FlavourState.Mismatched,
            FlavourVerification.Evaluate("EngageBrightSign", "SignageOS"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SignageOS")]
    [InlineData("avalonia")]
    public void ADeviceWithNoEnrolledFlavourIsNeverMismatched(string? declared)
    {
        // Every applet player declares a platform while having no enrolled flavour. Treating that
        // as a contradiction would light up the entire existing fleet on its next heartbeat.
        Assert.Equal(FlavourState.NotEnrolled,
            FlavourVerification.Evaluate(null, declared));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEnrolledDeviceThatHasDeclaredNothingIsNotAFault(string? declared)
    {
        Assert.Equal(FlavourState.NotReported,
            FlavourVerification.Evaluate("EngageBrightSign", declared));
    }

    [Fact]
    public void ClearingIsRefusedWhileThePlayerStillRunsTheFlavour()
    {
        // Clearing is the last step of moving a player back to the applet, not the first: a
        // cleared device raises no mismatch and falls out of the update pipeline entirely.
        Assert.False(FlavourVerification.CanClearFlavour("EngageBrightSign", "EngageBrightSign"));
    }

    [Theory]
    [InlineData("SignageOS")]
    [InlineData(null)]
    public void ClearingIsAllowedOnceThePlayerNoLongerDeclaresIt(string? declared)
    {
        Assert.True(FlavourVerification.CanClearFlavour("EngageBrightSign", declared));
    }
}
