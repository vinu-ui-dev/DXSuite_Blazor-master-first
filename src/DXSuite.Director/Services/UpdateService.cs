using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using DXSuite.Director.Entities;
using System.Security.Cryptography;

namespace DXSuite.Director.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UpdateService> _logger;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public UpdateService(IServiceScopeFactory scopeFactory, ILogger<UpdateService> logger, IConfiguration config, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _config = config;
            _env = env;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Base URL for absolute download links. Update checks arrive as device requests on the
        /// tenant's host, so the request is authoritative in multi-tenant deployments; the global
        /// FilePaths:SiteUrl config only covers callers with no active request.
        /// </summary>
        /// <summary>Key of the tblSystemSetting row holding the blessed bootloader's AppVersionId.</summary>
        private const string KnownGoodBootloaderSettingKey = "engage:knownGoodBootloaderVersionId";

        private string ResolveBaseUrl()
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request != null)
                return $"{request.Scheme}://{request.Host}{request.PathBase}";
            return _config["FilePaths:SiteUrl"] ?? "";
        }

        #region Platform Management

        public async Task<List<TblAppPlatform>> GetAllPlatformsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblAppPlatforms
                .Where(p => p.IsActive)
                .OrderBy(p => p.PlatformName)
                .ToListAsync();
        }

        public async Task<TblAppPlatform?> GetPlatformByIdAsync(int platformId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblAppPlatforms.FindAsync(platformId);
        }

        public async Task<TblAppPlatform?> GetPlatformByNameAsync(string platformName)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblAppPlatforms
                .FirstOrDefaultAsync(p => p.PlatformName == platformName);
        }

        #endregion

        #region Version Management

        public async Task<List<TblAppVersion>> GetAllVersionsAsync(int? platformId = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var query = db.TblAppVersions
                .Include(v => v.Platform)
                .AsQueryable();

            if (platformId.HasValue)
            {
                query = query.Where(v => v.PlatformId == platformId.Value);
            }

            return await query
                .OrderByDescending(v => v.CreatedDate)
                .ToListAsync();
        }

        public async Task<TblAppVersion?> GetVersionByIdAsync(int appVersionId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblAppVersions
                .Include(v => v.Platform)
                .FirstOrDefaultAsync(v => v.AppVersionId == appVersionId);
        }

        /// <summary>
        /// Registers an uploaded artifact, taking its version from the artifact itself.
        ///
        /// Two refusals rather than warnings. A zip with no manifest has no authoritative version,
        /// and inventing one puts the pipeline and the fleet permanently out of step. A duplicate
        /// (PlatformId, ArtifactKind, VersionNumber) makes resolution ambiguous for every device on
        /// that platform, and the resolver picks by created date, so the ambiguity would resolve
        /// quietly and differently over time.
        /// </summary>
        public async Task<TblAppVersion> CreateVersionAsync(
            TblAppVersion version, IPlayerArtifactStore store, string? createdBy = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            if (!ArtifactKinds.IsValid(version.ArtifactKind))
                throw new InvalidOperationException($"Unknown artifact kind '{version.ArtifactKind}'.");

            // The store comes from the caller and must. This service is a singleton, so the scope
            // opened above carries no request; an IPlayerArtifactStore resolved from it has an
            // unpopulated tenant context and therefore a root with no tenant segment. Resolving one
            // here wrote the artifact under the tenant's root and then looked for it under the
            // bare one, so every upload on a subdomain-routed host failed with "Artifact not found"
            // naming a path that did exist.
            version.VersionNumber = ResolveVersionNumber(store, version);

            var duplicate = await db.TblAppVersions.AnyAsync(v =>
                v.PlatformId == version.PlatformId
                && v.ArtifactKind == version.ArtifactKind
                && v.VersionNumber == version.VersionNumber);

            if (duplicate)
                throw new InvalidOperationException(
                    $"Version {version.VersionNumber} already exists for this platform as a {version.ArtifactKind} artifact.");

            // A bootloader is the artifact an app bundle's floor is measured against, so it cannot
            // itself carry a floor.
            if (version.ArtifactKind == ArtifactKinds.Bootloader)
                version.MinBootloaderVersion = null;

            version.CreatedDate = DateTime.UtcNow;
            version.ModifiedDate = DateTime.UtcNow;
            version.CreatedBy = createdBy;
            version.ModifiedBy = createdBy;

            db.TblAppVersions.Add(version);
            await db.SaveChangesAsync();

            _logger.LogInformation("Created {ArtifactKind} version {VersionNumber} for platform {PlatformId}",
                version.ArtifactKind, version.VersionNumber, version.PlatformId);

            return version;
        }

        /// <summary>
        /// Takes the version from the artifact's manifest, which is the authoritative source.
        ///
        /// A caller that supplies no version is on the Engage for BrightSign path and gets a
        /// refusal if the manifest is missing. A caller that supplies one is on a legacy platform
        /// whose artifacts predate the manifest convention; its value is accepted only while no
        /// manifest contradicts it, so the two sources can never silently disagree.
        /// </summary>
        private string ResolveVersionNumber(IPlayerArtifactStore store, TblAppVersion version)
        {
            if (string.IsNullOrEmpty(version.ZipFilePath))
                throw new InvalidOperationException("Artifact path is not set, so its manifest cannot be read.");

            var fullPath = store.ResolveExisting(version.ZipFilePath)
                ?? throw new InvalidOperationException($"Artifact not found at {version.ZipFilePath}.");

            var manifestVersion = ArtifactManifest.TryReadVersion(fullPath, out var error);

            if (manifestVersion != null)
            {
                if (!string.IsNullOrWhiteSpace(version.VersionNumber)
                    && !string.Equals(version.VersionNumber, manifestVersion, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Artifact manifest declares version {manifestVersion}, but {version.VersionNumber} was supplied. "
                        + "A device reports what the manifest says, so the two must not disagree.");
                }

                return manifestVersion;
            }

            if (string.IsNullOrWhiteSpace(version.VersionNumber))
                throw new InvalidOperationException(error ?? "Artifact manifest could not be read.");

            return version.VersionNumber;
        }

        public async Task<TblAppVersion> UpdateVersionAsync(TblAppVersion version, string? modifiedBy = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblAppVersions.FindAsync(version.AppVersionId);
            if (existing == null)
                throw new KeyNotFoundException($"Version {version.AppVersionId} not found");

            existing.VersionName = version.VersionName;
            existing.ReleaseNotes = version.ReleaseNotes;
            existing.MinPreviousVersion = version.MinPreviousVersion;
            existing.ModifiedDate = DateTime.UtcNow;
            existing.ModifiedBy = modifiedBy;

            await db.SaveChangesAsync();

            _logger.LogInformation("Updated app version {AppVersionId}", version.AppVersionId);

            return existing;
        }

        public async Task DeleteVersionAsync(int appVersionId, IPlayerArtifactStore store)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblAppVersions
                .Include(v => v.Platform)
                .FirstOrDefaultAsync(v => v.AppVersionId == appVersionId);
            if (existing == null)
                throw new KeyNotFoundException($"Version {appVersionId} not found");

            // Check if any deployments use this version
            var deploymentCount = await db.TblUpdateDeployments
                .CountAsync(d => d.AppVersionId == appVersionId);
            if (deploymentCount > 0)
                throw new InvalidOperationException($"Cannot delete version. It is used by {deploymentCount} deployment(s). Delete the deployments first.");

            // The blessed bootloader is what rescues a broken player, so it is the one version that
            // must never become undeletable only by convention.
            var knownGood = await GetKnownGoodBootloaderAsync();
            if (knownGood?.AppVersionId == appVersionId)
                throw new InvalidOperationException("Cannot delete version. It is the known-good bootloader.");

            // Both pins, because a bootloader target lives in its own column.
            var targetedDeviceCount = await db.TblDevices
                .CountAsync(d => d.TargetAppVersionId == appVersionId || d.TargetBootloaderVersionId == appVersionId);
            if (targetedDeviceCount > 0)
                throw new InvalidOperationException($"Cannot delete version. {targetedDeviceCount} device(s) have this as their target version.");

            var authorisationCount = await db.TblProvisioningAuthorizations
                .CountAsync(a => a.PinnedAppVersionId == appVersionId || a.PinnedBootloaderVersionId == appVersionId);
            if (authorisationCount > 0)
                throw new InvalidOperationException($"Cannot delete version. {authorisationCount} provisioning authorisation(s) pin it.");

            // The running-device check follows the artifact kind. Comparing ClientAppVersion for a
            // bootloader row let a bootloader in use on the fleet be deleted.
            var runningDeviceCount = existing.ArtifactKind == ArtifactKinds.Bootloader
                ? await db.TblDevices.CountAsync(d => d.ClientBootloaderVersion == existing.VersionNumber && d.PlatformId == existing.PlatformId)
                : await db.TblDevices.CountAsync(d => d.ClientAppVersion == existing.VersionNumber && d.PlatformId == existing.PlatformId);
            if (runningDeviceCount > 0)
                throw new InvalidOperationException($"Cannot delete version. {runningDeviceCount} device(s) are currently running this version.");

            if (!string.IsNullOrEmpty(existing.ZipFilePath))
            {
                // The caller's store, for the reason CreateVersionAsync gives. Resolving one here
                // looked under a root with no tenant segment and deleted nothing, silently, so the
                // row went and the bytes stayed.
                store.Delete(existing.ZipFilePath);
                _logger.LogInformation("Deleted artifact {ArtifactPath}", existing.ZipFilePath);
            }

            db.TblAppVersions.Remove(existing);
            await db.SaveChangesAsync();

            _logger.LogInformation("Deleted app version {AppVersionId}: {VersionNumber}",
                appVersionId, existing.VersionNumber);
        }

        #endregion

        #region Update Check

        /// <summary>
        /// Decides the one artifact this device should be offered now, or that it should be
        /// offered nothing and why.
        ///
        /// The two kinds are never offered together. A bootloader update is an autorun replacement
        /// plus a 46 second reboot; an app update is a sub-second slot swap. Sequencing them lives
        /// here, next to MinBootloaderVersion, rather than in the player, so a bootloader
        /// requirement and the artifact that satisfies it are reasoned about in one place.
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdateAsync(string deviceId, string? currentVersion, string? currentBootloaderVersion = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var device = await LoadDeviceForResolutionAsync(db, deviceId);
            if (device == null)
            {
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    CurrentVersion = currentVersion,
                    ArtifactKind = ArtifactKinds.App
                };
            }

            var bootloaderOnDevice = currentBootloaderVersion ?? device.ClientBootloaderVersion;

            var bootloader = await ResolveForDeviceAsync(db, device, ArtifactKinds.Bootloader);
            if (bootloader is { Outcome: TargetResolutionOutcome.Resolved, Version: { } bootloaderTarget }
                && !VersionMatches(bootloaderOnDevice, bootloaderTarget.VersionNumber))
            {
                if (RefuseDowngrade(device, bootloaderTarget, bootloaderOnDevice, bootloader.AllowsDowngrade,
                        ArtifactKinds.Bootloader) is { } refusedBootloader)
                    return refusedBootloader;

                return Offer(bootloaderTarget, ArtifactKinds.Bootloader, bootloaderOnDevice);
            }

            var app = await ResolveForDeviceAsync(db, device, ArtifactKinds.App);

            if (app.Outcome == TargetResolutionOutcome.Blocked)
            {
                // A bootloader floor is a block the server can act on rather than only report:
                // the satisfying bootloader is what this device needs next.
                if (app.RequiredBootloaderVersion is { } required)
                {
                    var satisfying = await FindSatisfyingBootloaderAsync(db, device.PlatformId, required);
                    if (satisfying != null)
                        return Offer(satisfying, ArtifactKinds.Bootloader, bootloaderOnDevice);
                }

                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    Blocked = true,
                    BlockedReason = app.BlockedReason,
                    CurrentVersion = currentVersion,
                    ArtifactKind = ArtifactKinds.App
                };
            }

            if (app is not { Outcome: TargetResolutionOutcome.Resolved, Version: { } appTarget })
            {
                return new UpdateCheckResult
                {
                    UpdateAvailable = false,
                    CurrentVersion = currentVersion,
                    ArtifactKind = ArtifactKinds.App
                };
            }

            if (RefuseDowngrade(device, appTarget, currentVersion ?? device.ClientAppVersion,
                    app.AllowsDowngrade, ArtifactKinds.App) is { } refusedApp)
                return refusedApp;

            return Offer(appTarget, ArtifactKinds.App, currentVersion);
        }

        /// <summary>
        /// Refuses to send a device backwards unless whatever targeted the version said it may.
        ///
        /// Resolution answers "what is targeted at this device", which is not the same question as
        /// "should this device move". Without this, a deployment naming an older build quietly
        /// rolled every device it covered back onto it. On the bench that put two players on a
        /// bundle that could not authenticate, which no later deployment could reach, and the way
        /// out was a visit to each. At fleet scale and across sites, that is the one mistake with
        /// no remote remedy.
        ///
        /// Refusing is reported rather than silent. A deployment an operator made and the fleet is
        /// ignoring has to say so somewhere, and Blocked is the existing channel for that.
        ///
        /// Ordering is the honest comparison here rather than equality, so an unreadable version on
        /// either side falls through to the old behaviour instead of stranding a device: if we
        /// cannot tell which way the move goes, refusing it would be a guess with the same
        /// confidence as allowing it.
        /// </summary>
        private UpdateCheckResult? RefuseDowngrade(
            TblDevice device, TblAppVersion target, string? runningVersion, bool allowsDowngrade, string artifactKind)
        {
            if (allowsDowngrade)
                return null;

            if (!VersionOrdering.TryParse(runningVersion, out _) || !VersionOrdering.TryParse(target.VersionNumber, out _))
                return null;

            // Satisfies is "at least", so a target that does not reach the running version is older.
            if (VersionOrdering.Satisfies(target.VersionNumber, runningVersion))
                return null;

            var reason =
                $"Version {target.VersionNumber} is older than the {runningVersion} this device is running, "
                + "and the deployment targeting it does not allow a downgrade.";

            _logger.LogWarning(
                "Refused to downgrade device {DeviceId} from {Running} to {Target} ({Kind}): no downgrade allowance",
                device.DeviceId, runningVersion, target.VersionNumber, artifactKind);

            return new UpdateCheckResult
            {
                UpdateAvailable = false,
                Blocked = true,
                BlockedReason = reason,
                CurrentVersion = runningVersion,
                TargetVersion = target.VersionNumber,
                ArtifactKind = artifactKind
            };
        }

        private UpdateCheckResult Offer(TblAppVersion target, string artifactKind, string? currentVersion)
        {
            var needsUpdate = !VersionMatches(currentVersion, target.VersionNumber);

            return new UpdateCheckResult
            {
                UpdateAvailable = needsUpdate,
                CurrentVersion = currentVersion,
                TargetVersion = target.VersionNumber,
                AppVersionId = target.AppVersionId,
                DownloadUrl = needsUpdate ? ResolveArtifactUrl(target.AppVersionId, artifactKind) : null,
                FileSize = target.ZipFileSize,
                FileHash = target.ZipFileHash,
                ReleaseNotes = target.ReleaseNotes,
                // Only a bootloader replacement costs a reboot. This was hardcoded true, which made
                // every sub-second slot swap look like one.
                RequiresReboot = artifactKind == ArtifactKinds.Bootloader,
                ArtifactKind = artifactKind,
                // Only meaningful on an app bundle, and only as the device's last line: the
                // resolution above has already refused a bundle this device cannot run.
                MinBootloaderVersion = artifactKind == ArtifactKinds.App ? target.MinBootloaderVersion : null
            };
        }

        /// <summary>
        /// Where the device fetches this artifact.
        ///
        /// An app bundle comes from the authenticated artifact route, not the static path under
        /// the web root, which left every tenant's bundles anonymously readable.
        ///
        /// A bootloader comes from the anonymous recovery route, and it is the same URL the
        /// recovery courier is handed. One URL rather than two: a routine bootloader update has a
        /// live device token and could use the authenticated route, but two paths to the same bytes
        /// is a second thing to keep correct on the one path that has to work when everything else
        /// is broken. What makes it safe is an invariant rather than a property, that the bootloader
        /// artifact is tenant-neutral by construction, carrying no endpoint, credentials, CA, proxy
        /// or tenant identifier and reading all of them from the registry at boot.
        /// </summary>
        private string ResolveArtifactUrl(int appVersionId, string artifactKind)
        {
            if (artifactKind == ArtifactKinds.Bootloader && ResolveTenantToken() is { } tenantToken)
                return $"{ResolveBaseUrl()}/api/Recovery/bootloader/{tenantToken}/{appVersionId}";

            return $"{ResolveBaseUrl()}/api/Update/versions/{appVersionId}/artifact";
        }

        /// <summary>
        /// The tenant token the anonymous recovery route is keyed on.
        ///
        /// Read off the request rather than injected: this service is a singleton and the tenant
        /// context is scoped. Returns null when no request is in flight, which leaves a bootloader
        /// offer on the authenticated route rather than emitting a URL with no tenant in it.
        /// </summary>
        private string? ResolveTenantToken()
        {
            var tenantContext = _httpContextAccessor.HttpContext?.RequestServices
                .GetService<DXSuite.Shared.Multitenancy.ITenantContext>();

            if (tenantContext is not { IsPopulated: true } || string.IsNullOrWhiteSpace(tenantContext.Subdomain))
            {
                _logger.LogWarning(
                    "No tenant context on this request, so a bootloader offer falls back to the authenticated artifact route.");
                return null;
            }

            return tenantContext.Subdomain;
        }

        private static bool VersionMatches(string? left, string? right)
            => !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        public async Task<TblAppVersion?> GetTargetVersionForDeviceAsync(string deviceId)
            => (await ResolveTargetVersionAsync(deviceId, ArtifactKinds.App)).Version;

        /// <summary>
        /// Resolves what is targeted at a device for one artifact kind, most specific scope first.
        ///
        /// Every scope now also requires the version's platform to be active. Without that, a
        /// deployment under a deactivated platform row kept resolving and kept shipping, which is
        /// what makes deactivating a platform row a safe idiom rather than a half-measure.
        /// </summary>
        public async Task<TargetResolution> ResolveTargetVersionAsync(string deviceId, string artifactKind)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var device = await LoadDeviceForResolutionAsync(db, deviceId);
            if (device == null)
                return TargetResolution.None;

            return await ResolveForDeviceAsync(db, device, artifactKind);
        }

        private static Task<TblDevice?> LoadDeviceForResolutionAsync(IDirectorDBContext db, string deviceId)
            => db.TblDevices
                .Include(d => d.TargetAppVersion).ThenInclude(v => v!.Platform)
                .Include(d => d.TargetBootloaderVersion).ThenInclude(v => v!.Platform)
                .Include(d => d.Platform)
                .FirstOrDefaultAsync(d => d.DeviceId == deviceId);

        private async Task<TargetResolution> ResolveForDeviceAsync(IDirectorDBContext db, TblDevice device, string artifactKind)
        {
            var resolved = ResolveDevicePin(device, artifactKind)
                ?? await ResolveDeploymentAsync(db, device, artifactKind);

            if (resolved == null)
                return TargetResolution.None;

            if (resolved.Outcome != TargetResolutionOutcome.Resolved || resolved.Version is not { } version)
                return resolved;

            return ApplyBootloaderFloor(device, version, artifactKind, resolved.AllowsDowngrade);
        }

        /// <summary>
        /// The operator's explicit per-device pin, which outranks every deployment.
        ///
        /// A pin naming the wrong platform or the wrong artifact kind is reported rather than
        /// skipped. Silently ignoring it would leave an operator looking at a pin the fleet does
        /// not honour with nothing anywhere saying so; the real fix is refusing such a pin where it
        /// is set, and this guard is what surfaces the ones set before that existed.
        /// </summary>
        private TargetResolution? ResolveDevicePin(TblDevice device, string artifactKind)
        {
            var pin = artifactKind == ArtifactKinds.Bootloader
                ? device.TargetBootloaderVersion
                : device.TargetAppVersion;

            if (pin == null)
                return null;

            if (pin.ArtifactKind != artifactKind)
            {
                _logger.LogWarning(
                    "Device {DeviceId} pin {AppVersionId} is a {PinnedKind} artifact on a {RequestedKind} target",
                    device.DeviceId, pin.AppVersionId, pin.ArtifactKind, artifactKind);
                return TargetResolution.Blocked(
                    $"Pinned version {pin.VersionNumber} is a {pin.ArtifactKind} artifact and cannot satisfy a {artifactKind} target.");
            }

            if (pin.PlatformId != device.PlatformId)
            {
                _logger.LogWarning(
                    "Device {DeviceId} on platform {DevicePlatformId} is pinned to version {AppVersionId} on platform {PinPlatformId}",
                    device.DeviceId, device.PlatformId, pin.AppVersionId, pin.PlatformId);
                return TargetResolution.Blocked(
                    $"Pinned version {pin.VersionNumber} belongs to a different platform than this device.");
            }

            if (pin.Platform is { IsActive: false })
            {
                _logger.LogWarning(
                    "Device {DeviceId} is pinned to version {AppVersionId} on deactivated platform {PlatformId}",
                    device.DeviceId, pin.AppVersionId, pin.PlatformId);
                return TargetResolution.Blocked(
                    $"Pinned version {pin.VersionNumber} belongs to a deactivated platform.");
            }

            // A pin allows a downgrade unconditionally. It is per-device and explicit, which is
            // what a deployment's flag has to be opted into to become.
            return TargetResolution.Resolved(pin, allowsDowngrade: true);
        }

        private static async Task<TargetResolution?> ResolveDeploymentAsync(IDirectorDBContext db, TblDevice device, string artifactKind)
        {
            var deviceScoped = await db.TblUpdateDeploymentDevices
                .Where(dd => dd.DeviceId == device.DeviceId)
                .Include(dd => dd.Deployment)
                    .ThenInclude(d => d.AppVersion)
                .Where(dd => dd.Deployment.IsActive
                          && dd.Deployment.AppVersion.PlatformId == device.PlatformId
                          && dd.Deployment.AppVersion.ArtifactKind == artifactKind
                          && dd.Deployment.AppVersion.Platform.IsActive)
                .OrderByDescending(dd => dd.Deployment.CreatedDate)
                .Select(dd => new { dd.Deployment.AppVersion, dd.Deployment.AllowDowngrade })
                .FirstOrDefaultAsync();

            if (deviceScoped != null)
                return TargetResolution.Resolved(deviceScoped.AppVersion, deviceScoped.AllowDowngrade);

            if (device.LocationId.HasValue)
            {
                var locationScoped = await db.TblUpdateDeployments
                    .Where(d => d.DeploymentScope == DeploymentScope.Location
                             && d.LocationId == device.LocationId
                             && d.IsActive
                             && d.AppVersion.PlatformId == device.PlatformId
                             && d.AppVersion.ArtifactKind == artifactKind
                             && d.AppVersion.Platform.IsActive)
                    .Include(d => d.AppVersion)
                    .OrderByDescending(d => d.CreatedDate)
                    .Select(d => new { d.AppVersion, d.AllowDowngrade })
                    .FirstOrDefaultAsync();

                if (locationScoped != null)
                    return TargetResolution.Resolved(locationScoped.AppVersion, locationScoped.AllowDowngrade);
            }

            if (device.PlatformId.HasValue)
            {
                var allScoped = await db.TblUpdateDeployments
                    .Where(d => d.DeploymentScope == DeploymentScope.All
                             && d.IsActive
                             && d.AppVersion.PlatformId == device.PlatformId
                             && d.AppVersion.ArtifactKind == artifactKind
                             && d.AppVersion.Platform.IsActive)
                    .Include(d => d.AppVersion)
                    .OrderByDescending(d => d.CreatedDate)
                    .Select(d => new { d.AppVersion, d.AllowDowngrade })
                    .FirstOrDefaultAsync();

                if (allScoped != null)
                    return TargetResolution.Resolved(allScoped.AppVersion, allScoped.AllowDowngrade);
            }

            return null;
        }

        /// <summary>
        /// Holds back an app bundle the device's bootloader cannot run. The device refuses as a
        /// last line too, because a card can be moved between players and arrive under an older
        /// bootloader than the one it left, but the server is the decision point.
        /// </summary>
        private static TargetResolution ApplyBootloaderFloor(
            TblDevice device, TblAppVersion version, string artifactKind, bool allowsDowngrade)
        {
            if (artifactKind != ArtifactKinds.App || string.IsNullOrWhiteSpace(version.MinBootloaderVersion))
                return TargetResolution.Resolved(version, allowsDowngrade);

            if (VersionOrdering.Satisfies(device.ClientBootloaderVersion, version.MinBootloaderVersion))
                return TargetResolution.Resolved(version, allowsDowngrade);

            var reported = string.IsNullOrWhiteSpace(device.ClientBootloaderVersion)
                ? "none reported"
                : device.ClientBootloaderVersion;

            return TargetResolution.Blocked(
                $"Version {version.VersionNumber} needs bootloader {version.MinBootloaderVersion} or later; this device reports {reported}.",
                version.MinBootloaderVersion);
        }

        /// <summary>
        /// The lowest bootloader in this tenant that clears the floor. Lowest rather than newest,
        /// so a blocked device takes the smallest step that unblocks it instead of being carried
        /// onto a rollout candidate as a side effect of being stuck.
        /// </summary>
        private static async Task<TblAppVersion?> FindSatisfyingBootloaderAsync(IDirectorDBContext db, int? platformId, string requiredVersion)
        {
            if (!platformId.HasValue)
                return null;

            var candidates = await db.TblAppVersions
                .Include(v => v.Platform)
                .Where(v => v.PlatformId == platformId.Value
                         && v.ArtifactKind == ArtifactKinds.Bootloader
                         && v.Platform.IsActive)
                .ToListAsync();

            return candidates
                .Where(v => VersionOrdering.Satisfies(v.VersionNumber, requiredVersion))
                .OrderBy(v => v.VersionNumber, VersionOrdering.Comparer)
                .FirstOrDefault();
        }

        #endregion

        #region Deployment Management

        public async Task<List<TblUpdateDeployment>> GetAllDeploymentsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblUpdateDeployments
                .Include(d => d.AppVersion)
                    .ThenInclude(v => v.Platform)
                .Include(d => d.Location)
                .Include(d => d.DeploymentDevices)
                .OrderByDescending(d => d.CreatedDate)
                .ToListAsync();
        }

        public async Task<TblUpdateDeployment?> GetDeploymentByIdAsync(int deploymentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblUpdateDeployments
                .Include(d => d.AppVersion)
                    .ThenInclude(v => v.Platform)
                .Include(d => d.Location)
                .Include(d => d.DeploymentDevices)
                    .ThenInclude(dd => dd.Device)
                .FirstOrDefaultAsync(d => d.DeploymentId == deploymentId);
        }

        public async Task<TblUpdateDeployment> CreateDeploymentAsync(TblUpdateDeployment deployment, List<string>? deviceIds, string? createdBy = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            deployment.CreatedDate = DateTime.UtcNow;
            deployment.ModifiedDate = DateTime.UtcNow;
            deployment.CreatedBy = createdBy;
            deployment.ModifiedBy = createdBy;

            db.TblUpdateDeployments.Add(deployment);
            await db.SaveChangesAsync();

            // Add device assignments if scope is DEVICE
            if (deployment.DeploymentScope == DeploymentScope.Device && deviceIds != null)
            {
                var deploymentDevices = deviceIds.Select(deviceId => new TblUpdateDeploymentDevice
                {
                    DeploymentId = deployment.DeploymentId,
                    DeviceId = deviceId
                }).ToList();

                await db.TblUpdateDeploymentDevices.AddRangeAsync(deploymentDevices);
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("Created deployment {DeploymentName} for version {AppVersionId}",
                deployment.DeploymentName, deployment.AppVersionId);

            return deployment;
        }

        public async Task<TblUpdateDeployment> UpdateDeploymentAsync(TblUpdateDeployment deployment, string? modifiedBy = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblUpdateDeployments.FindAsync(deployment.DeploymentId);
            if (existing == null)
                throw new KeyNotFoundException($"Deployment {deployment.DeploymentId} not found");

            existing.DeploymentName = deployment.DeploymentName;
            existing.Description = deployment.Description;
            existing.IsActive = deployment.IsActive;
            existing.ScheduledDate = deployment.ScheduledDate;
            existing.ModifiedDate = DateTime.UtcNow;
            existing.ModifiedBy = modifiedBy;

            await db.SaveChangesAsync();

            _logger.LogInformation("Updated deployment {DeploymentId}", deployment.DeploymentId);

            return existing;
        }

        public async Task DeleteDeploymentAsync(int deploymentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblUpdateDeployments.FindAsync(deploymentId);
            if (existing == null)
                throw new KeyNotFoundException($"Deployment {deploymentId} not found");

            db.TblUpdateDeployments.Remove(existing);
            await db.SaveChangesAsync();

            _logger.LogInformation("Deleted deployment {DeploymentId}", deploymentId);
        }

        public async Task SetDeploymentDevicesAsync(int deploymentId, List<string> deviceIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Remove existing
            var existingDevices = await db.TblUpdateDeploymentDevices
                .Where(dd => dd.DeploymentId == deploymentId)
                .ToListAsync();

            db.TblUpdateDeploymentDevices.RemoveRange(existingDevices);

            // Add new
            var newDevices = deviceIds.Select(deviceId => new TblUpdateDeploymentDevice
            {
                DeploymentId = deploymentId,
                DeviceId = deviceId
            }).ToList();

            await db.TblUpdateDeploymentDevices.AddRangeAsync(newDevices);
            await db.SaveChangesAsync();

            _logger.LogInformation("Set {Count} devices for deployment {DeploymentId}",
                deviceIds.Count, deploymentId);
        }

        #endregion

        #region Device Update Status

        /// <summary>
        /// Records what a device reports about an update, against the version line the artifact
        /// kind belongs to.
        ///
        /// UPDATE_ROLLED_BACK is reported explicitly by whichever bundle survived, never inferred.
        /// The new bundle cannot report its own rollback: the channel is silent for the six minutes
        /// a rollback takes, and "a heartbeat carrying the old version after an UPDATE_STARTED"
        /// cannot be told apart from a power cut followed by a manual downgrade.
        /// </summary>
        public async Task UpdateDeviceVersionAsync(string deviceId, string version, string status,
            string artifactKind = ArtifactKinds.App, int? generation = null, string? slot = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var device = await db.TblDevices.FindAsync(deviceId);
            if (device == null)
            {
                _logger.LogWarning("Device {DeviceId} not found for version update", deviceId);
                return;
            }

            // A report can be about progress rather than about a version: HEALTHY carries none.
            // Writing an empty string would erase the version line the device is actually running.
            if (!string.IsNullOrWhiteSpace(version))
            {
                if (artifactKind == ArtifactKinds.Bootloader)
                    device.ClientBootloaderVersion = version;
                else
                    device.ClientAppVersion = version;
            }

            device.LastUpdateStatus = status;
            device.LastUpdateAttempt = DateTime.UtcNow;

            if (generation.HasValue || !string.IsNullOrEmpty(slot))
            {
                device.ExtendedData = ExtendedDataHelper.UpdateProperty<DeviceExtendedData>(
                    device.ExtendedData,
                    ext =>
                    {
                        ext.UpdateProgress ??= new UpdateProgressData();
                        ext.UpdateProgress.Generation = generation ?? ext.UpdateProgress.Generation;
                        ext.UpdateProgress.Slot = slot ?? ext.UpdateProgress.Slot;
                        ext.UpdateProgress.Status = status;
                        ext.UpdateProgress.ArtifactKind = artifactKind;
                        ext.UpdateProgress.ReportedAt = DateTime.UtcNow;
                    });
            }

            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Device {DeviceId} reported {Status} for {ArtifactKind} version {Version} (generation {Generation}, slot {Slot})",
                deviceId, status, artifactKind, version, generation, slot);
        }

        /// <summary>
        /// The bootloader blessed as the rescue image for this tenant.
        ///
        /// A tblSystemSetting row rather than a flag on the version, because "exactly one row is
        /// blessed" is a single-valued choice and a boolean kept unique by convention is an
        /// invariant living in habit. It must also stay distinct from any rollout target, or a
        /// candidate under test becomes the image that rescues players it may itself have broken.
        /// </summary>
        public async Task<TblAppVersion?> GetKnownGoodBootloaderAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var setting = await db.TblSystemSettings.FindAsync(KnownGoodBootloaderSettingKey);
            if (setting?.SettingValue is not { } raw || !int.TryParse(raw, out var appVersionId))
                return null;

            return await db.TblAppVersions
                .Include(v => v.Platform)
                .FirstOrDefaultAsync(v => v.AppVersionId == appVersionId);
        }

        public async Task SetKnownGoodBootloaderAsync(int? appVersionId, string? editedBy = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            if (appVersionId is { } id)
            {
                var version = await db.TblAppVersions.FindAsync(id)
                    ?? throw new KeyNotFoundException($"Version {id} not found");

                if (version.ArtifactKind != ArtifactKinds.Bootloader)
                    throw new InvalidOperationException("Only a bootloader artifact can be blessed as known-good.");
            }

            var existing = await db.TblSystemSettings.FindAsync(KnownGoodBootloaderSettingKey);
            if (existing == null)
            {
                db.TblSystemSettings.Add(new TblSystemSetting
                {
                    SettingKey = KnownGoodBootloaderSettingKey,
                    SettingValue = appVersionId?.ToString(),
                    LastEditDate = DateTime.UtcNow,
                    LastEditUser = editedBy
                });
            }
            else
            {
                existing.SettingValue = appVersionId?.ToString();
                existing.LastEditDate = DateTime.UtcNow;
                existing.LastEditUser = editedBy;
            }

            await db.SaveChangesAsync();

            _logger.LogInformation("Known-good bootloader set to version {AppVersionId} by {User}",
                appVersionId, editedBy ?? "unknown");
        }

        public async Task LogUpdateEventAsync(string? deviceId, int appVersionId, string eventType, string? message, string? previousVersion, string? newVersion, string? ipAddress)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Lookup event type in database
            var dbEventType = await db.TblEventTypes.FirstOrDefaultAsync(t => t.EventTypeName == eventType);
            var infoSeverity = await db.TblEventSeverities.FirstOrDefaultAsync(s => s.SeverityName == "INFO");
            var warningSeverity = await db.TblEventSeverities.FirstOrDefaultAsync(s => s.SeverityName == "WARNING");

            // Determine severity based on event type
            var severityId = eventType == "UPDATE_FAILED"
                ? (warningSeverity?.EventSeverityId ?? 2)
                : (infoSeverity?.EventSeverityId ?? 1);

            var log = new TblEventLog
            {
                EventSource = "UpdateService",
                EventTypeId = dbEventType?.EventTypeId ?? 1,
                EventTitle = $"Update {eventType}: {newVersion ?? previousVersion}",
                EventMessage = message,
                EventSeverityId = severityId,
                DeviceId = deviceId,
                CreateDate = DateTime.UtcNow,
                Details = System.Text.Json.JsonSerializer.Serialize(new
                {
                    appVersionId = appVersionId,
                    eventType = eventType,
                    previousVersion = previousVersion,
                    newVersion = newVersion,
                    ipAddress = ipAddress
                })
            };

            db.TblEventLogs.Add(log);
            await db.SaveChangesAsync();

            _logger.LogInformation("Logged update event {EventType} for device {DeviceId}",
                eventType, deviceId);
        }

        #endregion

        #region File Management

        public async Task<string> GetVersionDownloadUrlAsync(int appVersionId, string baseUrl)
        {
            var version = await GetVersionByIdAsync(appVersionId);
            if (version == null)
                throw new KeyNotFoundException($"Version {appVersionId} not found");

            return $"{baseUrl}/api/Update/versions/{version.AppVersionId}/artifact";
        }

        public string ComputeFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        #endregion

        #region Reporting

        public async Task<List<DeviceUpdateStatusInfo>> GetDeviceUpdateStatusesAsync(int? platformId = null)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var query = db.TblDevices
                .Include(d => d.Location)
                .Include(d => d.Platform)
                .Include(d => d.TargetAppVersion).ThenInclude(v => v!.Platform)
                .Include(d => d.TargetBootloaderVersion).ThenInclude(v => v!.Platform)
                .AsQueryable();

            if (platformId.HasValue)
            {
                query = query.Where(d => d.PlatformId == platformId.Value);
            }

            var devices = await query.ToListAsync();
            var result = new List<DeviceUpdateStatusInfo>();

            foreach (var device in devices)
            {
                var app = await ResolveForDeviceAsync(db, device, ArtifactKinds.App);
                var bootloader = await ResolveForDeviceAsync(db, device, ArtifactKinds.Bootloader);

                var targetVersionNumber = app.Version?.VersionNumber;

                string updateStatus;
                if (app.Outcome == TargetResolutionOutcome.Blocked)
                    // Deliberately not silence. An unresolvable target used to read as "up to date",
                    // which hid a fleet held behind a bootloader requirement.
                    updateStatus = "BLOCKED";
                else if (string.IsNullOrEmpty(device.ClientAppVersion))
                    updateStatus = "UNKNOWN";
                else if (string.IsNullOrEmpty(targetVersionNumber))
                    updateStatus = "NO_TARGET";
                else if (device.ClientAppVersion == targetVersionNumber)
                    updateStatus = "UP_TO_DATE";
                else if (device.LastUpdateStatus == "DOWNLOADING" || device.LastUpdateStatus == "INSTALLING")
                    updateStatus = "IN_PROGRESS";
                else if (device.LastUpdateStatus == "FAILED" || device.LastUpdateStatus == "UPDATE_ROLLED_BACK")
                    updateStatus = "FAILED";
                else
                    updateStatus = "UPDATE_AVAILABLE";

                result.Add(new DeviceUpdateStatusInfo
                {
                    DeviceId = device.DeviceId,
                    DeviceName = device.Description,
                    LocationName = device.Location?.LocationName,
                    PlatformName = device.Platform?.PlatformName,
                    CurrentVersion = device.ClientAppVersion,
                    TargetVersion = targetVersionNumber,
                    CurrentBootloaderVersion = device.ClientBootloaderVersion,
                    TargetBootloaderVersion = bootloader.Version?.VersionNumber,
                    UpdateStatus = updateStatus,
                    BlockedReason = app.BlockedReason,
                    LastUpdateAttempt = device.LastUpdateAttempt,
                    LastUpdateStatus = device.LastUpdateStatus,
                    LastSeen = device.Heartbeat
                });
            }

            return result;
        }

        public async Task<UpdateSummary> GetUpdateSummaryAsync(int? platformId = null)
        {
            var statuses = await GetDeviceUpdateStatusesAsync(platformId);

            var summary = new UpdateSummary
            {
                TotalDevices = statuses.Count,
                UpToDate = statuses.Count(s => s.UpdateStatus == "UP_TO_DATE"),
                UpdateAvailable = statuses.Count(s => s.UpdateStatus == "UPDATE_AVAILABLE"),
                UpdateInProgress = statuses.Count(s => s.UpdateStatus == "IN_PROGRESS"),
                UpdateFailed = statuses.Count(s => s.UpdateStatus == "FAILED"),
                Blocked = statuses.Count(s => s.UpdateStatus == "BLOCKED"),
                Unknown = statuses.Count(s => s.UpdateStatus == "UNKNOWN" || s.UpdateStatus == "NO_TARGET"),
                VersionDistribution = statuses
                    .Where(s => !string.IsNullOrEmpty(s.CurrentVersion))
                    .GroupBy(s => s.CurrentVersion!)
                    .ToDictionary(g => g.Key, g => g.Count())
            };

            return summary;
        }

        #endregion
    }
}
