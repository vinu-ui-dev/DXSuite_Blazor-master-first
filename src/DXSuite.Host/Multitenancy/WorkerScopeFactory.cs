using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Creates per-tenant DI scopes for background service iteration.
/// WorkerTenants defaults to all enabled tenants in standalone mode and is restricted to
/// DXSuite:WorkerTenants in worker mode so each worker pod handles only its assigned tenants.
/// </summary>
internal sealed class WorkerScopeFactory : IWorkerScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantStore _tenantStore;

    // Non-null only when DXSuite:WorkerTenants is explicitly configured. When null, WorkerTenants
    // falls back to querying the tenant store on each access (see property below).
    private readonly IReadOnlyList<string>? _configured;

    public WorkerScopeFactory(IServiceScopeFactory scopeFactory, ITenantStore tenantStore, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _tenantStore = tenantStore;

        var configured = config.GetSection("DXSuite:WorkerTenants").Get<string[]>();
        if (configured is { Length: > 0 })
            _configured = configured;

        // When DXSuite:WorkerTenants is absent, WorkerTenants is resolved from the store on each
        // access rather than captured here. SqlTenantStore loads its tenant list asynchronously in
        // IHostedService.StartAsync — after all background-service constructors run. Capturing at
        // construction time would always return an empty list when SqlTenantStore is active.
    }

    /// <inheritdoc />
    /// <remarks>
    /// When DXSuite:WorkerTenants is configured, returns that static list.
    /// Otherwise calls GetAllEnabledAsync() on each access; both ConfigTenantStore and
    /// SqlTenantStore return Task.FromResult(...) so GetAwaiter().GetResult() is safe.
    /// </remarks>
    public IReadOnlyList<string> WorkerTenants =>
        _configured ?? _tenantStore.GetAllEnabledAsync()
            .GetAwaiter().GetResult()
            .Select(t => t.Subdomain)
            .ToList();

    public async Task<IServiceScope> CreateTenantScopeAsync(string subdomain, CancellationToken ct = default)
    {
        var record = await _tenantStore.FindBySubdomainAsync(subdomain, ct)
            ?? throw new InvalidOperationException(
                $"Worker tenant '{subdomain}' not found in the tenant store. " +
                "Verify DXSuite:WorkerTenants contains valid tenant subdomains.");

        var scope = _scopeFactory.CreateScope();
        var populatable = scope.ServiceProvider.GetRequiredService<IPopulatableTenantContext>();
        populatable.Populate(record);
        return scope;
    }
}
