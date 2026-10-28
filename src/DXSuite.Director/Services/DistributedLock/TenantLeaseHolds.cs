using System.Collections.Concurrent;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Director.Services.DistributedLock;

/// <summary>
/// Runs tenant-scoped background work under a per-tenant distributed lease (HOST-04 item 2): for each of this
/// instance's worker tenants where <c>shouldHold</c> says the work is needed, tries the lease <c>leaseName(tenantId)</c>
/// names; the instance that gets it runs the caller's per-tenant work until the lease's term elapses, then releases
/// and tries again, so a holder that silently drops out (a lost SQL session, a crash) is replaced within one term.
/// Shared by <c>CoreApiWatcherService</c> and <c>MessagingDoorbellService</c> so the lease-retry loop is written once.
/// </summary>
public sealed class TenantLeaseHolds(
    IWorkerScopeFactory workerScopes,
    IDistributedLockService leases,
    TimeProvider clock,
    ILogger logger,
    string label,
    Func<string, string> leaseName,
    Func<ITenantContext, bool> shouldHold,
    TimeSpan leaseRetry,
    TimeSpan leaseTerm)
{
    private readonly ConcurrentDictionary<string, byte> _held = new();

    /// <summary>The worker-tenant subdomains this instance currently holds the lease for.</summary>
    public IReadOnlyCollection<string> TenantsHeld => _held.Keys.ToList();

    /// <summary>
    /// Runs until <paramref name="ct"/> is cancelled: one independent retry loop per worker tenant, each trying its
    /// own lease and, while held, running <paramref name="work"/>(subdomain, tenantId, ct) — a token that cancels
    /// when this hold's lease term elapses, so the caller's own loop unwinds and the lease is taken again.
    /// </summary>
    public Task RunAsync(Func<string, string, CancellationToken, Task> work, CancellationToken ct) =>
        Task.WhenAll(workerScopes.WorkerTenants.Select(subdomain => HoldTenantAsync(subdomain, work, ct)));

    private async Task HoldTenantAsync(string subdomain, Func<string, string, CancellationToken, Task> work, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string tenantId;
                using (var scope = await workerScopes.CreateTenantScopeAsync(subdomain, ct))
                {
                    var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                    if (!shouldHold(tenant))
                    {
                        await Task.Delay(leaseRetry, clock, ct);
                        continue;
                    }
                    tenantId = tenant.TenantId;
                }

                using var held = await leases.TryAcquireLockAsync(leaseName(tenantId), leaseTerm, ct);
                if (held is null)
                {
                    await Task.Delay(leaseRetry, clock, ct);
                    continue;
                }

                _held[subdomain] = 0;
                try
                {
                    using var termCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    termCts.CancelAfter(leaseTerm);
                    try
                    {
                        await work(subdomain, tenantId, termCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // The lease term elapsed, not the host stopping — fall through to take it again.
                    }
                }
                finally
                {
                    _held.TryRemove(subdomain, out _);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Label} failed for tenant {Subdomain}", label, subdomain);
                await Task.Delay(leaseRetry, clock, ct);
            }
        }
    }
}
