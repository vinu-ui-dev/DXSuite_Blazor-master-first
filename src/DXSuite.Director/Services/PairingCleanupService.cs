using Microsoft.Extensions.Options;
using DXSuite.Shared.Models;
using DXSuite.Director.Services.DistributedLock;

namespace DXSuite.Director.Services;

/// <summary>
/// Background service that expires stale pairing requests and purges
/// old resolved requests. Runs every 5 minutes.
/// </summary>
public class PairingCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PairingCleanupService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly bool _engageEnabled;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ResolvedRetention = TimeSpan.FromDays(30);

    public PairingCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<PairingCleanupService> logger,
        IDistributedLockService lockService,
        IOptions<FeatureFlags> featureFlags)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _lockService = lockService;
        _engageEnabled = featureFlags.Value.Engage;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PairingCleanupService started");

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_engageEnabled)
            {
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "pairing-cleanup-lock",
                    TimeSpan.FromMinutes(10),
                    stoppingToken);

                if (distLock != null)
                {
                    await RunCleanupCycleAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PairingCleanupService cleanup cycle");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PairingCleanupService stopped");
    }

    private async Task RunCleanupCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var pairingService = scope.ServiceProvider.GetRequiredService<IDevicePairingService>();

        var expired = await pairingService.ExpireStaleRequestsAsync();
        if (expired > 0)
            _logger.LogInformation("PairingCleanup: expired {Count} stale requests", expired);

        var purged = await pairingService.PurgeOldRequestsAsync(ResolvedRetention);
        if (purged > 0)
            _logger.LogInformation("PairingCleanup: purged {Count} old resolved requests", purged);
    }
}
