using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DXSuite.Director.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UpdateController : ControllerBase
    {
        private readonly IUpdateService _updateService;
        private readonly IBatchDeploymentService _batchService;
        private readonly IPlayerArtifactStore _artifactStore;
        private readonly IDeviceDwsCredentialService _dwsCredentials;
        private readonly IPlayerCallbackUrlResolver _callbackUrls;
        private readonly ILogger<UpdateController> _logger;

        // Built from the current request so download URLs point at the host the caller
        // reached — per tenant subdomain in multi-tenant deployments.
        private string SiteUrl => _callbackUrls.Resolve();

        public UpdateController(IUpdateService updateService, IBatchDeploymentService batchService,
            IPlayerArtifactStore artifactStore, IPlayerCallbackUrlResolver callbackUrls,
            IDeviceDwsCredentialService dwsCredentials,
            ILogger<UpdateController> logger)
        {
            _updateService = updateService;
            _batchService = batchService;
            _artifactStore = artifactStore;
            _dwsCredentials = dwsCredentials;
            _callbackUrls = callbackUrls;
            _logger = logger;
        }

        #region Public API (for devices)

        /// <summary>
        /// Check if an update is available for a device.
        /// Called by player clients during sync.
        /// </summary>
        [HttpGet("check/{deviceId}")]
        [Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken)]
        public async Task<IActionResult> CheckForUpdate(string deviceId, [FromQuery] string? currentVersion)
        {
            if (!DeviceTokenBinding.Matches(HttpContext, deviceId))
                return Forbid();

            try
            {
                var result = await _updateService.CheckForUpdateAsync(deviceId, currentVersion);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for update for device {DeviceId}", deviceId);
                return StatusCode(500, new { error = "Failed to check for update" });
            }
        }

        /// <summary>
        /// Report update status from a device.
        /// Called by player clients after download/install.
        /// </summary>
        [HttpPost("status")]
        [Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken)]
        public async Task<IActionResult> ReportUpdateStatus([FromBody] UpdateStatusReport report)
        {
            if (report == null || string.IsNullOrEmpty(report.DeviceId))
            {
                return BadRequest("DeviceId is required");
            }

            if (!DeviceTokenBinding.Matches(HttpContext, report.DeviceId))
            {
                _logger.LogWarning("Device token bound to {BoundDeviceId} reported status for {RequestedDeviceId}",
                    HttpContext.Items["DeviceId"], report.DeviceId);
                return Forbid();
            }

            try
            {
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

                // The reported artifact decides which version line this lands on. Taken from the
                // version row rather than trusted from the payload, because the device is
                // reporting against an artifact the server offered it.
                var artifactKind = ArtifactKinds.App;
                if (report.AppVersionId.HasValue)
                {
                    var reported = await _updateService.GetVersionByIdAsync(report.AppVersionId.Value);
                    if (reported != null)
                        artifactKind = reported.ArtifactKind;
                }

                await _updateService.UpdateDeviceVersionAsync(
                    report.DeviceId,
                    report.Version ?? "",
                    report.Status ?? "UNKNOWN",
                    artifactKind,
                    report.Generation,
                    report.Slot
                );

                // Drive the batch from the device's own report. Nothing called
                // HandleDeviceVersionUpdateAsync before this, so a batched deployment could only
                // ever advance through its failure path and never past batch one on success.
                switch (report.Status)
                {
                    case UpdateReportStatuses.UpdateComplete:
                        await _batchService.HandleDeviceVersionUpdateAsync(report.DeviceId, report.Version ?? "");
                        break;
                    case UpdateReportStatuses.Healthy:
                        await _batchService.HandleDeviceHealthyAsync(report.DeviceId);
                        break;
                    case UpdateReportStatuses.UpdateRolledBack:
                        await _batchService.HandleDeviceRolledBackAsync(report.DeviceId, report.Version);
                        break;
                    case UpdateReportStatuses.UpdateFailed:
                        await _batchService.HandleDeviceUpdateFailedAsync(
                            report.DeviceId, report.Message ?? "Device reported an update failure.");
                        break;
                }

                // Log the event if we have version info
                if (report.AppVersionId.HasValue)
                {
                    await _updateService.LogUpdateEventAsync(
                        report.DeviceId,
                        report.AppVersionId.Value,
                        report.Status ?? "VERSION_REPORTED",
                        report.Message,
                        report.PreviousVersion,
                        report.Version,
                        ipAddress
                    );
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting update status for device {DeviceId}", report.DeviceId);
                return StatusCode(500, new { error = "Failed to report update status" });
            }
        }

        /// <summary>
        /// Records the per-device DWS password a player generated for itself after enrollment.
        ///
        /// The package's bootstrap password is shared by every player made from that card, so a
        /// leaked card would otherwise open every player it produced. Director's device row is the
        /// authoritative store, because Sentinel's own credentials column has no write path and no
        /// data protector.
        /// </summary>
        [HttpPost("dws-password")]
        [Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken)]
        public async Task<IActionResult> ReportDwsPassword([FromBody] DwsPasswordReport report)
        {
            if (report == null || string.IsNullOrEmpty(report.DeviceId))
                return BadRequest("DeviceId is required");

            if (string.IsNullOrWhiteSpace(report.Password))
                return BadRequest("Password is required");

            if (!DeviceTokenBinding.Matches(HttpContext, report.DeviceId))
            {
                _logger.LogWarning("Device token bound to {BoundDeviceId} reported a DWS password for {RequestedDeviceId}",
                    HttpContext.Items["DeviceId"], report.DeviceId);
                return Forbid();
            }

            if (!await _dwsCredentials.RecordRotationAsync(report.DeviceId, report.Password))
                return NotFound();

            return Ok(new { success = true });
        }

        #endregion

        #region Admin API - Platforms

        /// <summary>
        /// Get all platforms (BrightSign, WebOS, etc.)
        /// </summary>
        [HttpGet("platforms")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> GetPlatforms()
        {
            if (this.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var platforms = await _updateService.GetAllPlatformsAsync();
                return Ok(platforms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting platforms");
                return StatusCode(500, new { error = "Failed to get platforms" });
            }
        }

        #endregion

        #region Admin API - Versions

        /// <summary>
        /// Get all versions, optionally filtered by platform.
        /// </summary>
        [HttpGet("versions")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> GetVersions([FromQuery] int? platformId = null)
        {
            if (this.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var versions = await _updateService.GetAllVersionsAsync(platformId);
                return Ok(versions.Select(v => new
                {
                    v.AppVersionId,
                    v.PlatformId,
                    PlatformName = v.Platform?.PlatformName,
                    v.VersionNumber,
                    v.VersionName,
                    v.ReleaseNotes,
                    v.ZipFileName,
                    v.ZipFilePath,
                    v.ZipFileSize,
                    v.ZipFileHash,
                    v.CreatedDate,
                    v.CreatedBy
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting versions");
                return StatusCode(500, new { error = "Failed to get versions" });
            }
        }

        /// <summary>
        /// Get a specific version by ID.
        /// </summary>
        [HttpGet("versions/{appVersionId}")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> GetVersion(int appVersionId)
        {
            if (this.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var version = await _updateService.GetVersionByIdAsync(appVersionId);
                if (version == null)
                    return NotFound();

                return Ok(version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting version {AppVersionId}", appVersionId);
                return StatusCode(500, new { error = "Failed to get version" });
            }
        }

        /// <summary>
        /// Upload a new artifact. The version is read from the artifact's own manifest.json.
        ///
        /// The operator no longer types a version: a typed value that disagreed with the manifest
        /// would leave the device reporting one version forever while the pipeline believed
        /// another, and no comparison in the resolver would ever converge.
        /// </summary>
        [HttpPost("versions")]
        [RequestSizeLimit(500_000_000)] // 500 MB max
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> UploadVersion([FromForm] VersionUploadRequest request)
        {
            if (this.CheckScope(ApiKeyScopes.UpdatePublish) is { } denied) return denied;

            if (request.File == null || request.File.Length == 0)
                return BadRequest("File is required");

            if (request.PlatformId <= 0)
                return BadRequest("PlatformId is required");

            var artifactKind = string.IsNullOrWhiteSpace(request.ArtifactKind)
                ? ArtifactKinds.App
                : request.ArtifactKind;

            if (!ArtifactKinds.IsValid(artifactKind))
                return BadRequest($"ArtifactKind must be {ArtifactKinds.App} or {ArtifactKinds.Bootloader}");

            string? stagingPath = null;

            try
            {
                var platform = await _updateService.GetPlatformByIdAsync(request.PlatformId);
                if (platform == null)
                    return BadRequest("Invalid platform");

                // Staged first, because the destination directory is named after a version that
                // only the artifact knows. Staging lives in the artifact root too, so an upload
                // never touches the web root even transiently.
                var stagingDirectory = Path.Combine(_artifactStore.GetRoot(), "_staging");
                Directory.CreateDirectory(stagingDirectory);
                stagingPath = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}.zip");

                using (var stream = new FileStream(stagingPath, FileMode.Create))
                {
                    await request.File.CopyToAsync(stream);
                }

                var manifestVersion = ArtifactManifest.TryReadVersion(stagingPath, out var manifestError);

                // A manifest-less artifact is refused unless the caller supplied a version, which
                // is how platforms whose bundles predate the convention keep working.
                var versionNumber = manifestVersion ?? request.VersionNumber;
                if (string.IsNullOrWhiteSpace(versionNumber))
                    return BadRequest(manifestError ?? "Artifact carries no version.");

                if (manifestVersion != null
                    && !string.IsNullOrWhiteSpace(request.VersionNumber)
                    && !string.Equals(manifestVersion, request.VersionNumber, StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(
                        $"Artifact manifest declares version {manifestVersion}, but {request.VersionNumber} was supplied.");
                }

                // Bootloaders nest under their own directory because an app bundle and a bootloader
                // can share a version number under the one platform row.
                var relativeDirectory = artifactKind == ArtifactKinds.Bootloader
                    ? $"PlayerContent/{platform.FolderName}/bootloader/{versionNumber}"
                    : $"PlayerContent/{platform.FolderName}/{versionNumber}";

                var fileName = artifactKind == ArtifactKinds.Bootloader
                    ? $"Bootloader_{platform.FolderName}_v{versionNumber}.zip"
                    : $"DirectorClient_{platform.FolderName}_v{versionNumber}.zip";

                string storedPath;
                await using (var staged = System.IO.File.OpenRead(stagingPath))
                {
                    storedPath = await _artifactStore.SaveAsync(staged, $"{relativeDirectory}/{fileName}");
                }

                System.IO.File.Delete(stagingPath);
                stagingPath = null;

                var filePath = _artifactStore.ResolveExisting(storedPath)
                    ?? throw new InvalidOperationException("Artifact vanished immediately after being stored.");

                var fileHash = _updateService.ComputeFileHash(filePath);

                var version = new TblAppVersion
                {
                    PlatformId = request.PlatformId,
                    ArtifactKind = artifactKind,
                    VersionNumber = versionNumber,
                    VersionName = request.VersionName,
                    ReleaseNotes = request.ReleaseNotes,
                    MinBootloaderVersion = artifactKind == ArtifactKinds.App ? request.MinBootloaderVersion : null,
                    ZipFileName = fileName,
                    ZipFilePath = storedPath,
                    ZipFileSize = request.File.Length,
                    ZipFileHash = fileHash
                };

                var createdVersion = await _updateService.CreateVersionAsync(version, _artifactStore, User.Identity?.Name);

                // Nothing recorded who put code on a fleet.
                await _updateService.LogUpdateEventAsync(
                    deviceId: null,
                    appVersionId: createdVersion.AppVersionId,
                    eventType: "VERSION_PUBLISHED",
                    message: $"Published {artifactKind} version {createdVersion.VersionNumber} for platform {platform.PlatformName}.",
                    previousVersion: null,
                    newVersion: createdVersion.VersionNumber,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

                return Ok(new
                {
                    createdVersion.AppVersionId,
                    createdVersion.VersionNumber,
                    createdVersion.ArtifactKind,
                    createdVersion.ZipFilePath,
                    createdVersion.ZipFileSize,
                    createdVersion.ZipFileHash,
                    DownloadUrl = $"{SiteUrl}/api/Update/versions/{createdVersion.AppVersionId}/artifact"
                });
            }
            catch (InvalidOperationException ex)
            {
                // The service's refusals are operator-facing: a missing manifest, a duplicate
                // (platform, kind, version), an unknown kind.
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading version");
                return StatusCode(500, new { error = "Failed to upload version", details = ex.Message });
            }
            finally
            {
                if (stagingPath != null && System.IO.File.Exists(stagingPath))
                    System.IO.File.Delete(stagingPath);
            }
        }

        /// <summary>
        /// Streams an artifact to the player that was offered it.
        ///
        /// Replaces serving artifacts as static files out of the web root, which left an anonymous
        /// path to every tenant's bundles. The bootloader's own anonymous route is separate and
        /// deliberate, because the recovery courier reaches a player with no running app and
        /// therefore no device token.
        /// </summary>
        [HttpGet("versions/{appVersionId}/artifact")]
        [Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken)]
        public async Task<IActionResult> GetVersionArtifact(int appVersionId)
        {
            var version = await _updateService.GetVersionByIdAsync(appVersionId);
            if (version == null)
                return NotFound();

            var absolutePath = _artifactStore.ResolveExisting(version.ZipFilePath);
            if (absolutePath == null)
            {
                _logger.LogError("Artifact for version {AppVersionId} is missing at {Path}", appVersionId, version.ZipFilePath);
                return NotFound();
            }

            var stream = System.IO.File.OpenRead(absolutePath);
            return File(stream, "application/zip", version.ZipFileName);
        }

        /// <summary>
        /// Update version metadata (not the file).
        /// </summary>
        [HttpPut("versions/{appVersionId}")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> UpdateVersion(int appVersionId, [FromBody] VersionUpdateRequest request)
        {
            if (this.CheckScope(ApiKeyScopes.UpdatePublish) is { } denied) return denied;

            try
            {
                var existing = await _updateService.GetVersionByIdAsync(appVersionId);
                if (existing == null)
                    return NotFound();

                existing.VersionName = request.VersionName;
                existing.ReleaseNotes = request.ReleaseNotes;
                existing.MinPreviousVersion = request.MinPreviousVersion;

                await _updateService.UpdateVersionAsync(existing, User.Identity?.Name);

                return Ok(existing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating version {AppVersionId}", appVersionId);
                return StatusCode(500, new { error = "Failed to update version" });
            }
        }

        /// <summary>
        /// Delete a version (only if not active).
        /// </summary>
        [HttpDelete("versions/{appVersionId}")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> DeleteVersion(int appVersionId)
        {
            if (this.CheckScope(ApiKeyScopes.UpdatePublish) is { } denied) return denied;

            try
            {
                await _updateService.DeleteVersionAsync(appVersionId, _artifactStore);
                return Ok(new { success = true });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting version {AppVersionId}", appVersionId);
                return StatusCode(500, new { error = "Failed to delete version" });
            }
        }

        #endregion

        #region Admin API - Deployments

        /// <summary>
        /// Get all deployments.
        /// </summary>
        [HttpGet("deployments")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> GetDeployments()
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var deployments = await _updateService.GetAllDeploymentsAsync();
                return Ok(deployments.Select(d => new
                {
                    d.DeploymentId,
                    d.DeploymentName,
                    d.Description,
                    d.DeploymentScope,
                    d.LocationId,
                    LocationName = d.Location?.LocationName,
                    d.AppVersionId,
                    VersionNumber = d.AppVersion?.VersionNumber,
                    PlatformName = d.AppVersion?.Platform?.PlatformName,
                    d.IsActive,
                    d.ScheduledDate,
                    DeviceCount = d.DeploymentDevices.Count,
                    d.CreatedDate,
                    d.CreatedBy,
                    d.EnableBatching,
                    d.BatchSize,
                    d.BatchStatus,
                    d.CurrentBatchNumber,
                    d.TotalBatches
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting deployments");
                return StatusCode(500, new { error = "Failed to get deployments" });
            }
        }

        /// <summary>
        /// Create a new deployment.
        /// </summary>
        [HttpPost("deployments")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> CreateDeployment([FromBody] DeploymentCreateRequest request)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            if (string.IsNullOrEmpty(request.DeploymentName))
                return BadRequest("DeploymentName is required");

            if (request.AppVersionId <= 0)
                return BadRequest("AppVersionId is required");

            try
            {
                var deployment = new TblUpdateDeployment
                {
                    DeploymentName = request.DeploymentName,
                    Description = request.Description,
                    AppVersionId = request.AppVersionId,
                    DeploymentScope = request.DeploymentScope ?? DeploymentScope.Device,
                    LocationId = request.LocationId,
                    IsActive = request.IsActive,
                    ScheduledDate = request.ScheduledDate,
                    EnableBatching = request.EnableBatching,
                    BatchSize = request.BatchSize
                };

                var created = await _updateService.CreateDeploymentAsync(deployment, request.DeviceIds, User.Identity?.Name);

                return Ok(new { created.DeploymentId, created.DeploymentName, created.EnableBatching, created.BatchSize });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating deployment");
                return StatusCode(500, new { error = "Failed to create deployment" });
            }
        }

        /// <summary>
        /// Update a deployment.
        /// </summary>
        [HttpPut("deployments/{deploymentId}")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> UpdateDeployment(int deploymentId, [FromBody] DeploymentUpdateRequest request)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var existing = await _updateService.GetDeploymentByIdAsync(deploymentId);
                if (existing == null)
                    return NotFound();

                existing.DeploymentName = request.DeploymentName ?? existing.DeploymentName;
                existing.Description = request.Description;
                existing.IsActive = request.IsActive;
                existing.ScheduledDate = request.ScheduledDate;
                if (request.EnableBatching.HasValue)
                    existing.EnableBatching = request.EnableBatching.Value;
                if (request.BatchSize.HasValue)
                    existing.BatchSize = request.BatchSize.Value;

                await _updateService.UpdateDeploymentAsync(existing, User.Identity?.Name);

                return Ok(existing);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating deployment {DeploymentId}", deploymentId);
                return StatusCode(500, new { error = "Failed to update deployment" });
            }
        }

        /// <summary>
        /// Delete a deployment.
        /// </summary>
        [HttpDelete("deployments/{deploymentId}")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> DeleteDeployment(int deploymentId)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                await _updateService.DeleteDeploymentAsync(deploymentId);
                return Ok(new { success = true });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting deployment {DeploymentId}", deploymentId);
                return StatusCode(500, new { error = "Failed to delete deployment" });
            }
        }

        /// <summary>
        /// Set devices for a deployment.
        /// </summary>
        [HttpPost("deployments/{deploymentId}/devices")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> SetDeploymentDevices(int deploymentId, [FromBody] List<string> deviceIds)
        {
            if (this.CheckScope(ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                await _updateService.SetDeploymentDevicesAsync(deploymentId, deviceIds);
                return Ok(new { success = true, deviceCount = deviceIds.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting devices for deployment {DeploymentId}", deploymentId);
                return StatusCode(500, new { error = "Failed to set deployment devices" });
            }
        }

        #endregion

        #region Admin API - Reporting

        /// <summary>
        /// Get update status for all devices.
        /// </summary>
        [HttpGet("status")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> GetDeviceStatuses([FromQuery] int? platformId = null)
        {
            if (this.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var statuses = await _updateService.GetDeviceUpdateStatusesAsync(platformId);
                return Ok(statuses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting device statuses");
                return StatusCode(500, new { error = "Failed to get device statuses" });
            }
        }

        /// <summary>
        /// Get update summary statistics.
        /// </summary>
        [HttpGet("summary")]
        [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)]
        public async Task<IActionResult> GetUpdateSummary([FromQuery] int? platformId = null)
        {
            if (this.CheckAnyScope(ApiKeyScopes.UpdatePublish, ApiKeyScopes.UpdateDeploy) is { } denied) return denied;

            try
            {
                var summary = await _updateService.GetUpdateSummaryAsync(platformId);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting update summary");
                return StatusCode(500, new { error = "Failed to get update summary" });
            }
        }

        #endregion
    }

    #region Request/Response Models

    public class UpdateStatusReport
    {
        public string DeviceId { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string? Status { get; set; }  // UPDATE_STARTED, DOWNLOAD_COMPLETE, UPDATE_COMPLETE, UPDATE_FAILED
        public string? Message { get; set; }
        public int? AppVersionId { get; set; }
        public string? PreviousVersion { get; set; }

        // Which bundle is speaking. One app update is reported by two of them, and on a rollback
        // by the old one back again, so a report without these cannot be placed in the sequence.
        public int? Generation { get; set; }
        public string? Slot { get; set; }
    }

    public class DwsPasswordReport
    {
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Plaintext on the wire and protected the moment it lands. The DWS itself speaks plain
        /// HTTP on every Sentinel probe, which the runbook records rather than designs around, and
        /// which gives "a dedicated SSID or wired" a second independent reason to be a hard
        /// prerequisite.
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }

    public class VersionUploadRequest
    {
        public int PlatformId { get; set; }

        // Defaults to App. Bootloaders are a second kind under the one platform row.
        public string? ArtifactKind { get; set; }

        // Optional, and only honoured for artifacts predating manifest.json. The manifest wins
        // wherever one exists, and a disagreement is refused rather than reconciled.
        public string? VersionNumber { get; set; }

        // App artifacts only: the lowest bootloader that can run this bundle.
        public string? MinBootloaderVersion { get; set; }
        public string? VersionName { get; set; }
        public string? ReleaseNotes { get; set; }
        public IFormFile? File { get; set; }
    }

    public class VersionUpdateRequest
    {
        public string? VersionName { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? MinPreviousVersion { get; set; }
    }

    public class DeploymentCreateRequest
    {
        public string DeploymentName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int AppVersionId { get; set; }
        public string? DeploymentScope { get; set; }  // ALL, LOCATION, DEVICE
        public int? LocationId { get; set; }
        public List<string>? DeviceIds { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? ScheduledDate { get; set; }
        public bool EnableBatching { get; set; } = false;
        public int BatchSize { get; set; } = 10;
    }

    public class DeploymentUpdateRequest
    {
        public string? DeploymentName { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public bool? EnableBatching { get; set; }
        public int? BatchSize { get; set; }
    }

    #endregion
}
