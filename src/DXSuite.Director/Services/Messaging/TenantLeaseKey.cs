using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services.Messaging;

/// <summary>The id a per-tenant lease name is built from: the tenant's own id when the scope resolved one,
/// else the worker subdomain it was asked for (a tenant context that failed to populate is still one tenant).</summary>
public static class TenantLeaseKey
{
    public static string Of(ITenantContext? tenant, string fallbackSubdomain) =>
        tenant?.IsPopulated == true ? tenant.TenantId : fallbackSubdomain;
}
