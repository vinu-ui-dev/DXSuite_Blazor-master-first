using DXSuite.Director.Services.DistributedLock;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.Messaging;

/// <summary>How often the doorbell job does each part of its work; only tests change these.</summary>
/// <param name="VersionCheck">Between reads of the messaging delivery version (INK-09: every second).</param>
/// <param name="HashCheck">Between hashes of the delivery body, which catch time boundaries (INK-09: every 5 seconds).</param>
/// <param name="LeaseRetry">Between attempts to take a tenant's lease, and to notice a tenant whose Messaging came on.</param>
/// <param name="LeaseTerm">How long one hold of the lease lasts before it is given up and taken again.</param>
public sealed record MessagingDoorbellTiming(TimeSpan VersionCheck, TimeSpan HashCheck, TimeSpan LeaseRetry, TimeSpan LeaseTerm)
{
    public static MessagingDoorbellTiming Default { get; } =
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(10));
}

/// <summary>
/// The doorbell job (INK-09): for each worker tenant with Messaging on, exactly one Director instance, the holder of
/// the SQL lease <c>messaging-doorbell:{tenantId}</c> (HOST-04 item 2), runs <see cref="MessagingDoorbell"/> every
/// second, with the body's hash every 5 seconds. With <c>Messaging:Delivery:DoorbellUrl</c> empty the job takes no
/// lease and reads nothing.
/// </summary>
/// <remarks>
/// The lease is held for <see cref="MessagingDoorbellTiming.LeaseTerm"/> and then taken again at once
/// (<see cref="TenantLeaseHolds"/>), so a holder whose SQL session was lost without it noticing stops within one term;
/// the doorbell's own state in SQL keeps two holders from ringing twice for one change in the meantime.
/// </remarks>
public sealed class MessagingDoorbellService : BackgroundService
{
    public static string LeaseName(string tenantId) => $"messaging-doorbell:{tenantId}";

    private readonly IWorkerScopeFactory _workerScopes;
    private readonly bool _on;
    private readonly TimeProvider _clock;
    private readonly ILogger<MessagingDoorbellService> _logger;
    private readonly MessagingDoorbellTiming _timing;
    private readonly TenantLeaseHolds _holds;

    public MessagingDoorbellService(IWorkerScopeFactory workerScopes, IDistributedLockService leases, IOptions<MessagingOptions> options,
        TimeProvider clock, ILogger<MessagingDoorbellService> logger)
        : this(workerScopes, leases, options, clock, logger, MessagingDoorbellTiming.Default)
    {
    }

    internal MessagingDoorbellService(IWorkerScopeFactory workerScopes, IDistributedLockService leases, IOptions<MessagingOptions> options,
        TimeProvider clock, ILogger<MessagingDoorbellService> logger, MessagingDoorbellTiming timing)
    {
        _workerScopes = workerScopes;
        _on = options.Value.Delivery.DoorbellOn;
        _clock = clock;
        _logger = logger;
        _timing = timing;
        _holds = new TenantLeaseHolds(workerScopes, leases, clock, logger, "The delivery doorbell", LeaseName, HasMessaging,
            timing.LeaseRetry, timing.LeaseTerm);
    }

    /// <summary>The tenant ids this instance holds the doorbell lease for right now.</summary>
    public IReadOnlyCollection<string> TenantsHeld => _holds.TenantsHeld;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // The URL is a per-instance setting read at start-up; pull only needs no job at all.
        _on ? _holds.RunAsync(CheckUntilStoppedAsync, stoppingToken) : Task.CompletedTask;

    private async Task CheckUntilStoppedAsync(string subdomain, string tenantId, CancellationToken ct)
    {
        var nextHash = _clock.GetUtcNow();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var now = _clock.GetUtcNow();
                // The hash catches what changes the body with no bump: a window or day boundary, and a device or
                // location time zone change, which moves when a scheduled Activation shows and its endUtc.
                var withHash = now >= nextHash;
                if (withHash) nextHash = now + _timing.HashCheck;

                try
                {
                    using var scope = await _workerScopes.CreateTenantScopeAsync(subdomain, ct);
                    if (!HasMessaging(scope.ServiceProvider.GetRequiredService<ITenantContext>())) return;
                    await scope.ServiceProvider.GetRequiredService<MessagingDoorbell>().CheckAsync(withHash, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One failed read (a dropped connection, a deadlock victim) must not end the hold; the next check retries.
                    _logger.LogError(ex, "A delivery doorbell check failed for tenant {Tenant}", tenantId);
                }

                await Task.Delay(_timing.VersionCheck, _clock, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    // Without Messaging the tenant has no delivery route (INK-03), so there is nothing to ring for.
    private static bool HasMessaging(ITenantContext tenant) => tenant.IsPopulated && tenant.Features.Messaging;
}
