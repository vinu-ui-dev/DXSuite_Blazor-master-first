using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// What a device's report does to its row in a batched deployment.
///
/// The two kinds settle at different moments, and that is the whole of the difference: an app
/// bundle is done when the device runs it, a bootloader is done when the device has proved it can
/// boot on it. A bootloader that installs and then fails to boot is precisely the case batching
/// exists to contain, so completing it on the install would defeat the mechanism.
/// </summary>
public static class BatchProgressRules
{
    /// <summary>
    /// True when a device reporting <paramref name="reportedVersion"/> settles the row, which is
    /// only ever the case for an app bundle. A bootloader install parks the row instead.
    /// </summary>
    public static bool CompletesOnVersionReport(string? artifactKind, string? targetVersion, string? reportedVersion)
        => VersionsAgree(targetVersion, reportedVersion)
           && artifactKind != ArtifactKinds.Bootloader;

    /// <summary>
    /// True when a device reporting the version it now runs should park rather than settle: a
    /// bootloader is installed but unproven until it reports healthy.
    /// </summary>
    public static bool ParksOnVersionReport(string? artifactKind, string? targetVersion, string? reportedVersion)
        => VersionsAgree(targetVersion, reportedVersion)
           && artifactKind == ArtifactKinds.Bootloader;

    /// <summary>
    /// True when a health report settles the row. Only bootloader rows wait on health, and only
    /// for the bootloader the device actually ended up running: a healthy report from a player
    /// still on the old one says the rollout has not reached it, not that it succeeded.
    /// </summary>
    public static bool CompletesOnHealthReport(string? artifactKind, string? targetVersion, string? runningVersion)
        => artifactKind == ArtifactKinds.Bootloader
           && VersionsAgree(targetVersion, runningVersion);

    private static bool VersionsAgree(string? target, string? reported)
        => !string.IsNullOrWhiteSpace(target)
           && string.Equals(target, reported, StringComparison.OrdinalIgnoreCase);
}
