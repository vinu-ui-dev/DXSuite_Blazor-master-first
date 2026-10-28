using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Services;

namespace DXSuite.Sentinel.Services.Reporter;

/// <summary>
/// The Reporter Engine decides what to communicate externally (tickets, reports, or nothing)
/// based on configurable intelligence. Evaluates events, detects mass outages, generates tickets,
/// and tracks ticket lifecycle.
/// </summary>
public class ReporterEngine : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReporterEngine> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly ISentinelAvailabilityService _sentinelAvailability;

    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(20);

    public ReporterEngine(
        IServiceScopeFactory scopeFactory,
        ILogger<ReporterEngine> logger,
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
        _logger.LogInformation("ReporterEngine starting");
        try { await Task.Delay(InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_sentinelAvailability.IsAvailable)
                {
                    await Task.Delay(CycleInterval, stoppingToken);
                    continue;
                }
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "sentinel-reporter-engine-lock",
                    TimeSpan.FromMinutes(5),
                    stoppingToken);

                if (distLock != null)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IReportProcessor>();
                    await processor.ProcessPendingEventsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReporterEngine cycle");
            }

            try
            {
                await Task.Delay(CycleInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("ReporterEngine stopped");
    }
}
