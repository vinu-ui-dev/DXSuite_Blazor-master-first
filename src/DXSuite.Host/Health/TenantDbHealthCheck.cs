using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Health;

/// <summary>
/// Readiness check that probes each enabled tenant's Director database.
/// Reports Degraded (not Unhealthy) when some but not all tenants are reachable,
/// so a single unavailable tenant DB does not take the whole instance out of rotation.
/// </summary>
public sealed class TenantDbHealthCheck : IHealthCheck
{
    private readonly ITenantStore _tenantStore;

    public TenantDbHealthCheck(ITenantStore tenantStore)
    {
        _tenantStore = tenantStore;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        var tenants = await _tenantStore.GetAllEnabledAsync(ct);
        var results = new Dictionary<string, object>();
        var failed = new List<string>();

        foreach (var tenant in tenants)
        {
            if (string.IsNullOrEmpty(tenant.DirectorConnectionString))
            {
                results[tenant.Subdomain] = "no_director_db";
                continue;
            }

            try
            {
                await using var conn = new SqlConnection(tenant.DirectorConnectionString);
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.CommandTimeout = 5;
                await cmd.ExecuteScalarAsync(ct);
                results[tenant.Subdomain] = "ok";
            }
            catch (Exception ex)
            {
                results[tenant.Subdomain] = $"error: {ex.Message}";
                failed.Add(tenant.Subdomain);
            }
        }

        if (failed.Count == 0)
            return HealthCheckResult.Healthy("All tenant databases reachable.", results);

        if (failed.Count == tenants.Count)
            return HealthCheckResult.Unhealthy(
                $"All {failed.Count} tenant databases unreachable.", data: results);

        return HealthCheckResult.Degraded(
            $"{failed.Count} of {tenants.Count} tenant databases unreachable: {string.Join(", ", failed)}",
            data: results);
    }
}
