using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// Sends the approval reminder digests (FSD 1.1 section 12 point 7) for every worker tenant. Every instance polls, and each tenant's
/// pass runs under that tenant's SQL lease (HOST-04): with two instances, one sends a tenant's digests at a time, and
/// the reminder times it records in <c>tblApprovalReminder</c> stop the other from sending them again.
/// </summary>
public sealed class ApprovalReminderService : BackgroundService
{
    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly IDistributedLockService _lockService;
    private readonly TimeProvider _clock;
    private readonly ILogger<ApprovalReminderService> _logger;
    private readonly TimeSpan _startupDelay;
    private readonly TimeSpan _pollInterval;

    public ApprovalReminderService(
        IWorkerScopeFactory workerScopeFactory,
        IDistributedLockService lockService,
        ILogger<ApprovalReminderService> logger)
        : this(workerScopeFactory, lockService, TimeProvider.System, logger, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5))
    {
    }

    // The interval is at least an hour, so a 5-minute poll sends each digest within minutes of being due.
    internal ApprovalReminderService(
        IWorkerScopeFactory workerScopeFactory,
        IDistributedLockService lockService,
        TimeProvider clock,
        ILogger<ApprovalReminderService> logger,
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
                _logger.LogError(ex, "Error in the approval reminder cycle");
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
                // Leased per tenant: worker pods can serve different tenant sets.
                using var lease = await _lockService.TryAcquireLockAsync($"approval-reminders:{tenant}", TimeSpan.FromMinutes(10), ct);
                if (lease == null) continue;

                using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenant, ct);
                var sent = await scope.ServiceProvider.GetRequiredService<ApprovalEmails>()
                    .SendDueRemindersAsync(_clock.GetUtcNow().UtcDateTime, ct);
                if (sent > 0)
                    _logger.LogInformation("Sent {Count} approval reminder digest(s) for tenant {Tenant}", sent, tenant);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Approval reminders failed for tenant {Tenant}", tenant);
            }
        }
    }
}
