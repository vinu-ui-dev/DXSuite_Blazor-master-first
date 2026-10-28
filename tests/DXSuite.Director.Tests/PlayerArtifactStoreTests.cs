using System.Text;
using DXSuite.Director.Services;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// Artifacts left the web root so that no tenant can read or overwrite another's bytes, and so
/// that static-file middleware serves none of them.
/// </summary>
public class PlayerArtifactStoreTests : IDisposable
{
    private readonly string _contentRoot = Directory.CreateTempSubdirectory("artifact-store").FullName;

    public void Dispose() => Directory.Delete(_contentRoot, recursive: true);

    private sealed class FakeEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class FakeTenantContext(string? subdomain) : ITenantContext
    {
        public bool IsPopulated => subdomain != null;
        public string Subdomain => subdomain ?? "";
        public string TenantId => subdomain ?? "";
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
        public string MediaRootPath => "wwwroot";
        public string? SiteUrl => null;
    }

    private PlayerArtifactStore CreateStore(string? subdomain)
        => new(new FakeTenantContext(subdomain), new FakeEnvironment(_contentRoot),
            new ConfigurationBuilder().Build(), NullLogger<PlayerArtifactStore>.Instance);

    [Fact]
    public void TheRootIsOutsideTheWebRoot()
    {
        var store = CreateStore("acme");
        var env = new FakeEnvironment(_contentRoot);

        Assert.DoesNotContain(env.WebRootPath, store.GetRoot());
    }

    [Fact]
    public void EachTenantGetsItsOwnRoot()
    {
        // The defect this closes: one tenant uploading a version number another already had
        // silently replaced that tenant's zip, leaving its recorded hash describing other bytes.
        Assert.NotEqual(CreateStore("acme").GetRoot(), CreateStore("globex").GetRoot());
    }

    [Fact]
    public async Task ASavedArtifactReadsBackFromTheTenantRoot()
    {
        var store = CreateStore("acme");
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("zip bytes"));

        var storedPath = await store.SaveAsync(content, "PlayerContent/EngageBrightSign/2.0.1/app.zip");
        var resolved = store.ResolveExisting(storedPath);

        Assert.NotNull(resolved);
        Assert.StartsWith(store.GetRoot(), resolved);
        Assert.Equal("zip bytes", await File.ReadAllTextAsync(resolved));
    }

    [Fact]
    public async Task OneTenantCannotSeeAnothersArtifact()
    {
        var acme = CreateStore("acme");
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("zip bytes"));
        var storedPath = await acme.SaveAsync(content, "PlayerContent/EngageBrightSign/2.0.1/app.zip");

        Assert.Null(CreateStore("globex").ResolveExisting(storedPath));
    }

    [Fact]
    public async Task ARowWrittenBeforeTheStoreStillResolvesFromTheWebRoot()
    {
        // Read-only fallback: nothing is written back there, so the anonymous surface shrinks to
        // the artifacts that already existed rather than being inherited by every new one.
        var store = CreateStore("acme");
        var legacy = Path.Combine(_contentRoot, "wwwroot", "PlayerContent", "BrightSign", "1.0.8");
        Directory.CreateDirectory(legacy);
        await File.WriteAllTextAsync(Path.Combine(legacy, "old.zip"), "legacy");

        var resolved = store.ResolveExisting("PlayerContent/BrightSign/1.0.8/old.zip");

        Assert.NotNull(resolved);
        Assert.Equal("legacy", await File.ReadAllTextAsync(resolved));
    }

    [Fact]
    public void AnArtifactMissingFromDiskResolvesToNull()
    {
        Assert.Null(CreateStore("acme").ResolveExisting("PlayerContent/nothing/here.zip"));
    }

    [Theory]
    [InlineData("../../escape.zip")]
    [InlineData("PlayerContent/../../escape.zip")]
    public async Task APathThatClimbsOutOfTheRootIsRefused(string path)
    {
        var store = CreateStore("acme");
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(content, path));
    }

    /// <summary>
    /// A store with no tenant reads a different root from one with a tenant, which is the whole
    /// reason <c>CreateVersionAsync</c> and <c>DeleteVersionAsync</c> take the caller's store.
    ///
    /// <c>UpdateService</c> is a singleton and opens its own scopes; a scope opened that way carries
    /// no request, so an <c>IPlayerArtifactStore</c> resolved from it has an unpopulated tenant
    /// context. Uploading wrote under the tenant root and then looked under the bare one, and
    /// failed with "Artifact not found" naming a path that did exist. It is invisible on a
    /// single-tenant run, where both roots are the same directory, and it broke the first upload
    /// attempted against a subdomain-routed host.
    /// </summary>
    [Fact]
    public void AStoreWithNoTenantCannotSeeATenantsArtifact()
    {
        var tenant = CreateStore("acme");
        var tenantless = CreateStore(null);

        Assert.NotEqual(tenant.GetRoot(), tenantless.GetRoot());

        var stored = tenant.SaveAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("artifact")),
            "PlayerContent/EngageBrightSign/bootloader/2.0.0/Bootloader.zip").GetAwaiter().GetResult();

        Assert.NotNull(tenant.ResolveExisting(stored));
        Assert.Null(tenantless.ResolveExisting(stored));
    }
}
