using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// Background service that periodically checks for offline devices and attempts auto-healing
/// </summary>
public class AutoHealBackgroundService : BackgroundService
{
    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly ILogger<AutoHealBackgroundService> _logger;
    private readonly IDistributedLockService _lockService;

    // Check every 30 seconds
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public AutoHealBackgroundService(
        IWorkerScopeFactory workerScopeFactory,
        ILogger<AutoHealBackgroundService> logger,
        IDistributedLockService lockService)
    {
        _workerScopeFactory = workerScopeFactory;
        _logger = logger;
        _lockService = lockService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoHealBackgroundService started");

        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "auto-heal-background-service-lock",
                    TimeSpan.FromMinutes(5),
                    stoppingToken);

                if (distLock != null)
                {
                    foreach (var tenantSubdomain in _workerScopeFactory.WorkerTenants)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        try
                        {
                            using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenantSubdomain, stoppingToken);
                            var autoHealService = scope.ServiceProvider.GetRequiredService<IAutoHealService>();

                            await autoHealService.CheckAndHealDevicesAsync(stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in auto-heal check for tenant {Tenant}", tenantSubdomain);
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("Another instance is handling auto-heal, skipping this cycle");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AutoHealBackgroundService check cycle");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
        }

        _logger.LogInformation("AutoHealBackgroundService stopped");
    }
}
