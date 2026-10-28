namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Scoped carrier that holds the subdomain resolved during SSR prerender so
/// TenantCircuitInitializer can populate TenantContext when the interactive
/// circuit opens in its own (middleware-free) DI scope.
/// </summary>
internal sealed class TenantSubdomainState
{
    public string? Subdomain { get; set; }
}
