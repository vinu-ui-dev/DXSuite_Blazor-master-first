using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Entities.Registry;

namespace DXSuite.Shared.Services.Identity;

public class TenantProvisioningService : ITenantProvisioningService
{
    private readonly ITenantRegistryDbContext _registry;
    private readonly IPlatformAuditService _audit;
    private readonly ReAuthConfirmationService _reAuth;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(ITenantRegistryDbContext registry, IPlatformAuditService audit,
        ReAuthConfirmationService reAuth, ILogger<TenantProvisioningService> logger)
    {
        _registry = registry;
        _audit = audit;
        _reAuth = reAuth;
        _logger = logger;
    }

    public bool IsAvailable => true;

    public async Task<IReadOnlyList<TblTenant>> GetAllTenantsAsync()
        => await _registry.TblTenants.AsNoTracking().OrderBy(t => t.DisplayName).ToListAsync();

    public async Task<TblTenant?> GetTenantAsync(string tenantId)
        => await _registry.TblTenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId);

    public async Task<TblTenant> CreateTenantAsync(TblTenant tenant, Guid actingPlatformUserId, Guid reAuthToken)
    {
        RequireReAuth(reAuthToken);

        tenant.CreatedDate  = DateTime.UtcNow;
        tenant.LastEditDate = DateTime.UtcNow;

        _registry.TblTenants.Add(tenant);
        await _registry.SaveChangesAsync();

        await _audit.WriteAsync(AuditEventType.TenantProvisioned, actingPlatformUserId, tenant.TenantId);

        _logger.LogInformation("Tenant {TenantId} provisioned by {ActingUser}", tenant.TenantId, actingPlatformUserId);
        return tenant;
    }

    public async Task<TblTenant> UpdateTenantAsync(TblTenant tenant, Guid actingPlatformUserId, Guid reAuthToken)
    {
        RequireReAuth(reAuthToken);

        var existing = await _registry.TblTenants.FindAsync(tenant.TenantId)
            ?? throw new InvalidOperationException($"Tenant {tenant.TenantId} not found.");

        existing.Subdomain                  = tenant.Subdomain;
        existing.CustomerCode               = tenant.CustomerCode;
        existing.DisplayName                = tenant.DisplayName;
        existing.IsEnabled                  = tenant.IsEnabled;
        existing.ConnectionString_Director  = tenant.ConnectionString_Director;
        existing.ConnectionString_Sentinel  = tenant.ConnectionString_Sentinel;
        existing.ConnectionString_CoreData  = tenant.ConnectionString_CoreData;
        existing.ConnectionString_Aviation  = tenant.ConnectionString_Aviation;
        existing.AuthConfigJson             = tenant.AuthConfigJson;
        existing.ModulesJson                = tenant.ModulesJson;
        existing.FeaturesJson               = tenant.FeaturesJson;
        existing.SmtpConfigJson             = tenant.SmtpConfigJson;
        existing.FilePaths_MediaRoot        = tenant.FilePaths_MediaRoot;
        existing.MaxUsers                   = tenant.MaxUsers;
        existing.LastEditDate               = DateTime.UtcNow;

        await _registry.SaveChangesAsync();

        await _audit.WriteAsync(AuditEventType.TenantSettingsUpdated, actingPlatformUserId, tenant.TenantId);

        _logger.LogInformation("Tenant {TenantId} updated by {ActingUser}", tenant.TenantId, actingPlatformUserId);
        return existing;
    }

    public async Task DisableTenantAsync(string tenantId, Guid actingPlatformUserId, Guid reAuthToken)
    {
        RequireReAuth(reAuthToken);

        var tenant = await _registry.TblTenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        tenant.IsEnabled    = false;
        tenant.LastEditDate = DateTime.UtcNow;
        await _registry.SaveChangesAsync();

        await _audit.WriteAsync(AuditEventType.TenantDeprovisioned, actingPlatformUserId, tenantId);

        _logger.LogInformation("Tenant {TenantId} disabled by {ActingUser}", tenantId, actingPlatformUserId);
    }

    private void RequireReAuth(Guid token)
    {
        if (!_reAuth.ValidateToken(token))
            throw new UnauthorizedAccessException(
                "Re-authentication confirmation has expired or is invalid. Please confirm the action again.");
        _reAuth.ConsumeToken();
    }
}
