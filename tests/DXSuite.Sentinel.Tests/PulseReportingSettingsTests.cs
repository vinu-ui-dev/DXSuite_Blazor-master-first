using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services;
using DXSuite.Sentinel.Services.Pulse;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DXSuite.Sentinel.Tests;

/// <summary>
/// Pins how a tenant's Pulse reporting settings resolve: tenant keys win one by one, the
/// top level fills the gaps, and an enabled tenant that cannot write correctly fails loudly.
/// Also pins that Sentinel's DbContext follows a worker tenant scope, which the publisher
/// depends on to read each tenant's own Sentinel DB.
/// </summary>
public class PulseReportingSettingsTests
{
    private const string TopLevelPulse = "Server=top;Database=Pulse";
    private const string TenantPulse = "Server=acme;Database=Pulse";

    [Fact]
    public void Tenant_values_win_over_top_level()
    {
        var resolved = Resolve("acme", new()
        {
            ["Sentinel:PulseReporting:Enabled"] = "false",
            ["Sentinel:PulseReporting:CustomerId"] = "TOP",
            ["Sentinel:PulseReporting:CycleSeconds"] = "60",
            ["ConnectionStrings:Pulse"] = TopLevelPulse,
            ["Tenants:acme:Sentinel:PulseReporting:Enabled"] = "true",
            ["Tenants:acme:Sentinel:PulseReporting:CustomerId"] = "ACME",
            ["Tenants:acme:Sentinel:PulseReporting:CycleSeconds"] = "30",
            ["Tenants:acme:ConnectionStrings:Pulse"] = TenantPulse,
        });

        Assert.True(resolved.Settings.Enabled);
        Assert.Equal("ACME", resolved.Settings.CustomerId);
        Assert.Equal(30, resolved.Settings.CycleSeconds);
        Assert.Equal(TenantPulse, resolved.ConnectionString);
    }

    [Fact]
    public void Top_level_fills_keys_the_tenant_leaves_unset()
    {
        var resolved = Resolve("acme", new()
        {
            ["Sentinel:PulseReporting:CustomerId"] = "TOP",
            ["Sentinel:PulseReporting:ReassertHours"] = "12",
            ["ConnectionStrings:Pulse"] = TopLevelPulse,
            ["Tenants:acme:Sentinel:PulseReporting:Enabled"] = "true",
            ["Tenants:acme:Sentinel:PulseReporting:CustomerId"] = "",
        });

        Assert.True(resolved.Settings.Enabled);
        Assert.Equal("TOP", resolved.Settings.CustomerId);
        Assert.Equal(12, resolved.Settings.ReassertHours);
        Assert.Equal(60, resolved.Settings.CycleSeconds);
        Assert.Equal(TopLevelPulse, resolved.ConnectionString);
    }

    [Fact]
    public void Nothing_configured_resolves_disabled()
    {
        var resolved = Resolve("acme", new());

        Assert.False(resolved.Settings.Enabled);
        Assert.Null(resolved.ConnectionString);
    }

    [Fact]
    public void Enabled_with_blank_customer_throws_naming_the_tenant()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve("acme", new()
        {
            ["ConnectionStrings:Pulse"] = TopLevelPulse,
            ["Tenants:acme:Sentinel:PulseReporting:Enabled"] = "true",
            ["Tenants:acme:Sentinel:PulseReporting:CustomerId"] = "  ",
        }));

        Assert.Contains("'acme'", ex.Message);
        Assert.Contains("CustomerId", ex.Message);
    }

    [Fact]
    public void Enabled_without_connection_string_throws_naming_the_tenant()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve("acme", new()
        {
            ["Tenants:acme:Sentinel:PulseReporting:Enabled"] = "true",
            ["Tenants:acme:Sentinel:PulseReporting:CustomerId"] = "ACME",
        }));

        Assert.Contains("'acme'", ex.Message);
        Assert.Contains("connection string", ex.Message);
    }

    [Fact]
    public void Disabled_tenant_with_blank_customer_does_not_throw()
    {
        var resolved = Resolve("acme", new() { ["Sentinel:PulseReporting:Enabled"] = "false" });

        Assert.False(resolved.Settings.Enabled);
    }

    [Fact]
    public void Sentinel_context_follows_a_populated_worker_tenant_scope()
    {
        using var provider = BuildSentinelProvider(new TenantStub { Populated = true, Sentinel = "Server=acme;Database=Sentinel" });
        using var scope = provider.CreateScope();

        using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SentinelDBContext>>().CreateDbContext();

        Assert.Equal("acme", DataSource(db));
    }

    [Fact]
    public void Sentinel_context_falls_back_to_top_level_when_the_scope_is_unpopulated()
    {
        using var provider = BuildSentinelProvider(new TenantStub { Populated = false });
        using var scope = provider.CreateScope();

        using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SentinelDBContext>>().CreateDbContext();

        Assert.Equal("top", DataSource(db));
    }

    private static ResolvedPulseReporting Resolve(string subdomain, Dictionary<string, string?> values) =>
        new PulseReportingSettingsResolver(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .Resolve(subdomain);

    private static string DataSource(DbContext db) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;

    private static ServiceProvider BuildSentinelProvider(TenantStub tenant)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DXSuite:Role"] = "web",
            ["ConnectionStrings:Sentinel"] = "Server=top;Database=Sentinel",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ITenantContext>(_ => tenant);
        services.AddSentinelModule(config);
        return services.BuildServiceProvider();
    }

    private sealed class TenantStub : ITenantContext
    {
        public bool Populated { get; init; }
        public string? Sentinel { get; init; }

        public bool IsPopulated => Populated;
        public string TenantId => "acme";
        public string Subdomain => "acme";
        public string CustomerCode => "acme";
        public string DisplayName => "acme";
        public string? DirectorConnectionString => null;
        public string? SentinelConnectionString => Sentinel;
        public string? CoreDataConnectionString => null;
        public string? AviationConnectionString => null;
        public string? DesignConnectionString => null;
        public TenantAuthConfig AuthConfig => new();
        public TenantModuleConfig Modules => new();
        public TenantFeatureFlags Features => new();
        public string MediaRootPath => "";
        public string? SiteUrl => null;
    }
}
