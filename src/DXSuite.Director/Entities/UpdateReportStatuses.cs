namespace DXSuite.Director.Entities;

/// <summary>
/// The statuses a player reports against an update. Constants rather than literals because three
/// components have to agree on them: the controller that receives them, the batch service that
/// advances on them, and the player that sends them.
/// </summary>
public static class UpdateReportStatuses
{
    public const string UpdateStarted = "UPDATE_STARTED";
    public const string DownloadComplete = "DOWNLOAD_COMPLETE";
    public const string UpdateComplete = "UPDATE_COMPLETE";
    public const string UpdateFailed = "UPDATE_FAILED";

    /// <summary>
    /// Reported by whichever bundle survived a rollback, never inferred. The channel is silent for
    /// the six minutes a rollback takes, and "a heartbeat carrying the old version after an
    /// UPDATE_STARTED" cannot be told apart from a power cut followed by a manual downgrade.
    /// </summary>
    public const string UpdateRolledBack = "UPDATE_ROLLED_BACK";

    /// <summary>
    /// Reported once the player reaches HEALTHY after a bootloader update: three heartbeats plus
    /// 45 seconds of uptime. This, not UPDATE_COMPLETE, is what advances a bootloader batch.
    /// </summary>
    public const string Healthy = "HEALTHY";
}
