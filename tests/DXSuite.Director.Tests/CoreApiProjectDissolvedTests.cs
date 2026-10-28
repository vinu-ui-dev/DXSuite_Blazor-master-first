using System.Text.Json;
using DXSuite.Connectors;
using DXSuite.Host.Multitenancy;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Models;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Tests;

// Section numbers in this file refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>
/// DEV-09 (B-06): the ported CoreAPI project is gone. The Flight Data Editor is Aviation's, the key page and the
/// editor's field settings are one Connectors settings tab behind config-tab:coreapi-connection (CAPI-10, FDE-01),
/// and the process-wide switches that stood in for tenant gating are deleted.
/// </summary>
public class CoreApiProjectDissolvedTests
{
    private static readonly TenantFeatureFlags EditorOn = new() { Aviation = new AviationFeatureFlags { FlightDataEditor = true } };

    [Fact]
    public void The_Host_ships_neither_the_CoreAPI_project_nor_Radzen()
    {
        // The Host's deps file lists every assembly it loads, its own projects and packages included.
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DXSuite.Host.deps.json")));
        var libraries = deps.RootElement.GetProperty("libraries").EnumerateObject().Select(l => l.Name).ToList();

        Assert.Contains(libraries, l => l.StartsWith("DXSuite.Aviation/", StringComparison.Ordinal));
        Assert.DoesNotContain(libraries, l => l.StartsWith("DXSuite.CoreAPI/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(libraries, l => l.StartsWith("Radzen", StringComparison.OrdinalIgnoreCase));
    }

    // No tenant can load a Radzen script, or the project's vendored Bootstrap, when the Host serves none.
    [Fact]
    public void The_Host_serves_no_Radzen_script_and_no_vendored_CoreAPI_assets()
    {
        var assets = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DXSuite.Host.staticwebassets.endpoints.json"));

        Assert.Contains("_content/DXSuite.Director/", assets);
        Assert.DoesNotContain("Radzen", assets, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_content/DXSuite.CoreAPI/", assets, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Host_settings_no_longer_carry_the_CoreAPI_module_or_the_Messages_switches()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build();

        Assert.True(config.GetSection("Modules:Director").Exists());
        Assert.False(config.GetSection("Modules:CoreAPI").Exists());
        Assert.False(config.GetSection("Messages:ActiveMessages").Exists());
        Assert.False(config.GetSection("Messages:ZoneBuilder").Exists());
    }

    // DEV-06: Messaging's tenant flag and page:messages decide; Active Messages has no host switch. Display Zones
    // are edited on Configuration, not on Messages (ZONE-11).
    [Fact]
    public void The_Messages_page_tabs_follow_no_host_switch_and_hold_no_Display_Zones_tab()
    {
        var tabs = Pages.Messages.Messages.TabsFor([], [], gtaaMessaging: false);

        Assert.Equal(["ActiveMessages"], tabs.Select(t => t.Key));
    }

    [Fact]
    public async Task Settings_offers_the_connection_tab_to_a_role_holding_its_config_tab_key()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RenderSettingsAsync(new KeySession(PermissionKeys.ConfigTabCoreApiConnection), EditorOn));

        // The tab rendered and began reading the tenant's CoreAPI connection.
        Assert.StartsWith(ThrowingProxy.Loaded + "ICoreApiSettingsStore", ex.Message);
    }

    [Fact]
    public async Task Settings_does_not_offer_the_connection_tab_for_a_settings_tab_key_of_the_same_name()
    {
        var rendered = await RenderSettingsAsync(new KeySession("settings-tab:coreapi-connection"), EditorOn);

        Assert.Contains("No Settings Available", rendered);
    }

    [Fact]
    public async Task Settings_offers_no_connection_tab_to_a_tenant_without_the_editor_even_for_Admin()
    {
        var rendered = await RenderSettingsAsync(new KeySession((int)Roles.Role.Admin), new TenantFeatureFlags());

        Assert.Contains("No Settings Available", rendered);
    }

    // The one entry is the config-tab key: a role granted it shows the box ticked.
    [Fact]
    public async Task The_Roles_tab_lists_the_CoreAPI_connection_once_under_its_config_tab_key()
    {
        var page = typeof(DXSuite.Shared.Components.Settings.RolesListView);
        var services = PermissionGatedPagesTests.ThrowingServices(page, new KeySession((int)Roles.Role.Admin), s =>
        {
            AddTenantAndModules(s, EditorOn);
            s.AddSingleton<IRolePermissionService>(new OneRole(PermissionKeys.ConfigTabCoreApiConnection));
        });

        var rendered = await PageRenderer.RenderAsync(services, page);

        Assert.Contains("Settings Tabs", rendered);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(rendered, "CoreAPI Connection"));
        Assert.Matches(@"\[checked\]\s*CoreAPI Connection", rendered);
    }

    private static Task<string> RenderSettingsAsync(KeySession session, TenantFeatureFlags features)
    {
        var page = typeof(DXSuite.Host.Components.Pages.Settings);
        var services = PermissionGatedPagesTests.ThrowingServices(page, session, s => AddTenantAndModules(s, features));
        return PageRenderer.RenderAsync(services, page);
    }

    private static void AddTenantAndModules(IServiceCollection services, TenantFeatureFlags features)
    {
        var tenant = new TenantContext();
        tenant.Populate(new TenantRecord
        {
            TenantId = "t1",
            Subdomain = "acme",
            CustomerCode = "ACME",
            DisplayName = "Acme",
            Features = features,
        });
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IReadOnlyList<IBlazorModule>>([new ConnectorsModule()]);
        services.AddSingleton<IOptions<FeatureFlags>>(Options.Create(new FeatureFlags()));
    }

    /// <summary>One custom role holding the given keys; the Roles tab reads no more than this to render.</summary>
    private sealed class OneRole(params string[] keys) : IRolePermissionService
    {
        public Task<List<TblUserRole>> GetAllRolesAsync() =>
            Task.FromResult(new List<TblUserRole>
            {
                new()
                {
                    RoleId = 7, RoleName = "Operator", BaseRoleId = (int)Roles.Role.Contributor,
                    Permissions = keys.Select(k => new TblRolePermission { RoleId = 7, PermissionKey = k }).ToList(),
                },
            });
        public Task<Dictionary<int, int>> GetAssignedUserCountsAsync() => Task.FromResult(new Dictionary<int, int>());
        public Task<TblUserRole?> GetRoleAsync(int roleId) => throw new NotSupportedException();
        public Task<int> GetAssignedUserCountAsync(int roleId) => throw new NotSupportedException();
        public Task<TblUserRole> CreateRoleAsync(string roleName, string? description, int baseRoleId, IEnumerable<string> permissionKeys) => throw new NotSupportedException();
        public Task UpdateRoleAsync(int roleId, string roleName, string? description, int? baseRoleId, IEnumerable<string> permissionKeys) => throw new NotSupportedException();
        public Task<RoleDeleteResult> DeleteRoleAsync(int roleId) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetPermissionsForRoleAsync(int roleId) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(int roleId, string permissionKey) => throw new NotSupportedException();
        public Task<Roles.Role> GetEffectiveRoleAsync(int roleId) => throw new NotSupportedException();
    }
}
