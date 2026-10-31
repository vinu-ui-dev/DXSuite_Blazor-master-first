using DXSuite.Host.Multitenancy;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

/// <summary>
/// The X-API-KEY tenant override lets a request header select which tenant's database the
/// request runs against. TenantResolutionMiddleware runs before authentication and before
/// endpoint selection, so no route can decline the header for itself — the gate is the only
/// place the behaviour can be turned off.
/// </summary>
public class TenantApiKeyOverrideTests
{
    [Fact]
    public async Task Override_is_not_consulted_when_the_gate_is_off()
    {
        var store = new RecordingTenantStore();
        var (ctx, subdomainState) = await RunAsync(store, allowOverride: false,
            host: "localhost", apiKey: "real-tenant-api-key");

        Assert.False(store.ApiKeyLookupAttempted);
        Assert.Equal("default", ctx.Subdomain);
        Assert.Equal("default", subdomainState.Subdomain);
    }

    [Fact]
    public async Task Override_replaces_the_default_tenant_when_the_gate_is_on()
    {
        var store = new RecordingTenantStore();
        var (ctx, _) = await RunAsync(store, allowOverride: true,
            host: "localhost", apiKey: "real-tenant-api-key");

        Assert.True(store.ApiKeyLookupAttempted);
        Assert.Equal("keyed", ctx.Subdomain);
    }

    [Fact]
    public async Task Device_tokens_never_trigger_the_lookup()
    {
        var store = new RecordingTenantStore();
        var (ctx, _) = await RunAsync(store, allowOverride: true,
            host: "localhost", apiKey: "dt_abc123");

        Assert.False(store.ApiKeyLookupAttempted);
        Assert.Equal("default", ctx.Subdomain);
    }

    [Fact]
    public async Task Subdomain_requests_ignore_the_header_entirely()
    {
        var store = new RecordingTenantStore();
        var (ctx, _) = await RunAsync(store, allowOverride: true,
            host: "acme.dxsuite.cloud", apiKey: "real-tenant-api-key");

        Assert.False(store.ApiKeyLookupAttempted);
        Assert.Equal("acme", ctx.Subdomain);
    }

    private static async Task<(TenantContext Context, TenantSubdomainState SubdomainState)> RunAsync(
        ITenantStore store, bool allowOverride, string host, string? apiKey)
    {
        var options = Options.Create(new TenantResolutionOptions { AllowApiKeyTenantOverride = allowOverride });
        var middleware = new TenantResolutionMiddleware(
            _ => Task.CompletedTask, options, NullLogger<TenantResolutionMiddleware>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString(host);
        if (apiKey is not null)
            httpContext.Request.Headers["X-API-KEY"] = apiKey;

        var tenantContext = new TenantContext();
        var subdomainState = new TenantSubdomainState();
        await middleware.InvokeAsync(httpContext, store, tenantContext, subdomainState);

        return (tenantContext, subdomainState);
    }
}

/// <summary>
/// Matches any API key hash, so a test that reaches the lookup always swaps tenant.
/// The assertion that matters is whether the lookup was reached at all.
/// </summary>
internal sealed class RecordingTenantStore : ITenantStore
{
    private static readonly TenantRecord Default = new() { TenantId = "t-default", Subdomain = "default" };
    private static readonly TenantRecord Keyed = new() { TenantId = "t-keyed", Subdomain = "keyed" };
    private static readonly TenantRecord Acme = new() { TenantId = "t-acme", Subdomain = "acme" };

    public bool ApiKeyLookupAttempted { get; private set; }

    public Task<IReadOnlyList<TenantRecord>> GetAllEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantRecord>>([Default, Keyed, Acme]);

    public Task<TenantRecord?> FindBySubdomainAsync(string subdomain, CancellationToken ct = default) =>
        Task.FromResult(string.Equals(subdomain, "acme", StringComparison.OrdinalIgnoreCase) ? Acme : null);

    public Task<TenantRecord?> GetDefaultAsync(CancellationToken ct = default) =>
        Task.FromResult<TenantRecord?>(Default);

    public Task<TenantRecord?> FindTenantByApiKeyHashAsync(string keyHash, CancellationToken ct = default)
    {
        ApiKeyLookupAttempted = true;
        return Task.FromResult<TenantRecord?>(Keyed);
    }

    public Task<TenantRecord?> FindByTenantIdAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(new[] { Default, Keyed, Acme }.FirstOrDefault(t => t.TenantId == tenantId));
}
