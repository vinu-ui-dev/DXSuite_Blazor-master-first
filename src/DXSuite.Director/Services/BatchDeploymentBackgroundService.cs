using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services;

/// <summary>
/// Background service that monitors batch deployments for:
/// - Batch completion (all devices in current batch completed/failed)
/// - Timeouts (devices that haven't responded within timeout period)
/// - Auto-progression to next batch when current batch is complete
/// </summary>
public class BatchDeploymentBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BatchDeploymentBackgroundService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _defaultBatchTimeout = TimeSpan.FromMinutes(5);

    public BatchDeploymentBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<BatchDeploymentBackgroundService> logger,
        IDistributedLockService lockService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _lockService = lockService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BatchDeploymentBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "batch-deployment-background-service-lock",
                    TimeSpan.FromMinutes(5),
                    stoppingToken);

                if (distLock != null)
                {
                    await CheckActiveDeploymentsAsync(stoppingToken);
                }
                else
                {
                    _logger.LogDebug("Another instance is handling batch deployment monitoring, skipping this cycle");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking active batch deployments");
            }

            try { await Task.Delay(_checkInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("BatchDeploymentBackgroundService stopped");
    }

    private async Task CheckActiveDeploymentsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var batchService = scope.ServiceProvider.GetRequiredService<IBatchDeploymentService>();

        // Get all active batch deployments (IN_PROGRESS status)
        var activeDeployments = await ((DirectorDbContext)dbContext).TblUpdateDeployments
            .Where(d => d.EnableBatching && d.BatchStatus == BatchDeploymentStatus.InProgress && d.IsActive)
            .ToListAsync(stoppingToken);

        foreach (var deployment in activeDeployments)
        {
            try
            {
                await ProcessDeploymentAsync(deployment, dbContext, batchService, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deployment {DeploymentId}", deployment.DeploymentId);
            }
        }
    }

    private async Task ProcessDeploymentAsync(
        TblUpdateDeployment deployment,
        IDirectorDBContext dbContext,
        IBatchDeploymentService batchService,
        CancellationToken stoppingToken)
    {
        var currentBatch = deployment.CurrentBatchNumber;

        // Get all progress records for current batch
        var batchProgress = await ((DirectorDbContext)dbContext).TblUpdateDeploymentProgress
            .Where(p => p.DeploymentId == deployment.DeploymentId && p.BatchNumber == currentBatch)
            .ToListAsync(stoppingToken);

        if (!batchProgress.Any())
        {
            _logger.LogWarning("No progress records found for deployment {DeploymentId} batch {BatchNumber}",
                deployment.DeploymentId, currentBatch);
            return;
        }

        // Check for timeouts - devices that were triggered but haven't completed
        var timedOutDevices = batchProgress
            .Where(p => p.Status == DeploymentProgressStatus.Triggered &&
                        p.TriggeredAt.HasValue &&
                        DateTime.UtcNow - p.TriggeredAt.Value > _defaultBatchTimeout)
            .ToList();

        foreach (var timedOut in timedOutDevices)
        {
            _logger.LogWarning("Device {DeviceId} timed out in deployment {DeploymentId}",
                timedOut.DeviceId, deployment.DeploymentId);

            timedOut.Status = DeploymentProgressStatus.Failed;
            timedOut.CompletedAt = DateTime.UtcNow;
            timedOut.ErrorMessage = "Update timed out - device did not respond within 5 minutes";
        }

        if (timedOutDevices.Any())
        {
            await dbContext.SaveChangesAsync(stoppingToken);
        }

        // Check if current batch is complete (all devices either COMPLETED or FAILED)
        var allCompleted = batchProgress.All(p =>
            p.Status == DeploymentProgressStatus.Completed ||
            p.Status == DeploymentProgressStatus.Failed);

        if (allCompleted)
        {
            var completedCount = batchProgress.Count(p => p.Status == DeploymentProgressStatus.Completed);
            var failedCount = batchProgress.Count(p => p.Status == DeploymentProgressStatus.Failed);

            _logger.LogInformation(
                "Batch {BatchNumber} complete for deployment {DeploymentId}: {Completed} completed, {Failed} failed",
                currentBatch, deployment.DeploymentId, completedCount, failedCount);

            // Update deployment's last batch completed time
            deployment.LastBatchCompletedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(stoppingToken);

            // Check if there are any failures
            if (failedCount > 0)
            {
                // Pause deployment for user action
                _logger.LogInformation("Pausing deployment {DeploymentId} due to {FailedCount} failed devices",
                    deployment.DeploymentId, failedCount);

                deployment.BatchStatus = BatchDeploymentStatus.Paused;
                await dbContext.SaveChangesAsync(stoppingToken);
            }
            else if (currentBatch < deployment.TotalBatches)
            {
                // All devices succeeded, auto-progress to next batch
                _logger.LogInformation("Auto-progressing deployment {DeploymentId} to batch {NextBatch}",
                    deployment.DeploymentId, currentBatch + 1);

                await batchService.ProcessNextBatchAsync(deployment.DeploymentId);
            }
            else
            {
                // All batches complete
                _logger.LogInformation("Deployment {DeploymentId} completed successfully",
                    deployment.DeploymentId);

                deployment.BatchStatus = BatchDeploymentStatus.Completed;
                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
