using System.Security.Cryptography;
using System.Text;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Tenant registry backed by appsettings.json "Tenants" section (Phase 1 / Option B).
/// Reads and validates at startup; fails fast if no tenants are configured.
/// Swappable for a SQL-backed store (Phase 6) without changing consumers.
/// </summary>
internal sealed class ConfigTenantStore : ITenantStore
{
    private readonly IReadOnlyList<TenantRecord> _tenants;
    private readonly string? _defaultSubdomain;

    public ConfigTenantStore(IConfiguration config)
    {
        _defaultSubdomain = config["DXSuite:DefaultTenant"];

        _tenants = config.GetSection("Tenants")
            .GetChildren()
            .Select(ParseTenant)
            .Where(t => t.IsEnabled)
            .ToList();

        if (_tenants.Count == 0)
            throw new InvalidOperationException(
                "No tenants are configured. Add at least one entry under 'Tenants' in appsettings.json " +
                "and set 'DXSuite:DefaultTenant' to its key for localhost fallback.");
    }

    public Task<TenantRecord?> FindBySubdomainAsync(string subdomain, CancellationToken ct = default)
    {
        var match = _tenants.FirstOrDefault(t =>
            string.Equals(t.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<TenantRecord?> GetDefaultAsync(CancellationToken ct = default)
    {
        TenantRecord? tenant = null;

        if (_defaultSubdomain != null)
            tenant = _tenants.FirstOrDefault(t =>
                string.Equals(t.Subdomain, _defaultSubdomain, StringComparison.OrdinalIgnoreCase));

        tenant ??= _tenants.FirstOrDefault();
        return Task.FromResult(tenant);
    }

    public Task<IReadOnlyList<TenantRecord>> GetAllEnabledAsync(CancellationToken ct = default)
        => Task.FromResult(_tenants);

    public Task<TenantRecord?> FindByTenantIdAsync(string tenantId, CancellationToken ct = default)
    {
        var match = _tenants.FirstOrDefault(t => string.Equals(t.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public async Task<TenantRecord?> FindTenantByApiKeyHashAsync(string keyHash, CancellationToken ct = default)
    {
        // Linear scan across tenant Director DBs. This is the Option B implementation;
        // Phase 8 replaces this with an O(1) central mapping table query.
        foreach (var tenant in _tenants)
        {
            if (string.IsNullOrEmpty(tenant.DirectorConnectionString)) continue;

            try
            {
                var options = new DbContextOptionsBuilder<DirectorDbContext>()
                    .UseSqlServer(tenant.DirectorConnectionString)
                    .Options;
                await using var db = new DirectorDbContext(options);
                var exists = await db.TblApiKeys.AnyAsync(k => k.ApiKeyHash == keyHash, ct);
                if (exists) return tenant;
            }
            catch
            {
                // Best effort — unavailable tenant DBs are skipped silently.
            }
        }
        return null;
    }

    private static TenantRecord ParseTenant(IConfigurationSection s) => new()
    {
        TenantId = s["TenantId"] ?? Guid.NewGuid().ToString(),
        Subdomain = s.Key,
        CustomerCode = s["CustomerCode"] ?? s.Key.ToUpperInvariant(),
        DisplayName = s["DisplayName"] ?? s.Key,
        IsEnabled = s.GetValue("IsEnabled", true),
        DirectorConnectionString = s["ConnectionStrings:Director"],
        SentinelConnectionString = s["ConnectionStrings:Sentinel"],
        CoreDataConnectionString = s["ConnectionStrings:CoreData"],
        AviationConnectionString = s["ConnectionStrings:Aviation"],
        DesignConnectionString = s["ConnectionStrings:Design"],
        AuthConfig = s.GetSection("Auth").Get<TenantAuthConfig>() ?? new TenantAuthConfig(),
        Modules = s.GetSection("Modules").Get<TenantModuleConfig>() ?? new TenantModuleConfig(),
        Features = s.GetSection("Features").Get<TenantFeatureFlags>() ?? new TenantFeatureFlags(),
        MediaRootPath = s["MediaRootPath"],
        SiteUrl = s["SiteUrl"],
    };
}
