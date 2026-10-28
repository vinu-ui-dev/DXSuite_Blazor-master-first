using DXSuite.Director.Models;
using DXSuite.Director.Services;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

/// <summary>
/// Pins the thumbnail storage contract: files live outside wwwroot (the anonymous static-files
/// middleware must never serve them) in a per-tenant folder, while web paths stay tenant-free
/// (WebThumbnailController maps the caller's resolved tenant server-side).
/// </summary>
public class WebPreviewServicePathTests
{
    private static WebPreviewService CreateService(ITenantContext tenant)
    {
        var guard = new OutboundUrlGuard(
            Options.Create(new WebPreviewOptions()), NullLogger<OutboundUrlGuard>.Instance, new StubTenantStore());
        return new WebPreviewService(
            Options.Create(new WebPreviewOptions()),
            new ThumbnailService(NullLogger<ThumbnailService>.Instance),
            new StubHttpClientFactory(),
            new StubEnvironment(),
            tenant,
            guard,
            new EgressFilteringProxy(guard, NullLogger<EgressFilteringProxy>.Instance),
            NullLogger<WebPreviewService>.Instance);
    }

    [Fact]
    public void Local_path_is_tenant_scoped_and_outside_wwwroot()
    {
        var service = CreateService(new StubTenantContext { Subdomain = "acme" });
        var path = service.GetThumbnailLocalPath(42);

        Assert.Equal(Path.Combine(@"C:\approot", "App_Data", "WebThumbnails", "acme", "42.png"), path);
        Assert.DoesNotContain("wwwroot", path);
    }

    [Fact]
    public void Different_tenants_get_different_local_paths_for_the_same_asset_id()
    {
        var acme = CreateService(new StubTenantContext { Subdomain = "acme" }).GetThumbnailLocalPath(7);
        var beta = CreateService(new StubTenantContext { Subdomain = "beta" }).GetThumbnailLocalPath(7);
        Assert.NotEqual(acme, beta);
    }

    [Fact]
    public void Web_path_contains_no_tenant_segment()
    {
        Assert.Equal("/WebThumbnails/42.png", WebPreviewService.GetThumbnailWebPath(42));
    }

    [Fact]
    public void Versioned_web_path_falls_back_to_stable_url_when_tenant_is_unpopulated()
    {
        var service = CreateService(new StubTenantContext { Populated = false });
        Assert.Equal("/WebThumbnails/42.png", service.GetVersionedThumbnailWebPath(42));
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("C:")]
    public void Local_path_rejects_tenant_names_that_could_escape_the_tree(string subdomain)
    {
        var service = CreateService(new StubTenantContext { Subdomain = subdomain });
        Assert.Throws<InvalidOperationException>(() => service.GetThumbnailLocalPath(1));
    }

    [Fact]
    public void Tenant_folder_is_case_folded_so_case_variants_cannot_diverge()
    {
        var lower = CreateService(new StubTenantContext { Subdomain = "acme" }).GetThumbnailLocalPath(1);
        var upper = CreateService(new StubTenantContext { Subdomain = "ACME" }).GetThumbnailLocalPath(1);
        Assert.Equal(lower, upper);
    }

    /// <summary>
    /// The read path's only access control is the controller's [Authorize]. Nothing else in the
    /// system fails if it is removed, so pin it: these files were anonymously enumerable before.
    /// </summary>
    [Fact]
    public void Thumbnail_controller_requires_authentication()
    {
        var controller = typeof(Controllers.WebThumbnailController);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true));
        Assert.Empty(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true));

        foreach (var method in controller.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            Assert.Empty(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true));
    }

    internal sealed class StubTenantContext : ITenantContext
    {
        public bool Populated { get; init; } = true;
        public string Subdomain { get; init; } = "test";

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
        public string MediaRootPath => "";
        public string? SiteUrl => null;
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = @"C:\approot\wwwroot";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = @"C:\approot";
        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
