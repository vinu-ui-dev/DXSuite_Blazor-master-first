namespace DXSuite.Shared.Multitenancy;

public class TenantFeatureFlags
{
    public bool Engage { get; init; } = false;
    public bool Wayfinding { get; init; } = false;
    public bool ContentPackage { get; init; } = false;
    public bool GroupDiscovery { get; init; } = false;
    public bool DomainGroupAssignment { get; init; } = false;

    /// <summary>
    /// Controls visibility of the Overrides nav item. Defaults to true so existing
    /// tenants are unaffected; set to false per-tenant to hide override management.
    /// </summary>
    public bool Overrides { get; init; } = true;

    /// <summary>
    /// Controls visibility of the Counter Scheduler nav item. Defaults to true;
    /// set to false per-tenant on deployments that don't use counter scheduling.
    /// </summary>
    public bool CounterScheduler { get; init; } = true;

    /// <summary>
    /// Grants access to the Branding settings tab. Branding images are server-global
    /// (shared login page, header, favicon), so on shared multi-tenant deployments
    /// exactly one owner tenant should hold this flag; all others keep the default.
    /// </summary>
    public bool BrandingManagement { get; init; } = false;

    /// <summary>
    /// Controls the Messages page, its config tabs and the Messaging delivery/trigger API
    /// (GTAA FSD 1.1 MSG/ZONE). Off by default — a tenant without Messaging sees no messaging
    /// screen, menu or route.
    /// </summary>
    public bool Messaging { get; init; } = false;

    /// <summary>
    /// Controls the Flight Data Editor page (GTAA FSD 1.1 FDE) and the CoreAPI watcher that backs
    /// it. Off by default — a tenant without it sees no editor and runs no CoreAPI watcher.
    /// </summary>
    public bool FlightDataEditor { get; init; } = false;

    /// <summary>
    /// Controls visibility of the Pulse top-nav item. Defaults to true so existing tenants are
    /// unaffected; set to false on deployments (e.g. GTAA/Aviation) with no Pulse signage use case.
    /// </summary>
    public bool ShowPulseNav { get; init; } = true;

    /// <summary>
    /// Controls visibility of the Sentinel top-nav group (Dashboard, Automation). Defaults to
    /// true; set to false on deployments that don't operate Sentinel-managed devices. Unlike the
    /// Sentinel module flag, this only hides the nav — Sentinel's Pulse Reporting Configuration
    /// tab keeps working, since it has no device-ops dependency.
    /// </summary>
    public bool ShowSentinelNav { get; init; } = true;

    /// <summary>
    /// Controls visibility of the Content Log nav item, in addition to its existing
    /// PageContentLog permission gate — the Admin role bypasses permission checks entirely,
    /// so a Feature flag is the only way to hide it from Admin too.
    /// Defaults to true; set to false on deployments that don't use the content log.
    /// </summary>
    public bool ShowContentLogNav { get; init; } = true;

    /// <summary>
    /// Controls whether the Platform module appears in the Home dashboard's Active Modules
    /// card. Defaults to true; set to false on deployments where tenant administration isn't
    /// a user-facing concern (e.g. a single-tenant GTAA/Aviation demo).
    /// </summary>
    public bool ShowPlatformModuleCard { get; init; } = true;

    /// <summary>Looks up a flag by its property name (e.g. <c>nameof(Messaging)</c>), false when unknown.</summary>
    public bool IsEnabled(string featureName) =>
        typeof(TenantFeatureFlags).GetProperty(featureName)?.GetValue(this) is true;
}
