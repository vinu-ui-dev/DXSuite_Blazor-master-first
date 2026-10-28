using System.Text.Json;
using DXSuite.Aviation;
using DXSuite.Connectors;
using DXSuite.Host.Multitenancy;
using DXSuite.Shared.Audit;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DXSuite.Director.Tests;

/// <summary>
/// Messaging and the Flight Data Editor are product capabilities, each behind one per-tenant flag that is off
/// unless provisioning turns it on (DEV-06, DEV-07). With a flag off the tenant sees none of the capability's
/// surface: nav item, Configuration tab or route.
/// </summary>
public class MessagingAndFlightDataEditorGateTests
{
    [Fact]
    public void Both_capabilities_are_off_for_a_tenant_that_names_neither()
    {
        var flags = new TenantFeatureFlags();

        Assert.False(flags.IsEnabled("Messaging"));
        Assert.False(flags.IsEnabled("Aviation.FlightDataEditor"));
    }

    [Fact]
    public void Each_flag_turns_on_only_its_own_capability()
    {
        var messagingOnly = new TenantFeatureFlags { Messaging = true };
        var editorOnly = new TenantFeatureFlags { Aviation = new AviationFeatureFlags { FlightDataEditor = true } };

        Assert.True(messagingOnly.IsEnabled("Messaging"));
        Assert.False(messagingOnly.IsEnabled("Aviation.FlightDataEditor"));
        Assert.True(editorOnly.IsEnabled("Aviation.FlightDataEditor"));
        Assert.False(editorOnly.IsEnabled("Messaging"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_Messages_nav_item_shows_exactly_when_Messaging_is_on(bool on)
    {
        var item = new DirectorModule().GetNavItems().Single(n => n.Href == "/messages");

        Assert.Equal(on, new TenantFeatureFlags { Messaging = on }.IsEnabled(item.Feature));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_Message_Playback_Log_nav_item_shows_exactly_when_Messaging_is_on(bool on)
    {
        var item = new DirectorModule().GetNavItems().Single(n => n.Href == Pages.Reports.MessagePlaybackLog.Route);

        Assert.Equal(on, new TenantFeatureFlags { Messaging = on }.IsEnabled(item.Feature));
    }

    // The User Event Log names Activations whatever the flag; their link opens the Message Playback Log, which is Messaging's.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_Activation_target_links_to_the_Message_Playback_Log_exactly_when_Messaging_is_on(bool on)
    {
        var link = DXSuite.Director.Services.Reports.UserEventRecordLinks.For(UserEventTargetTypes.Activation, "10471")!;

        Assert.Equal(on, link.OpensFor(Tenant(new TenantFeatureFlags { Messaging = on })));
        Assert.True(DXSuite.Director.Services.Reports.UserEventRecordLinks.For(UserEventTargetTypes.Override, "7")!
            .OpensFor(Tenant(new TenantFeatureFlags())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_Flight_Data_Editor_nav_item_shows_exactly_when_its_Aviation_flag_is_on(bool on)
    {
        var item = new AviationModule().GetNavItems().Single(n => n.Href == "/Flight-Data-Editor");
        var flags = new TenantFeatureFlags { Aviation = new AviationFeatureFlags { FlightDataEditor = on } };

        Assert.Equal(on, flags.IsEnabled(item.Feature));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configuration_offers_Message_Types_exactly_when_Messaging_is_on(bool on)
    {
        var tenant = Tenant(new TenantFeatureFlags { Messaging = on });

        var keys = TenantTabs.ConfigTabs([new DirectorModule()], tenant).Select(t => t.Key).ToList();

        Assert.Equal(on, keys.Contains(PermissionKeys.MessageTypesConfigTabKey));
        Assert.Contains("devices", keys);
    }

    // ZONE-11: Display Zones is a Configuration tab behind config-tab:zone-builder and the Messaging flag.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configuration_offers_Display_Zones_exactly_when_Messaging_is_on(bool on)
    {
        var tenant = Tenant(new TenantFeatureFlags { Messaging = on });

        var tab = TenantTabs.ConfigTabs([new DirectorModule()], tenant).SingleOrDefault(t => t.Key == PermissionKeys.ZoneBuilderConfigTabKey);

        Assert.Equal(on, tab is not null);
        if (tab is null) return;
        Assert.Equal(("Display Zones", typeof(ConfigTabs.DisplayZonesTab), "config-tab:zone-builder"), (tab.Label, tab.ComponentType, tab.PermissionKey));
    }

    // In 1.1 the Flight Data Editor is the only capability that talks to CoreAPI, so its connection tab follows the
    // editor's flag (DEV-07: off unless provisioned).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Settings_offers_the_CoreAPI_connection_tab_exactly_when_the_Flight_Data_Editor_is_on(bool on)
    {
        var tenant = Tenant(new TenantFeatureFlags { Aviation = new AviationFeatureFlags { FlightDataEditor = on } });

        var keys = TenantTabs.SettingsTabs([new ConnectorsModule()], tenant).Select(t => t.Key).ToList();

        Assert.Equal(on, keys.Contains(PermissionKeys.CoreApiConnectionConfigTabKey));
    }

    [Fact]
    public void A_module_unavailable_to_the_tenant_contributes_no_settings_tabs()
    {
        var tenant = Tenant(new TenantFeatureFlags(), new TenantModuleConfig { Director = false });

        Assert.Empty(TenantTabs.SettingsTabs([new DirectorModule()], tenant));
    }

    [Fact]
    public void A_settings_tab_behind_an_off_tenant_flag_is_not_offered()
    {
        var off = TenantTabs.SettingsTabs([new DirectorModule()], Tenant(new TenantFeatureFlags())).Select(t => t.Key);
        var on = TenantTabs.SettingsTabs([new DirectorModule()], Tenant(new TenantFeatureFlags { BrandingManagement = true })).Select(t => t.Key);

        Assert.DoesNotContain("branding", off);
        Assert.Contains("branding", on);
        Assert.Contains("users", off);
    }

    [Fact]
    public void A_tenant_not_yet_resolved_is_offered_no_tabs()
    {
        var unresolved = new TenantContext();

        Assert.Empty(TenantTabs.ConfigTabs([new DirectorModule()], unresolved));
        Assert.Empty(TenantTabs.SettingsTabs([new DirectorModule()], unresolved));
    }

    [Fact]
    public void A_hosted_tenant_turns_both_on_in_its_stored_feature_set()
    {
        // The shape of tblTenant.FeaturesJson, as SqlTenantStore reads it on each refresh.
        var flags = JsonSerializer.Deserialize<TenantFeatureFlags>(
            """{ "Messaging": true, "Aviation": { "FlightDataEditor": true } }""")!;

        Assert.True(flags.IsEnabled("Messaging"));
        Assert.True(flags.IsEnabled("Aviation.FlightDataEditor"));
    }

    [Fact]
    public void A_stored_feature_set_with_no_Aviation_flags_leaves_the_editor_off()
    {
        var flags = JsonSerializer.Deserialize<TenantFeatureFlags>("""{ "Messaging": true, "Aviation": null }""")!;

        Assert.False(flags.IsEnabled("Aviation.FlightDataEditor"));
    }

    [Fact]
    public async Task A_standalone_tenant_turns_both_on_in_its_appsettings_Features_section()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:acme:Features:Messaging"] = "true",
            ["Tenants:acme:Features:Aviation:FlightDataEditor"] = "true",
            ["Tenants:plain:DisplayName"] = "Plain",
        }).Build();
        var store = new ConfigTenantStore(config);

        var acme = (await store.FindBySubdomainAsync("acme"))!.Features;
        var plain = (await store.FindBySubdomainAsync("plain"))!.Features;

        Assert.True(acme.IsEnabled("Messaging"));
        Assert.True(acme.IsEnabled("Aviation.FlightDataEditor"));
        Assert.False(plain.IsEnabled("Messaging"));
        Assert.False(plain.IsEnabled("Aviation.FlightDataEditor"));
    }

    private static TenantContext Tenant(TenantFeatureFlags features, TenantModuleConfig? modules = null)
    {
        var tenant = new TenantContext();
        tenant.Populate(new TenantRecord
        {
            TenantId = "t1",
            Subdomain = "acme",
            CustomerCode = "ACME",
            DisplayName = "Acme",
            Modules = modules ?? new TenantModuleConfig(),
            Features = features,
        });
        return tenant;
    }
}
