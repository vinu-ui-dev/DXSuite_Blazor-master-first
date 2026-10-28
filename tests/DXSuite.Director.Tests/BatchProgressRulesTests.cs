using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// The point of batching is to stop before batch two. These rules are what decide whether a batch
/// has actually succeeded, so a bootloader counted as done too early defeats the whole mechanism.
/// </summary>
public class BatchProgressRulesTests
{
    private const string Target = "2.0.1";

    [Fact]
    public void AnAppBundleIsDoneWhenTheDeviceRunsIt()
    {
        Assert.True(BatchProgressRules.CompletesOnVersionReport(ArtifactKinds.App, Target, Target));
        Assert.False(BatchProgressRules.ParksOnVersionReport(ArtifactKinds.App, Target, Target));
    }

    [Fact]
    public void ABootloaderInstallParksTheRowInsteadOfCompletingIt()
    {
        // The device has the artifact but has not yet proved it can boot on it.
        Assert.True(BatchProgressRules.ParksOnVersionReport(ArtifactKinds.Bootloader, Target, Target));
        Assert.False(BatchProgressRules.CompletesOnVersionReport(ArtifactKinds.Bootloader, Target, Target));
    }

    [Fact]
    public void AHealthReportCompletesABootloaderRow()
    {
        Assert.True(BatchProgressRules.CompletesOnHealthReport(ArtifactKinds.Bootloader, Target, Target));
    }

    [Fact]
    public void AHealthReportFromAPlayerStillOnTheOldBootloaderCompletesNothing()
    {
        // That report says the rollout has not reached this player, not that it succeeded.
        Assert.False(BatchProgressRules.CompletesOnHealthReport(ArtifactKinds.Bootloader, Target, "1.9.0"));
    }

    [Fact]
    public void AHealthReportDoesNotCompleteAnAppRow()
    {
        // App rows settle on the version report; health would settle them a second time.
        Assert.False(BatchProgressRules.CompletesOnHealthReport(ArtifactKinds.App, Target, Target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.9.0")]
    public void AReportOfTheWrongVersionSettlesNothing(string? reported)
    {
        Assert.False(BatchProgressRules.CompletesOnVersionReport(ArtifactKinds.App, Target, reported));
        Assert.False(BatchProgressRules.ParksOnVersionReport(ArtifactKinds.Bootloader, Target, reported));
        Assert.False(BatchProgressRules.CompletesOnHealthReport(ArtifactKinds.Bootloader, Target, reported));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ADeploymentWithNoTargetVersionSettlesNothing(string? target)
    {
        Assert.False(BatchProgressRules.CompletesOnVersionReport(ArtifactKinds.App, target, Target));
        Assert.False(BatchProgressRules.CompletesOnHealthReport(ArtifactKinds.Bootloader, target, Target));
    }
}
