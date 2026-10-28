using DXSuite.Shared.Multitenancy;

namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Scoped implementation of ITenantContext. TenantResolutionMiddleware calls
/// Populate() once per request before any other middleware or handler runs.
/// </summary>
internal sealed class TenantContext : IPopulatableTenantContext
{
    private TenantRecord? _record;

    // Implements IPopulatableTenantContext — public so DI can resolve via the interface.
    public void Populate(TenantRecord record) => _record = record;

    public bool IsPopulated => _record != null;

    private TenantRecord Record => _record ?? throw new InvalidOperationException(
        "ITenantContext has not been populated. Ensure TenantResolutionMiddleware is registered " +
        "and runs before any code that depends on ITenantContext.");

    public string TenantId => Record.TenantId;
    public string Subdomain => Record.Subdomain;
    public string CustomerCode => Record.CustomerCode;
    public string DisplayName => Record.DisplayName;
    public string? DirectorConnectionString => Record.DirectorConnectionString;
    public string? SentinelConnectionString => Record.SentinelConnectionString;
    public string? CoreDataConnectionString => Record.CoreDataConnectionString;
    public string? AviationConnectionString => Record.AviationConnectionString;
    public string? DesignConnectionString => Record.DesignConnectionString;
    public TenantAuthConfig AuthConfig => Record.AuthConfig;
    public TenantModuleConfig Modules => Record.Modules;
    public TenantFeatureFlags Features => Record.Features;

    public string MediaRootPath =>
        Record.MediaRootPath ?? Path.Combine("wwwroot", "tenants", Record.Subdomain);

    public string? SiteUrl => Record.SiteUrl;
}
