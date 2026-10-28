using DXSuite.Shared.Entities.Registry;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Shared.Services.Identity;

/// <summary>
/// Stand-in used when ConnectionStrings:TenantRegistry is absent (dev / single-tenant).
/// Read methods fall back to ITenantStore (config-backed). Mutating methods are unavailable.
/// </summary>
public class NullTenantProvisioningService : ITenantProvisioningService
{
    private readonly ITenantStore _store;

    public NullTenantProvisioningService(ITenantStore store)
    {
        _store = store;
    }

    public bool IsAvailable => false;

    public async Task<IReadOnlyList<TblTenant>> GetAllTenantsAsync()
    {
        var records = await _store.GetAllEnabledAsync();
        return records.Select(MapToTblTenant).ToList();
    }

    public async Task<TblTenant?> GetTenantAsync(string tenantId)
    {
        var all = await _store.GetAllEnabledAsync();
        var record = all.FirstOrDefault(t => t.TenantId == tenantId);
        return record is null ? null : MapToTblTenant(record);
    }

    public Task<TblTenant> CreateTenantAsync(TblTenant tenant, Guid actingPlatformUserId, Guid reAuthToken)
        => throw new NotSupportedException("Tenant provisioning requires a configured TenantRegistry database.");

    public Task<TblTenant> UpdateTenantAsync(TblTenant tenant, Guid actingPlatformUserId, Guid reAuthToken)
        => throw new NotSupportedException("Tenant provisioning requires a configured TenantRegistry database.");

    public Task DisableTenantAsync(string tenantId, Guid actingPlatformUserId, Guid reAuthToken)
        => throw new NotSupportedException("Tenant provisioning requires a configured TenantRegistry database.");

    private static TblTenant MapToTblTenant(TenantRecord r) => new()
    {
        TenantId                 = r.TenantId,
        Subdomain                = r.Subdomain,
        CustomerCode             = r.CustomerCode,
        DisplayName              = r.DisplayName,
        IsEnabled                = r.IsEnabled,
        ConnectionString_Director = r.DirectorConnectionString,
        ConnectionString_Sentinel = r.SentinelConnectionString,
        ConnectionString_CoreData = r.CoreDataConnectionString,
        ConnectionString_Aviation = r.AviationConnectionString,
        CreatedDate              = DateTime.MinValue,
        LastEditDate             = DateTime.MinValue,
    };
}
