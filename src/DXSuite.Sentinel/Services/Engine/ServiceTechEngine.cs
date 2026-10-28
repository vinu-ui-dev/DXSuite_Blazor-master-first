using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Services;
using DXSuite.Sentinel.Services.Drivers;
using DXSuite.Sentinel.Services.Routing;

namespace DXSuite.Sentinel.Services.Engine;

/// <summary>
/// The Service Tech Engine continuously evaluates device health and executes tiered remediation.
/// Runs as a BackgroundService matching the existing AutoHealBackgroundService pattern.
///
/// Evaluation cycle (configurable, default 60s):
/// 1. ASSESS - Pull current device states
/// 2. CLASSIFY - Match against known issue patterns
/// 3. ACT - Execute remediation based on tier (1=Rule, 2=AI, 3=Advisory)
/// 4. LOG - Record decisions and actions in audit trail
/// 5. NOTIFY - Send results to Reporter Engine
/// </summary>
public class ServiceTechEngine : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ServiceTechEngine> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly ISentinelAvailabilityService _sentinelAvailability;

    private static readonly TimeSpan DefaultCycleInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);

    public ServiceTechEngine(
        IServiceScopeFactory scopeFactory,
        ILogger<ServiceTechEngine> logger,
        IDistributedLockService lockService,
        ISentinelAvailabilityService sentinelAvailability)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _lockService = lockService;
        _sentinelAvailability = sentinelAvailability;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ServiceTechEngine starting");
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_sentinelAvailability.IsAvailable)
                {
                    await Task.Delay(DefaultCycleInterval, stoppingToken);
                    continue;
                }
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "sentinel-service-tech-engine-lock",
                    TimeSpan.FromMinutes(5),
                    stoppingToken);

                if (distLock != null)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var evaluator = scope.ServiceProvider.GetRequiredService<IDeviceEvaluator>();
                    await evaluator.RunEvaluationCycleAsync(stoppingToken);
                }
                else
                {
                    _logger.LogDebug("Another instance is running the Service Tech evaluation cycle, skipping");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ServiceTechEngine evaluation cycle");
            }

            try
            {
                await Task.Delay(DefaultCycleInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("ServiceTechEngine stopped");
    }
}
