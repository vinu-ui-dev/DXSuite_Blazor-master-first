using System.Reflection;
using DXSuite.Director.Services;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Module;
using DXSuite.Shared.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director;

public class DirectorModule : IBlazorModule
{
    public string Name => "Director";

    public string Version => "2026.05";

    public Assembly Assembly => typeof(DirectorModule).Assembly;

    public bool IsAvailableForTenant(ITenantContext tenant) => tenant.Modules.Director;

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddDirectorModule(config);
    }

    public IEnumerable<NavItem> GetNavItems()
    {
        yield return new NavItem("Dashboard", "/director", "bi-speedometer2", 10, "Director");
        // Schedule and Media Creator are omitted — they require workflow context
        // (e.g. display selection) and are navigated to from within other pages
        yield return new NavItem("Overrides", "/overrides", "bi-lightning", 12, "Director", Feature: "Overrides");
        yield return new NavItem("Approvals", "/approvals", "bi-check2-square", 13, "Director");
        yield return new NavItem("Counter Scheduler", "/ticket-counter-scheduler", "bi-grid-3x3", 14, "Director", Feature: "CounterScheduler");
        yield return new NavItem("Content Log", "/content-log", "bi-journal-text", 15, "Director", Permission: PermissionKeys.PageContentLog);
    }

    public IEnumerable<ConfigTab> GetConfigTabs()
    {
        yield return new ConfigTab("devices", "Devices", typeof(ConfigTabs.DevicesTab), 10);
        yield return new ConfigTab("locations", "Locations", typeof(ConfigTabs.LocationsTab), 15);
        yield return new ConfigTab("sites", "Sites", typeof(ConfigTabs.SitesTab), 16);
        yield return new ConfigTab("content-channels", "Content Channels", typeof(ConfigTabs.ContentChannelsTab), 17);
        yield return new ConfigTab("director-urls", "Director URLs", typeof(ConfigTabs.DirectorUrlsTab), 18);
        yield return new ConfigTab("metadata", "Metadata", typeof(ConfigTabs.MetadataTab), 20);
        yield return new ConfigTab("llm-providers", "LLM Providers", typeof(Components.Settings.LlmProvidersTab), 25);
        yield return new ConfigTab("cloud-connectors", "Cloud Connectors", typeof(ConfigTabs.CloudConnectorsTab), 30);
        yield return new ConfigTab("content-package", "Content Packages", typeof(ConfigTabs.ContentPackageTab), 35, Feature: "ContentPackage");
        yield return new ConfigTab("location-bundles", "Location Bundles", typeof(ConfigTabs.LocationBundlesTab), 36, Feature: "LocationBundles");
        yield return new ConfigTab("panels", "Panels", typeof(ConfigTabs.PanelsTab), 37, Feature: "Panels");
    }

    public IEnumerable<SettingsTab> GetSettingsTabs()
    {
        // Admin-only tabs (default MinimumRole = Admin)
        yield return new SettingsTab("users", "Users", typeof(DXSuite.Shared.Components.Settings.UsersListView), 10);
        yield return new SettingsTab("groups", "Groups", typeof(DXSuite.Director.Components.Settings.DirectorGroupsView), 20);
        yield return new SettingsTab("roles", "Roles", typeof(DXSuite.Shared.Components.Settings.RolesListView), 25);
        yield return new SettingsTab("branding", "Branding", typeof(DXSuite.Shared.Components.Settings.Branding), 30, TenantFeature: "BrandingManagement");
        yield return new SettingsTab("apikeys", "API Keys", typeof(Components.Settings.ApiKeys), 40);
        yield return new SettingsTab("smtp", "SMTP", typeof(Components.Settings.SmtpSettings), 50);
        yield return new SettingsTab("devicepairing", "Device Pairing", typeof(Components.Settings.DevicePairing), 55, Feature: "Engage");
        // Order 57 puts it between Device Pairing (55) and Player Updates (60), so the tab order
        // reads as the real lifecycle: provision, pair, update.
        yield return new SettingsTab("playerprovisioning", "Player Provisioning", typeof(Components.Settings.PlayerProvisioning), 57, Feature: "Engage");
        yield return new SettingsTab("playerupdates", "Player Updates", typeof(Components.Settings.PlayerUpdates), 60, Feature: "Engage");
        yield return new SettingsTab("playersync", "Player Sync", typeof(Components.Settings.PlayerSync), 62, Feature: "Engage");
        yield return new SettingsTab("airlines", "Airlines", typeof(Components.Settings.Airlines), 70, Module: "Aviation");
        yield return new SettingsTab("cities", "Cities", typeof(Components.Settings.Cities), 80, Module: "Aviation");
        yield return new SettingsTab("wayfinding", "Wayfinding", typeof(Components.Settings.Wayfinding), 90, Feature: "Wayfinding");
        yield return new SettingsTab("content-package-envs", "Content Package Environments", typeof(Components.Settings.ContentPackageEnvironments), 95, Feature: "ContentPackage");
        yield return new SettingsTab("systeminfo", "System Info", typeof(Components.Settings.SystemInfo), 110);
        yield return new SettingsTab("vendoraccess", "Vendor Access Log", typeof(Components.Settings.VendorAccessLog), 120, RequiresPlatform: true);

        // Contributor-accessible tabs
        yield return new SettingsTab("notifications", "Notifications", typeof(DXSuite.Shared.Components.Settings.NotificationPreferences), 5, Roles.Role.Contributor);
    }

    /// <summary>
    /// Every location is a valid landing page. Deliberately not filtered by the selected user's
    /// access: Users is an admin page and the natural order is to assign the landing location,
    /// then grant access to it.
    /// </summary>
    public async Task<IEnumerable<LandingPageOption>> GetLandingPageOptionsAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<Entities.IDirectorDBContext>();

        var locations = await db.TblLocations
            .AsNoTracking()
            .OrderBy(l => l.LocationName)
            .Select(l => new { l.LocationId, l.LocationName })
            .ToListAsync();

        return locations.Select(l =>
            new LandingPageOption(l.LocationName, $"/location/{l.LocationId}", "Locations"));
    }

    public Type? GetHomeDashboardComponent() => typeof(Components.Home.DirectorDashboardCard);

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IApplicationBuilder app)
    {
        // Auth is handled per-endpoint via [Authorize(AuthenticationSchemes = AuthSchemes.DeviceToken)]
        // and [Authorize(AuthenticationSchemes = AuthSchemes.AdminApiKey)] on controller actions.
    }
}
