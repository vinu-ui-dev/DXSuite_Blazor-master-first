using System.Net;
using DXSuite.Director.Models;
using DXSuite.Director.Services;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

public class OutboundUrlGuardTests
{
    private static OutboundUrlGuard CreateGuard(bool allowPrivate = false, params string[] allowList) =>
        new(Options.Create(new WebPreviewOptions
        {
            AllowPrivateNetworks = allowPrivate,
            OutboundAllowList = allowList
        }), NullLogger<OutboundUrlGuard>.Instance, new StubTenantStore(tenantCount: 1));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("168.63.129.16")]   // Azure Wire Server — public-looking, host-internal
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a00:1")]  // NAT64-embedded 10.0.0.1
    [InlineData("::0a00:0001")]     // IPv4-compatible 10.0.0.1
    [InlineData("2002:0a00:0001::")]// 6to4-embedded 10.0.0.1
    [InlineData("2001::1")]         // Teredo
    public void Blocks_loopback_linklocal_and_private_addresses(string address) =>
        Assert.False(CreateGuard().IsAllowedAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("168.63.129.15")]
    [InlineData("2606:4700::1111")]
    [InlineData("2001:4860:4860::8888")] // 2001:4860 is global unicast, not Teredo (2001:0000::/32)
    public void Allows_public_addresses(string address) =>
        Assert.True(CreateGuard().IsAllowedAddress(IPAddress.Parse(address)));

    [Fact]
    public void AllowPrivateNetworks_disables_all_blocking()
    {
        var guard = CreateGuard(allowPrivate: true);
        Assert.True(guard.IsAllowedAddress(IPAddress.Parse("127.0.0.1")));
        Assert.True(guard.IsAllowedAddress(IPAddress.Parse("169.254.169.254")));
    }

    [Fact]
    public void Cidr_allowlist_admits_only_the_listed_range()
    {
        var guard = CreateGuard(false, "10.20.0.0/16");
        Assert.True(guard.IsAllowedAddress(IPAddress.Parse("10.20.5.6")));
        Assert.False(guard.IsAllowedAddress(IPAddress.Parse("10.21.0.1")));
        Assert.False(guard.IsAllowedAddress(IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void Cidr_allowlist_matches_ipv4_mapped_ipv6_form()
    {
        var guard = CreateGuard(false, "10.20.0.0/16");
        Assert.True(guard.IsAllowedAddress(IPAddress.Parse("::ffff:10.20.5.6")));
    }

    [Fact]
    public void Host_allowlist_matches_exact_and_wildcard_entries()
    {
        var guard = CreateGuard(false, "dash.corp.local", "*.internal.example");
        Assert.True(guard.IsHostAllowListed("dash.corp.local"));
        Assert.True(guard.IsHostAllowListed("DASH.CORP.LOCAL"));
        Assert.True(guard.IsHostAllowListed("a.internal.example"));
        Assert.False(guard.IsHostAllowListed("internal.example"));
        Assert.False(guard.IsHostAllowListed("other.example"));
        Assert.False(guard.IsHostAllowListed("dash.corp.local.evil.com"));
    }

    /// <summary>
    /// The shared-deployment refusal is what EgressFilteringProxy lifted: the browser's own loads
    /// are filtered now, so a tenant admin's URL can no longer read an internal endpoint.
    /// </summary>
    [Fact]
    public async Task Browser_capture_is_allowed_when_multiple_tenants_are_configured()
    {
        var guard = new OutboundUrlGuard(
            Options.Create(new WebPreviewOptions()),
            NullLogger<OutboundUrlGuard>.Instance,
            new StubTenantStore(3));
        Assert.True(await guard.IsBrowserCaptureAllowedAsync());
    }

    [Fact]
    public async Task Browser_capture_is_allowed_for_a_single_tenant()
    {
        var guard = new OutboundUrlGuard(
            Options.Create(new WebPreviewOptions()),
            NullLogger<OutboundUrlGuard>.Instance,
            new StubTenantStore(1));
        Assert.True(await guard.IsBrowserCaptureAllowedAsync());
    }

    /// <summary>
    /// AllowPrivateNetworks disarms the filter, so in a shared deployment it would hand every
    /// tenant admin an unfiltered browser. That combination is documented as unsupported and
    /// stays refused rather than honoured.
    /// </summary>
    [Fact]
    public async Task Browser_capture_is_refused_when_private_networks_are_permitted_in_a_shared_deployment()
    {
        var guard = new OutboundUrlGuard(
            Options.Create(new WebPreviewOptions { AllowPrivateNetworks = true }),
            NullLogger<OutboundUrlGuard>.Instance,
            new StubTenantStore(5));
        Assert.False(await guard.IsBrowserCaptureAllowedAsync());
    }

    [Fact]
    public async Task Browser_capture_is_allowed_when_private_networks_are_permitted_on_a_single_tenant()
    {
        var guard = new OutboundUrlGuard(
            Options.Create(new WebPreviewOptions { AllowPrivateNetworks = true }),
            NullLogger<OutboundUrlGuard>.Instance,
            new StubTenantStore(1));
        Assert.True(await guard.IsBrowserCaptureAllowedAsync());
    }

    [Fact]
    public async Task Browser_preflight_blocks_private_ip_literals()
    {
        var guard = CreateGuard();
        var (allowed, rule) = await guard.EvaluateForBrowserCaptureAsync(new Uri("http://192.168.1.50/dashboard"));
        Assert.False(allowed);
        Assert.Null(rule);
    }

    [Fact]
    public async Task Browser_preflight_allows_public_ip_literals_without_pinning()
    {
        var guard = CreateGuard();
        var (allowed, rule) = await guard.EvaluateForBrowserCaptureAsync(new Uri("http://93.184.216.34/"));
        Assert.True(allowed);
        Assert.Null(rule);
    }

    [Fact]
    public async Task Browser_preflight_skips_validation_for_allowlisted_hosts()
    {
        var guard = CreateGuard(false, "dash.corp.local");
        var (allowed, rule) = await guard.EvaluateForBrowserCaptureAsync(new Uri("http://dash.corp.local/"));
        Assert.True(allowed);
        Assert.Null(rule);
    }

}

internal sealed class StubTenantStore : ITenantStore
{
    private readonly IReadOnlyList<TenantRecord> _tenants;

    public StubTenantStore(int tenantCount = 1) =>
        _tenants = Enumerable.Range(0, tenantCount)
            .Select(i => new TenantRecord { TenantId = $"t{i}", Subdomain = $"t{i}" })
            .ToList();

    public Task<IReadOnlyList<TenantRecord>> GetAllEnabledAsync(CancellationToken ct = default) =>
        Task.FromResult(_tenants);

    public Task<TenantRecord?> FindBySubdomainAsync(string subdomain, CancellationToken ct = default) =>
        Task.FromResult(_tenants.FirstOrDefault(t => t.Subdomain == subdomain));

    public Task<TenantRecord?> GetDefaultAsync(CancellationToken ct = default) =>
        Task.FromResult(_tenants.FirstOrDefault());

    public Task<TenantRecord?> FindTenantByApiKeyHashAsync(string keyHash, CancellationToken ct = default) =>
        Task.FromResult<TenantRecord?>(null);
}
