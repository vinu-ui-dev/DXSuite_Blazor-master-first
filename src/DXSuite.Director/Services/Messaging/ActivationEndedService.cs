using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Runs <see cref="ActivationEndedLog"/> for every worker tenant. Every instance polls, and each tenant's pass runs
/// under that tenant's SQL lease (HOST-04): with two instances one logs a tenant's ended Activations at a time, and
/// the claim each Activation carries stops the other from logging it again.
/// </summary>
public sealed class ActivationEndedService : BackgroundService
{
    public static string LeaseName(string tenantId) => $"activation-ended:{tenantId}";

    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly TimeProvider _clock;
    private readonly ILogger<ActivationEndedService> _logger;
    private readonly TimeSpan _startupDelay;
    private readonly TimeSpan _pollInterval;

    public ActivationEndedService(
        IWorkerScopeFactory workerScopeFactory,
        IDistributedLockService lockService,
        TimeProvider clock,
        ILogger<ActivationEndedService> logger)
        : this(workerScopeFactory, lockService, clock, logger, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1))
    {
    }

    internal ActivationEndedService(
        IWorkerScopeFactory workerScopeFactory,
        IDistributedLockService lockService,
        TimeProvider clock,
        ILogger<ActivationEndedService> logger,
        TimeSpan startupDelay,
        TimeSpan pollInterval)
    {
        _workerScopeFactory = workerScopeFactory;
        _lockService = lockService;
        _clock = clock;
        _logger = logger;
        _startupDelay = startupDelay;
        _pollInterval = pollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(_startupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in the activation.ended cycle");
            }

            try { await Task.Delay(_pollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>One pass over every worker tenant; a tenant that fails is logged and the rest still run.</summary>
    internal async Task RunCycleAsync(CancellationToken ct)
    {
        foreach (var tenant in _workerScopeFactory.WorkerTenants)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenant, ct);

                // Leased per tenant: worker pods can serve different tenant sets.
                using var lease = await _lockService.TryAcquireLockAsync(
                    LeaseName(TenantLeaseKey.Of(scope.ServiceProvider.GetService<ITenantContext>(), tenant)), TimeSpan.FromMinutes(5), ct);
                if (lease == null) continue;
                var logged = await scope.ServiceProvider.GetRequiredService<ActivationEndedLog>()
                    .LogEndedAsync(_clock.GetUtcNow().UtcDateTime, ct);
                if (logged > 0)
                    _logger.LogInformation("Logged the end of {Count} Activation(s) for tenant {Tenant}", logged, tenant);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "activation.ended failed for tenant {Tenant}", tenant);
            }
        }
    }
}
