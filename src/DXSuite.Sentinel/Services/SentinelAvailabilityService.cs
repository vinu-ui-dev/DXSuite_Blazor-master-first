using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;
using DXSuite.Shared.Services;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Singleton service that centralizes Sentinel availability checks.
/// Config-driven (Sentinel:Enabled) with periodic DB probes so all consumers
/// share one cached result instead of each page probing independently.
/// </summary>
public class SentinelAvailabilityService : ISentinelAvailabilityService, IHostedService, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SentinelAvailabilityService> _logger;
    private readonly bool _configEnabled;

    private volatile bool _isAvailable;
    private Timer? _probeTimer;

    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);

    public bool IsAvailable => _isAvailable;

    public SentinelAvailabilityService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<SentinelAvailabilityService> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configEnabled = configuration.GetValue<bool>("Sentinel:Enabled", false);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configEnabled)
        {
            _logger.LogInformation("Sentinel is disabled in configuration");
            return Task.CompletedTask;
        }

        // Fire initial probe after short delay, then repeat every 60s
        _probeTimer = new Timer(async _ => await ProbeAsync(), null, InitialDelay, ProbeInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _probeTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public async Task<bool> CheckAvailabilityAsync()
    {
        await ProbeAsync();
        return _isAvailable;
    }

    private async Task ProbeAsync()
    {
        if (!_configEnabled)
        {
            _isAvailable = false;
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();
            await sentinelDb.DeviceInstances.AsNoTracking().Take(1).ToListAsync();

            if (!_isAvailable)
                _logger.LogInformation("Sentinel DB is now available");

            _isAvailable = true;
        }
        catch (Exception ex)
        {
            if (_isAvailable)
                _logger.LogWarning("Sentinel DB is no longer reachable: {Message}", ex.Message);

            _isAvailable = false;
        }
    }

    public void Dispose()
    {
        _probeTimer?.Dispose();
    }
}
