using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace DXSuite.Director.Tests;

/// <summary>
/// Pins the tenant media root contract: tenant content resolves under a per-tenant root
/// (default wwwroot\tenants\{subdomain}), a FilePaths_MediaRoot override wins (absolute
/// as-is, relative against the content root — so 'wwwroot' pins a legacy tenant to the
/// global folder), and unresolved scopes fall back to the global web root so single-tenant
/// installs behave unchanged.
/// </summary>
public class TenantMediaPathTests
{
    [Fact]
    public void Unpopulated_tenant_falls_back_to_web_root()
    {
        var root = TenantMediaPaths.GetRoot(new StubMediaTenant { Populated = false }, new StubEnv());
        Assert.Equal(@"C:\approot\wwwroot", root);
    }

    [Fact]
    public void Null_tenant_falls_back_to_web_root()
    {
        Assert.Equal(@"C:\approot\wwwroot", TenantMediaPaths.GetRoot(null, new StubEnv()));
    }

    [Fact]
    public void Default_root_is_tenant_folder_under_web_root()
    {
        var root = TenantMediaPaths.GetRoot(new StubMediaTenant { Subdomain = "acme" }, new StubEnv());
        Assert.Equal(Path.Combine(@"C:\approot", "wwwroot", "tenants", "acme"), root);
    }

    [Fact]
    public void Absolute_override_is_used_verbatim()
    {
        var tenant = new StubMediaTenant { Subdomain = "acme", MediaRootOverride = @"D:\TenantMedia\acme" };
        Assert.Equal(@"D:\TenantMedia\acme", TenantMediaPaths.GetRoot(tenant, new StubEnv()));
    }

    [Fact]
    public void Wwwroot_override_pins_legacy_tenant_to_the_global_web_root()
    {
        var tenant = new StubMediaTenant { Subdomain = "radiant", MediaRootOverride = "wwwroot" };
        var root = TenantMediaPaths.GetRoot(tenant, new StubEnv());
        Assert.Equal(Path.GetFullPath(@"C:\approot\wwwroot"), Path.GetFullPath(root));
    }

    [Fact]
    public void Different_tenants_get_different_roots()
    {
        var a = TenantMediaPaths.GetRoot(new StubMediaTenant { Subdomain = "acme" }, new StubEnv());
        var b = TenantMediaPaths.GetRoot(new StubMediaTenant { Subdomain = "beta" }, new StubEnv());
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Isolation_disabled_by_default_resolves_web_root_even_with_a_tenant()
    {
        var config = new ConfigurationBuilder().Build();
        var root = TenantMediaPaths.GetRoot(new StubMediaTenant { Subdomain = "acme" }, new StubEnv(), config);
        Assert.Equal(@"C:\approot\wwwroot", root);
        Assert.False(TenantMediaPaths.IsEnabled(config));
    }

    [Fact]
    public void Isolation_enabled_resolves_tenant_root()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DXSuite:TenantMedia:Enabled"] = "true" })
            .Build();
        var root = TenantMediaPaths.GetRoot(new StubMediaTenant { Subdomain = "acme" }, new StubEnv(), config);
        Assert.Equal(Path.Combine(@"C:\approot", "wwwroot", "tenants", "acme"), root);
    }

    /// <summary>Mirrors TenantContext.MediaRootPath: override wins, else wwwroot/tenants/{sub}.</summary>
    private sealed class StubMediaTenant : ITenantContext
    {
        public bool Populated { get; init; } = true;
        public string Subdomain { get; init; } = "test";
        public string? MediaRootOverride { get; init; }

        public bool IsPopulated => Populated;
        public string TenantId => Subdomain;
        public string CustomerCode => Subdomain;
        public string DisplayName => Subdomain;
        public string? DirectorConnectionString => null;
        public string? SentinelConnectionString => null;
        public string? CoreDataConnectionString => null;
        public string? AviationConnectionString => null;
        public string? DesignConnectionString => null;
        public TenantAuthConfig AuthConfig => new();
        public TenantModuleConfig Modules => new();
        public TenantFeatureFlags Features => new();
        public string MediaRootPath => MediaRootOverride ?? Path.Combine("wwwroot", "tenants", Subdomain);
        public string? SiteUrl => null;
    }

    private sealed class StubEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = @"C:\approot\wwwroot";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = @"C:\approot";
        public string EnvironmentName { get; set; } = "Test";
    }
}
