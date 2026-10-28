using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services
{
    public class BatchDeploymentService : IBatchDeploymentService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BatchDeploymentService> _logger;
        private readonly IConfiguration _config;
        private readonly IDistributedLockService _lockService;
        private readonly IWebHostEnvironment _env;

        public BatchDeploymentService(
            IServiceScopeFactory scopeFactory,
            ILogger<BatchDeploymentService> logger,
            IConfiguration config,
            IDistributedLockService lockService,
            IWebHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _config = config;
            _lockService = lockService;
            _env = env;
        }

        #region Batch Deployment Lifecycle

        public async Task<BatchDeploymentResult> StartBatchDeploymentAsync(int deploymentId, string? startedBy = null)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                $"batch-deployment-{deploymentId}",
                TimeSpan.FromMinutes(10));

            if (distLock == null)
            {
                _logger.LogWarning("Could not acquire lock for deployment {DeploymentId}", deploymentId);
                return new BatchDeploymentResult { Success = false, ErrorMessage = "Could not acquire deployment lock - another operation may be in progress" };
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var deployment = await dbContext.TblUpdateDeployments
                .Include(d => d.AppVersion)
                    .ThenInclude(v => v.Platform)
                .Include(d => d.DeploymentDevices)
                .FirstOrDefaultAsync(d => d.DeploymentId == deploymentId);

            if (deployment == null)
            {
                return new BatchDeploymentResult { Success = false, ErrorMessage = "Deployment not found" };
            }

            if (!deployment.EnableBatching)
            {
                return new BatchDeploymentResult { Success = false, ErrorMessage = "Batching is not enabled for this deployment" };
            }

            if (!deployment.IsActive)
            {
                return new BatchDeploymentResult { Success = false, ErrorMessage = "Deployment must be active to start batch processing" };
            }

            // Get all target devices for this deployment
            var targetDeviceIds = await GetTargetDeviceIdsForDeploymentInternalAsync(dbContext, deployment);

            if (!targetDeviceIds.Any())
            {
                return new BatchDeploymentResult { Success = false, ErrorMessage = "No target devices found for this deployment" };
            }

            // For WebOS platform, deploy IPK to SCAP path
            var platformName = deployment.AppVersion?.Platform?.PlatformName;
            if (platformName?.Equals("WebOS", StringComparison.OrdinalIgnoreCase) == true)
            {
                var scapResult = await DeploySCAPFileAsync(deployment.AppVersion);
                if (!scapResult.Success)
                {
                    return new BatchDeploymentResult { Success = false, ErrorMessage = scapResult.ErrorMessage };
                }
                _logger.LogInformation("Deployed IPK to SCAP path for WebOS deployment {DeploymentId}", deploymentId);
            }

            // Calculate batches
            int totalDevices = targetDeviceIds.Count;
            int batchSize = deployment.BatchSize > 0 ? deployment.BatchSize : 10;
            int totalBatches = (int)Math.Ceiling((double)totalDevices / batchSize);

            // Clear any existing progress records for this deployment
            var existingProgress = await dbContext.TblUpdateDeploymentProgress
                .Where(p => p.DeploymentId == deploymentId)
                .ToListAsync();

            if (existingProgress.Any())
            {
                dbContext.TblUpdateDeploymentProgress.RemoveRange(existingProgress);
            }

            // Create progress records for all devices
            int batchNumber = 1;
            int deviceIndex = 0;

            foreach (var deviceId in targetDeviceIds)
            {
                var device = await dbContext.TblDevices.FindAsync(deviceId);

                var progress = new TblUpdateDeploymentProgress
                {
                    DeploymentId = deploymentId,
                    DeviceId = deviceId,
                    BatchNumber = batchNumber,
                    Status = DeploymentProgressStatus.Pending,
                    PreviousVersion = device?.ClientAppVersion
                };

                dbContext.TblUpdateDeploymentProgress.Add(progress);

                deviceIndex++;
                if (deviceIndex % batchSize == 0)
                {
                    batchNumber++;
                }
            }

            // Update deployment status
            deployment.BatchStatus = BatchDeploymentStatus.InProgress;
            deployment.CurrentBatchNumber = 1;
            deployment.TotalBatches = totalBatches;
            deployment.BatchStartedAt = DateTime.UtcNow;
            deployment.ModifiedBy = startedBy;
            deployment.ModifiedDate = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();

            _logger.LogInformation("Started batch deployment {DeploymentId} with {TotalDevices} devices in {TotalBatches} batches",
                deploymentId, totalDevices, totalBatches);

            // Trigger the first batch
            await TriggerBatchAsync(dbContext, deploymentId, 1);
            await dbContext.SaveChangesAsync();

            return new BatchDeploymentResult
            {
                Success = true,
                DeploymentId = deploymentId,
                TotalDevices = totalDevices,
                TotalBatches = totalBatches,
                BatchSize = batchSize
            };
        }

        public async Task<bool> PauseBatchDeploymentAsync(int deploymentId)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                $"batch-deployment-{deploymentId}",
                TimeSpan.FromMinutes(2));

            if (distLock == null)
            {
                _logger.LogWarning("Could not acquire lock to pause deployment {DeploymentId}", deploymentId);
                return false;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var deployment = await dbContext.TblUpdateDeployments.FindAsync(deploymentId);
            if (deployment == null || deployment.BatchStatus != BatchDeploymentStatus.InProgress)
            {
                return false;
            }

            deployment.BatchStatus = BatchDeploymentStatus.Paused;
            deployment.ModifiedDate = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Paused batch deployment {DeploymentId}", deploymentId);

            return true;
        }

        public async Task<bool> ResumeBatchDeploymentAsync(int deploymentId)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                $"batch-deployment-{deploymentId}",
                TimeSpan.FromMinutes(5));

            if (distLock == null)
            {
                _logger.LogWarning("Could not acquire lock to resume deployment {DeploymentId}", deploymentId);
                return false;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var deployment = await dbContext.TblUpdateDeployments.FindAsync(deploymentId);
            if (deployment == null || deployment.BatchStatus != BatchDeploymentStatus.Paused)
            {
                return false;
            }

            deployment.BatchStatus = BatchDeploymentStatus.InProgress;
            deployment.ModifiedDate = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();

            // Continue processing the current batch (lock still held)
            await ProcessNextBatchInternalAsync(dbContext, deploymentId);

            _logger.LogInformation("Resumed batch deployment {DeploymentId}", deploymentId);
            return true;
        }

        public async Task<bool> StopBatchDeploymentAsync(int deploymentId)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                $"batch-deployment-{deploymentId}",
                TimeSpan.FromMinutes(2));

            if (distLock == null)
            {
                _logger.LogWarning("Could not acquire lock to stop deployment {DeploymentId}", deploymentId);
                return false;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var deployment = await dbContext.TblUpdateDeployments.FindAsync(deploymentId);
            if (deployment == null)
            {
                return false;
            }

            deployment.BatchStatus = BatchDeploymentStatus.Failed;
            deployment.ModifiedDate = DateTime.UtcNow;

            // Mark any pending/triggered devices as failed
            var pendingProgress = await dbContext.TblUpdateDeploymentProgress
                .Where(p => p.DeploymentId == deploymentId &&
                           (p.Status == DeploymentProgressStatus.Pending || p.Status == DeploymentProgressStatus.Triggered))
                .ToListAsync();

            foreach (var progress in pendingProgress)
            {
                progress.Status = DeploymentProgressStatus.Failed;
                progress.ErrorMessage = "Deployment stopped by user";
                progress.CompletedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Stopped batch deployment {DeploymentId}", deploymentId);

            return true;
        }

        #endregion

        #region Progress Monitoring

        public async Task<BatchDeploymentProgress?> GetDeploymentProgressAsync(int deploymentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var deployment = await dbContext.TblUpdateDeployments
                .Include(d => d.AppVersion)
                    .ThenInclude(v => v.Platform)
                .FirstOrDefaultAsync(d => d.DeploymentId == deploymentId);

            if (deployment == null)
            {
                return null;
            }

            var progressRecords = await dbContext.TblUpdateDeploymentProgress
                .Include(p => p.Device)
                    .ThenInclude(d => d.Location)
                .Where(p => p.DeploymentId == deploymentId)
                .ToListAsync();

            return BuildProgressFromRecords(deployment, progressRecords);
        }

        public async Task<List<BatchDeploymentProgress>> GetActiveDeploymentsProgressAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var activeDeployments = await dbContext.TblUpdateDeployments
                .Include(d => d.AppVersion)
                    .ThenInclude(v => v.Platform)
                .Where(d => d.EnableBatching &&
                           (d.BatchStatus == BatchDeploymentStatus.InProgress ||
                            d.BatchStatus == BatchDeploymentStatus.Paused))
                .ToListAsync();

            var result = new List<BatchDeploymentProgress>();

            foreach (var deployment in activeDeployments)
            {
                var progressRecords = await dbContext.TblUpdateDeploymentProgress
                    .Include(p => p.Device)
                        .ThenInclude(d => d.Location)
                    .Where(p => p.DeploymentId == deployment.DeploymentId)
                    .ToListAsync();

                var progress = BuildProgressFromRecords(deployment, progressRecords);
                if (progress != null)
                {
                    result.Add(progress);
                }
            }

            return result;
        }

        private BatchDeploymentProgress? BuildProgressFromRecords(TblUpdateDeployment deployment, List<TblUpdateDeploymentProgress> records)
        {
            if (!records.Any())
            {
                return new BatchDeploymentProgress
                {
                    DeploymentId = deployment.DeploymentId,
                    DeploymentName = deployment.DeploymentName,
                    TargetVersion = deployment.AppVersion?.VersionNumber,
                    PlatformName = deployment.AppVersion?.Platform?.PlatformName,
                    BatchStatus = deployment.BatchStatus ?? BatchDeploymentStatus.Pending,
                    CurrentBatchNumber = deployment.CurrentBatchNumber,
                    TotalBatches = deployment.TotalBatches,
                    TotalDevices = 0,
                    StartedAt = deployment.BatchStartedAt
                };
            }

            int completed = records.Count(r => r.Status == DeploymentProgressStatus.Completed);
            int failed = records.Count(r => r.Status == DeploymentProgressStatus.Failed);
            int pending = records.Count(r => r.Status == DeploymentProgressStatus.Pending);
            int inProgress = records.Count(r => r.Status == DeploymentProgressStatus.Triggered || r.Status == DeploymentProgressStatus.Updating);
            int total = records.Count;

            var failedDevices = records
                .Where(r => r.Status == DeploymentProgressStatus.Failed)
                .Select(r => new FailedDeviceInfo
                {
                    DeviceId = r.DeviceId,
                    DeviceName = r.Device?.Description,
                    LocationName = r.Device?.Location?.LocationName,
                    ErrorMessage = r.ErrorMessage,
                    BatchNumber = r.BatchNumber
                })
                .ToList();

            // Check if we need user action (some succeeded, some failed in current batch)
            var currentBatchRecords = records.Where(r => r.BatchNumber == deployment.CurrentBatchNumber).ToList();
            bool hasPartialFailure = currentBatchRecords.Any(r => r.Status == DeploymentProgressStatus.Completed) &&
                                     currentBatchRecords.Any(r => r.Status == DeploymentProgressStatus.Failed);

            return new BatchDeploymentProgress
            {
                DeploymentId = deployment.DeploymentId,
                DeploymentName = deployment.DeploymentName,
                TargetVersion = deployment.AppVersion?.VersionNumber,
                PlatformName = deployment.AppVersion?.Platform?.PlatformName,
                BatchStatus = deployment.BatchStatus ?? BatchDeploymentStatus.Pending,
                CurrentBatchNumber = deployment.CurrentBatchNumber,
                TotalBatches = deployment.TotalBatches,
                TotalDevices = total,
                CompletedCount = completed,
                FailedCount = failed,
                PendingCount = pending,
                InProgressCount = inProgress,
                ProgressPercent = total > 0 ? Math.Round((double)(completed + failed) / total * 100, 1) : 0,
                FailedDevices = failedDevices,
                StartedAt = deployment.BatchStartedAt,
                LastUpdated = records.Max(r => r.CompletedAt ?? r.TriggeredAt),
                RequiresUserAction = hasPartialFailure && deployment.BatchStatus == BatchDeploymentStatus.Paused
            };
        }

        #endregion

        #region Device Status Updates

        /// <summary>
        /// A device reports the version it is now running.
        ///
        /// For an app deployment that completes the device. For a bootloader deployment it does
        /// not: the artifact is installed but the player has not yet proved it can boot on it, and
        /// a bootloader that installs and then fails to boot is precisely the case batching exists
        /// to contain. Such a row waits in UPDATING until the player reports healthy.
        /// </summary>
        public async Task HandleDeviceVersionUpdateAsync(string deviceId, string newVersion)
        {
            await ForEachActiveProgressAsync(deviceId, async (dbContext, progress) =>
            {
                var target = progress.Deployment?.AppVersion;
                var kind = target?.ArtifactKind;

                if (BatchProgressRules.ParksOnVersionReport(kind, target?.VersionNumber, newVersion))
                {
                    progress.NewVersion = newVersion;
                    progress.Status = DeploymentProgressStatus.Updating;

                    _logger.LogInformation(
                        "Device {DeviceId} installed bootloader {Version} for deployment {DeploymentId}; waiting for it to report healthy",
                        deviceId, newVersion, progress.DeploymentId);

                    // Deliberately no completion check: the batch must not advance on an install.
                    return false;
                }

                if (!BatchProgressRules.CompletesOnVersionReport(kind, target?.VersionNumber, newVersion))
                    return false;

                progress.NewVersion = newVersion;
                progress.Status = DeploymentProgressStatus.Completed;
                progress.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation("Device {DeviceId} completed update to version {Version} for deployment {DeploymentId}",
                    deviceId, newVersion, progress.DeploymentId);

                return true;
            });
        }

        /// <summary>
        /// The player has reached HEALTHY, which is what completes a bootloader deployment.
        /// App deployments are untouched: they completed when the version was reported.
        /// </summary>
        public async Task HandleDeviceHealthyAsync(string deviceId)
        {
            await ForEachActiveProgressAsync(deviceId, (dbContext, progress) =>
            {
                var target = progress.Deployment?.AppVersion;

                // The device row is the ground truth, because a player can come back healthy
                // without having posted UPDATE_COMPLETE first.
                var runningVersion = progress.Device?.ClientBootloaderVersion ?? progress.NewVersion;

                if (!BatchProgressRules.CompletesOnHealthReport(target?.ArtifactKind, target?.VersionNumber, runningVersion))
                    return Task.FromResult(false);

                progress.NewVersion = runningVersion;

                progress.Status = DeploymentProgressStatus.Completed;
                progress.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "Device {DeviceId} reported healthy on bootloader {Version} for deployment {DeploymentId}",
                    deviceId, runningVersion, progress.DeploymentId);

                return Task.FromResult(true);
            });
        }

        public async Task HandleDeviceUpdateFailedAsync(string deviceId, string errorMessage)
        {
            await ForEachActiveProgressAsync(deviceId, (dbContext, progress) =>
            {
                progress.Status = DeploymentProgressStatus.Failed;
                progress.ErrorMessage = errorMessage;
                progress.CompletedAt = DateTime.UtcNow;

                _logger.LogWarning("Device {DeviceId} failed update for deployment {DeploymentId}: {Error}",
                    deviceId, progress.DeploymentId, errorMessage);

                return Task.FromResult(true);
            });
        }

        /// <summary>
        /// A rollback counts as a failure, with the version the player fell back to recorded.
        ///
        /// The player healed itself, but that is also the strongest available evidence that the
        /// bundle is bad. The whole point of batching is to stop before batch two, and a batch one
        /// that rolled itself back is exactly when to stop, so this feeds the existing
        /// partial-failure pause rather than being counted as a quiet success.
        /// </summary>
        public async Task HandleDeviceRolledBackAsync(string deviceId, string? fellBackToVersion)
        {
            var detail = string.IsNullOrWhiteSpace(fellBackToVersion)
                ? "Device rolled back after a failed update."
                : $"Device rolled back to version {fellBackToVersion} after a failed update.";

            await HandleDeviceUpdateFailedAsync(deviceId, detail);
        }

        /// <summary>
        /// Applies <paramref name="apply"/> to each of a device's in-flight progress rows under
        /// that deployment's lock, and re-checks batch completion for the rows it changed.
        /// </summary>
        private async Task ForEachActiveProgressAsync(
            string deviceId,
            Func<IDirectorDBContext, TblUpdateDeploymentProgress, Task<bool>> apply)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var progressRecords = await dbContext.TblUpdateDeploymentProgress
                .Include(p => p.Deployment)
                    .ThenInclude(d => d.AppVersion)
                .Include(p => p.Device)
                .Where(p => p.DeviceId == deviceId &&
                           (p.Status == DeploymentProgressStatus.Triggered || p.Status == DeploymentProgressStatus.Updating))
                .ToListAsync();

            foreach (var progress in progressRecords)
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    $"batch-deployment-{progress.DeploymentId}",
                    TimeSpan.FromMinutes(2));

                if (distLock == null)
                {
                    _logger.LogWarning("Could not acquire lock for deployment {DeploymentId} while updating device {DeviceId}",
                        progress.DeploymentId, deviceId);
                    continue;
                }

                var settled = await apply(dbContext, progress);

                if (settled)
                    await CheckBatchCompletionInternalAsync(dbContext, progress.DeploymentId);

                await dbContext.SaveChangesAsync();
            }
        }

        #endregion

        #region Batch Processing

        public async Task ProcessNextBatchAsync(int deploymentId)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                $"batch-deployment-{deploymentId}",
                TimeSpan.FromMinutes(5));

            if (distLock == null)
            {
                _logger.LogWarning("Could not acquire lock to process next batch for deployment {DeploymentId}", deploymentId);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            await ProcessNextBatchInternalAsync(dbContext, deploymentId);
        }

        private async Task ProcessNextBatchInternalAsync(IDirectorDBContext dbContext, int deploymentId)
        {
            var deployment = await dbContext.TblUpdateDeployments.FindAsync(deploymentId);
            if (deployment == null || deployment.BatchStatus != BatchDeploymentStatus.InProgress)
            {
                return;
            }

            int nextBatch = deployment.CurrentBatchNumber;

            // Check if we have pending devices in the next batch
            var hasPendingInBatch = await dbContext.TblUpdateDeploymentProgress
                .AnyAsync(p => p.DeploymentId == deploymentId &&
                              p.BatchNumber == nextBatch &&
                              p.Status == DeploymentProgressStatus.Pending);

            if (!hasPendingInBatch)
            {
                // Move to next batch
                nextBatch++;
                if (nextBatch > deployment.TotalBatches)
                {
                    // All batches complete
                    deployment.BatchStatus = BatchDeploymentStatus.Completed;
                    deployment.LastBatchCompletedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Batch deployment {DeploymentId} completed all batches", deploymentId);
                    return;
                }

                deployment.CurrentBatchNumber = nextBatch;
            }

            await TriggerBatchAsync(dbContext, deploymentId, nextBatch);
            await dbContext.SaveChangesAsync();
        }

        public async Task CheckBatchCompletionAsync(int deploymentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            await CheckBatchCompletionInternalAsync(dbContext, deploymentId);
            await dbContext.SaveChangesAsync();
        }

        private async Task CheckBatchCompletionInternalAsync(IDirectorDBContext dbContext, int deploymentId)
        {
            var deployment = await dbContext.TblUpdateDeployments.FindAsync(deploymentId);
            if (deployment == null || deployment.BatchStatus != BatchDeploymentStatus.InProgress)
            {
                return;
            }

            var currentBatchRecords = await dbContext.TblUpdateDeploymentProgress
                .Where(p => p.DeploymentId == deploymentId && p.BatchNumber == deployment.CurrentBatchNumber)
                .ToListAsync();

            if (!currentBatchRecords.Any())
            {
                return;
            }

            int completed = currentBatchRecords.Count(r => r.Status == DeploymentProgressStatus.Completed);
            int failed = currentBatchRecords.Count(r => r.Status == DeploymentProgressStatus.Failed);
            int pending = currentBatchRecords.Count(r => r.Status == DeploymentProgressStatus.Pending ||
                                                         r.Status == DeploymentProgressStatus.Triggered ||
                                                         r.Status == DeploymentProgressStatus.Updating);

            if (pending > 0)
            {
                // Batch not yet complete
                return;
            }

            // All devices in batch have finished (completed or failed)
            deployment.LastBatchCompletedAt = DateTime.UtcNow;

            if (failed == currentBatchRecords.Count)
            {
                // All devices failed - stop deployment
                deployment.BatchStatus = BatchDeploymentStatus.Failed;
                _logger.LogError("Batch deployment {DeploymentId} batch {BatchNumber} - all devices failed, stopping deployment",
                    deploymentId, deployment.CurrentBatchNumber);
                return;
            }

            if (failed > 0 && completed > 0)
            {
                // Partial failure - pause for user action
                deployment.BatchStatus = BatchDeploymentStatus.Paused;
                _logger.LogWarning("Batch deployment {DeploymentId} batch {BatchNumber} - partial failure ({Completed} succeeded, {Failed} failed), pausing for user action",
                    deploymentId, deployment.CurrentBatchNumber, completed, failed);
                return;
            }

            // All succeeded - proceed to next batch
            _logger.LogInformation("Batch deployment {DeploymentId} batch {BatchNumber} completed successfully",
                deploymentId, deployment.CurrentBatchNumber);

            if (deployment.CurrentBatchNumber >= deployment.TotalBatches)
            {
                deployment.BatchStatus = BatchDeploymentStatus.Completed;
                _logger.LogInformation("Batch deployment {DeploymentId} completed all batches successfully", deploymentId);
            }
            else
            {
                deployment.CurrentBatchNumber++;
                await TriggerBatchAsync(dbContext, deploymentId, deployment.CurrentBatchNumber);
            }
        }

        private async Task TriggerBatchAsync(IDirectorDBContext dbContext, int deploymentId, int batchNumber)
        {
            var batchDevices = await dbContext.TblUpdateDeploymentProgress
                .Where(p => p.DeploymentId == deploymentId &&
                           p.BatchNumber == batchNumber &&
                           p.Status == DeploymentProgressStatus.Pending)
                .ToListAsync();

            foreach (var progress in batchDevices)
            {
                progress.Status = DeploymentProgressStatus.Triggered;
                progress.TriggeredAt = DateTime.UtcNow;
            }

            _logger.LogInformation("Triggered batch {BatchNumber} for deployment {DeploymentId} with {DeviceCount} devices",
                batchNumber, deploymentId, batchDevices.Count);
        }

        #endregion

        #region Utility Methods

        public async Task<List<string>> GetTargetDeviceIdsForDeploymentAsync(int deploymentId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var deployment = await dbContext.TblUpdateDeployments
                .Include(d => d.AppVersion)
                .Include(d => d.DeploymentDevices)
                .FirstOrDefaultAsync(d => d.DeploymentId == deploymentId);

            if (deployment == null)
            {
                return new List<string>();
            }

            return await GetTargetDeviceIdsForDeploymentInternalAsync(dbContext, deployment);
        }

        private async Task<List<string>> GetTargetDeviceIdsForDeploymentInternalAsync(IDirectorDBContext dbContext, TblUpdateDeployment deployment)
        {
            var platformId = deployment.AppVersion?.PlatformId;
            if (platformId == null)
            {
                return new List<string>();
            }

            IQueryable<TblDevice> query = dbContext.TblDevices.Where(d => d.PlatformId == platformId);

            switch (deployment.DeploymentScope)
            {
                case DeploymentScope.All:
                    // All devices on platform
                    break;

                case DeploymentScope.Location:
                    if (deployment.LocationId.HasValue)
                    {
                        query = query.Where(d => d.LocationId == deployment.LocationId.Value);
                    }
                    break;

                case DeploymentScope.Device:
                    var targetDeviceIds = deployment.DeploymentDevices.Select(dd => dd.DeviceId).ToList();
                    query = query.Where(d => targetDeviceIds.Contains(d.DeviceId));
                    break;
            }

            return await query.Select(d => d.DeviceId).ToListAsync();
        }

        public async Task<bool> ShouldTriggerUpdateForDeviceAsync(string deviceId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Check if device has a triggered status in any active batch deployment
            return await dbContext.TblUpdateDeploymentProgress
                .Include(p => p.Deployment)
                .AnyAsync(p => p.DeviceId == deviceId &&
                              p.Status == DeploymentProgressStatus.Triggered &&
                              p.Deployment.BatchStatus == BatchDeploymentStatus.InProgress);
        }

        public async Task MarkDeviceUpdateTriggeredAsync(string deviceId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Find any triggered progress records for this device in active batch deployments
            var progressRecords = await dbContext.TblUpdateDeploymentProgress
                .Include(p => p.Deployment)
                .Where(p => p.DeviceId == deviceId &&
                           p.Status == DeploymentProgressStatus.Triggered &&
                           p.Deployment.BatchStatus == BatchDeploymentStatus.InProgress)
                .ToListAsync();

            foreach (var progress in progressRecords)
            {
                progress.Status = DeploymentProgressStatus.Updating;
            }

            if (progressRecords.Any())
            {
                await dbContext.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Deploy IPK file to SCAP path for WebOS updates.
        /// Copies the version IPK to procentric/scap/application/scap_app.ipk
        /// </summary>
        private async Task<(bool Success, string? ErrorMessage)> DeploySCAPFileAsync(TblAppVersion? appVersion)
        {
            if (appVersion == null)
            {
                return (false, "App version not found");
            }

            try
            {
                var baseRoot = _env.WebRootPath;
                if (string.IsNullOrEmpty(baseRoot))
                {
                    return (false, "BaseRoot path not configured");
                }

                // Source: PlayerContent/WebOS/{version}/DirectorClient_WebOS_v{version}.ipk
                var sourceFilePath = appVersion.ZipFilePath;
                if (string.IsNullOrEmpty(sourceFilePath))
                {
                    return (false, "Version file path not set");
                }

                // Convert relative path to absolute
                var absoluteSourcePath = Path.Combine(baseRoot, sourceFilePath.Replace("/", Path.DirectorySeparatorChar.ToString()));

                if (!File.Exists(absoluteSourcePath))
                {
                    return (false, $"Source IPK file not found: {absoluteSourcePath}");
                }

                // Destination: procentric/scap/application/scap_app.ipk
                var scapDirectory = Path.Combine(baseRoot, "procentric", "scap", "application");
                var scapFilePath = Path.Combine(scapDirectory, "scap_app.ipk");

                // Ensure SCAP directory exists
                Directory.CreateDirectory(scapDirectory);

                // Copy file to SCAP path (overwrite if exists)
                File.Copy(absoluteSourcePath, scapFilePath, overwrite: true);

                _logger.LogInformation("Deployed WebOS IPK to SCAP path: {SourcePath} -> {DestPath}",
                    absoluteSourcePath, scapFilePath);

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deploy IPK to SCAP path");
                return (false, $"Failed to deploy IPK to SCAP path: {ex.Message}");
            }
        }

        #endregion
    }
}
