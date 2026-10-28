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
}
