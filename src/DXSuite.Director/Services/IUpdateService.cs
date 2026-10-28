using DXSuite.Director.Entities;

namespace DXSuite.Director.Services
{
    public interface IUpdateService
    {
        // Platform Management
        Task<List<TblAppPlatform>> GetAllPlatformsAsync();
        Task<TblAppPlatform?> GetPlatformByIdAsync(int platformId);
        Task<TblAppPlatform?> GetPlatformByNameAsync(string platformName);

        // Version Management
        Task<List<TblAppVersion>> GetAllVersionsAsync(int? platformId = null);
        Task<TblAppVersion?> GetVersionByIdAsync(int appVersionId);
        /// <param name="store">
        /// The CALLER's artifact store, and it must be the caller's. This service is a singleton and
        /// opens its own scopes, and a scope opened here carries no request, so an
        /// <see cref="IPlayerArtifactStore"/> resolved inside one has an unpopulated tenant context
        /// and reads a root with no tenant segment in it.
        /// </param>
        Task<TblAppVersion> CreateVersionAsync(TblAppVersion version, IPlayerArtifactStore store, string? createdBy = null);
        Task<TblAppVersion> UpdateVersionAsync(TblAppVersion version, string? modifiedBy = null);
        /// <param name="store">The caller's artifact store; see <see cref="CreateVersionAsync"/>.</param>
        Task DeleteVersionAsync(int appVersionId, IPlayerArtifactStore store);

        // Update Check (for devices)
        Task<UpdateCheckResult> CheckForUpdateAsync(string deviceId, string? currentVersion, string? currentBootloaderVersion = null);
        Task<TblAppVersion?> GetTargetVersionForDeviceAsync(string deviceId);
        Task<TargetResolution> ResolveTargetVersionAsync(string deviceId, string artifactKind);

        // The bootloader that rescues a broken player, distinct from any rollout target.
        Task<TblAppVersion?> GetKnownGoodBootloaderAsync();
        Task SetKnownGoodBootloaderAsync(int? appVersionId, string? editedBy = null);

        // Deployment Management
        Task<List<TblUpdateDeployment>> GetAllDeploymentsAsync();
        Task<TblUpdateDeployment?> GetDeploymentByIdAsync(int deploymentId);
        Task<TblUpdateDeployment> CreateDeploymentAsync(TblUpdateDeployment deployment, List<string>? deviceIds, string? createdBy = null);
        Task<TblUpdateDeployment> UpdateDeploymentAsync(TblUpdateDeployment deployment, string? modifiedBy = null);
        Task DeleteDeploymentAsync(int deploymentId);
        Task SetDeploymentDevicesAsync(int deploymentId, List<string> deviceIds);

        // Device Update Status
        Task UpdateDeviceVersionAsync(string deviceId, string version, string status,
            string artifactKind = ArtifactKinds.App, int? generation = null, string? slot = null);
        Task LogUpdateEventAsync(string? deviceId, int appVersionId, string eventType, string? message, string? previousVersion, string? newVersion, string? ipAddress);

        // File Management
        Task<string> GetVersionDownloadUrlAsync(int appVersionId, string baseUrl);
        string ComputeFileHash(string filePath);

        // Reporting
        Task<List<DeviceUpdateStatusInfo>> GetDeviceUpdateStatusesAsync(int? platformId = null);
        Task<UpdateSummary> GetUpdateSummaryAsync(int? platformId = null);
    }

    /// <summary>
    /// Why a device has no artifact of a given kind waiting for it. BLOCKED is deliberately not
    /// silence: an unresolvable target used to yield UpdateAvailable = false, which the reporting
    /// rendered as "up to date", so a fleet stuck behind a bootloader requirement looked healthy.
    /// </summary>
    public enum TargetResolutionOutcome
    {
        /// <summary>Nothing is targeted at this device, which is the ordinary case.</summary>
        None,

        /// <summary>A version resolved and may be offered.</summary>
        Resolved,

        /// <summary>Something is targeted but must not be offered. The reason says what to fix.</summary>
        Blocked
    }

    /// <param name="Version">Set only when the outcome is Resolved.</param>
    /// <param name="BlockedReason">Operator-facing, set only when the outcome is Blocked.</param>
    /// <param name="RequiredBootloaderVersion">
    /// Set when the block is a bootloader floor, so the caller can offer the satisfying bootloader
    /// instead of simply refusing. Absent on every other kind of block.
    /// </param>
    /// <param name="AllowsDowngrade">
    /// Whether whatever targeted this version is permitted to send a device backwards. A device pin
    /// always may, being an explicit per-device act; a deployment may only when it says so.
    /// </param>
    public sealed record TargetResolution(
        TargetResolutionOutcome Outcome,
        TblAppVersion? Version = null,
        string? BlockedReason = null,
        string? RequiredBootloaderVersion = null,
        bool AllowsDowngrade = false)
    {
        public static readonly TargetResolution None = new(TargetResolutionOutcome.None);

        public static TargetResolution Resolved(TblAppVersion version, bool allowsDowngrade = false)
            => new(TargetResolutionOutcome.Resolved, version, AllowsDowngrade: allowsDowngrade);

        public static TargetResolution Blocked(string reason, string? requiredBootloaderVersion = null)
            => new(TargetResolutionOutcome.Blocked, null, reason, requiredBootloaderVersion);
    }

    public class UpdateCheckResult
    {
        public bool UpdateAvailable { get; set; }
        public string? CurrentVersion { get; set; }
        public string? TargetVersion { get; set; }
        public int? AppVersionId { get; set; }
        public string? DownloadUrl { get; set; }
        public long? FileSize { get; set; }
        public string? FileHash { get; set; }
        public string? ReleaseNotes { get; set; }
        public bool RequiresReboot { get; set; } = true;

        /// <summary>
        /// Which artifact this offer is. The server never offers both kinds in one response:
        /// the bootloader goes first, then nothing until the device reports the new bootloader
        /// version, then the app bundle.
        /// </summary>
        public string ArtifactKind { get; set; } = ArtifactKinds.App;

        /// <summary>
        /// The offered bundle's bootloader floor, passed to the device so it can refuse as a last
        /// line. Null on a bootloader offer and on a bundle that names no floor.
        /// </summary>
        public string? MinBootloaderVersion { get; set; }

        /// <summary>True when an app bundle exists for this device but must not be offered yet.</summary>
        public bool Blocked { get; set; }

        public string? BlockedReason { get; set; }
    }

    public class DeviceUpdateStatusInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string? DeviceName { get; set; }
        public string? LocationName { get; set; }
        public string? PlatformName { get; set; }
        public string? CurrentVersion { get; set; }
        public string? TargetVersion { get; set; }
        public string? CurrentBootloaderVersion { get; set; }
        public string? TargetBootloaderVersion { get; set; }
        public string UpdateStatus { get; set; } = "UNKNOWN";
        public string? BlockedReason { get; set; }
        public DateTime? LastUpdateAttempt { get; set; }
        public string? LastUpdateStatus { get; set; }
        public DateTime? LastSeen { get; set; }
    }

    public class UpdateSummary
    {
        public int TotalDevices { get; set; }
        public int UpToDate { get; set; }
        public int UpdateAvailable { get; set; }
        public int UpdateInProgress { get; set; }
        public int UpdateFailed { get; set; }
        public int Blocked { get; set; }
        public int Unknown { get; set; }
        public Dictionary<string, int> VersionDistribution { get; set; } = new();
    }
}
