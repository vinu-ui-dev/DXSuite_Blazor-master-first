using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Registry;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Tenant registry backed by the DXSuite_TenantRegistry SQL database (Phase 8 / Option A).
/// Replaces ConfigTenantStore when ConnectionStrings:TenantRegistry is present.
/// Caches the full tenant list and an API-key → tenant dictionary in memory, refreshing
/// every 5 minutes in the background so changes to TblTenant are picked up without restart.
/// </summary>
internal sealed class SqlTenantStore : ITenantStore, IHostedService, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqlTenantStore> _logger;
    private readonly string? _defaultSubdomain;

    // Immutable snapshots replaced atomically on each refresh.
    private volatile IReadOnlyList<TenantRecord> _tenants = [];
    private volatile FrozenDictionary<string, string> _apiKeyHashToTenantId = FrozenDictionary<string, string>.Empty;

    private Timer? _refreshTimer;

    public SqlTenantStore(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<SqlTenantStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _defaultSubdomain = config["DXSuite:DefaultTenant"];
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        // Eagerly load so the store is ready before the first request.
        await RefreshAsync(ct);

        if (_tenants.Count == 0)
            throw new InvalidOperationException(
                "SqlTenantStore: no enabled tenants found in TblTenant. " +
                "Ensure at least one row with IsEnabled=1 exists in the TenantRegistry database.");

        _refreshTimer = new Timer(_ => _ = RefreshAsync(CancellationToken.None),
            null, RefreshInterval, RefreshInterval);
    }

    public Task StopAsync(CancellationToken ct)
    {
        _refreshTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _refreshTimer?.Dispose();

    // ── ITenantStore ─────────────────────────────────────────────────────────

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

    public Task<TenantRecord?> FindTenantByApiKeyHashAsync(string keyHash, CancellationToken ct = default)
    {
        // O(1) lookup via the in-memory dictionary built from TblApiKeyMapping.
        if (!_apiKeyHashToTenantId.TryGetValue(keyHash, out var tenantId))
            return Task.FromResult<TenantRecord?>(null);

        var tenant = _tenants.FirstOrDefault(t => t.TenantId == tenantId);
        return Task.FromResult(tenant);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ITenantRegistryDbContext>();

            var rows = await db.TblTenants
                .AsNoTracking()
                .Where(t => t.IsEnabled)
                .ToListAsync(ct);

            var keyRows = await db.TblApiKeyMappings
                .AsNoTracking()
                .Where(k => k.IsActive)
                .ToListAsync(ct);

            _tenants = rows.Select(MapToRecord).ToList();
            _apiKeyHashToTenantId = keyRows.ToFrozenDictionary(k => k.ApiKeyHash, k => k.TenantId);

            _logger.LogDebug("SqlTenantStore refreshed: {Count} tenants, {Keys} API key mappings",
                _tenants.Count, _apiKeyHashToTenantId.Count);
        }
        catch (Exception ex)
        {
            // Refresh failures preserve the last known good snapshot rather than zeroing it out.
            _logger.LogError(ex, "SqlTenantStore refresh failed — retaining stale cache");
        }
    }

    private static TenantRecord MapToRecord(TblTenant row) => new()
    {
        TenantId             = row.TenantId,
        Subdomain            = row.Subdomain,
        CustomerCode         = row.CustomerCode,
        DisplayName          = row.DisplayName,
        IsEnabled            = row.IsEnabled,
        DirectorConnectionString = row.ConnectionString_Director,
        SentinelConnectionString = row.ConnectionString_Sentinel,
        CoreDataConnectionString = row.ConnectionString_CoreData,
        AviationConnectionString = row.ConnectionString_Aviation,
        DesignConnectionString   = row.ConnectionString_Design,
        MediaRootPath        = row.FilePaths_MediaRoot,
        SiteUrl              = row.SiteUrl,
        AuthConfig           = Deserialize<TenantAuthConfig>(row.AuthConfigJson) ?? new(),
        Modules              = Deserialize<TenantModuleConfig>(row.ModulesJson) ?? new(),
        Features             = Deserialize<TenantFeatureFlags>(row.FeaturesJson) ?? new(),
    };

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }
}
