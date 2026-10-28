namespace DXSuite.Shared.Models;

public class FeatureFlags
{
    public bool Wayfinding { get; set; }
    public bool GroupDiscovery { get; set; }
    public bool DomainGroupAssignment { get; set; }
    public bool Sentinel { get; set; }
    public bool Engage { get; set; }
    public bool ContentPackage { get; set; }

    /// <summary>
    /// Allows the media file resolver to fall back to the pre-multi-tenant flat folder
    /// layout (MediaAssets/{locationId}/) when the tenant-scoped path does not exist.
    /// Enable only on on-premises single-tenant deployments being migrated to the
    /// tenant-prefixed layout. Must remain disabled in hosted multi-tenant environments
    /// to prevent cross-tenant path collisions.
    /// </summary>
    public bool LegacyMediaFolderFallback { get; set; }

    /// <summary>
    /// Controls visibility of the Overrides nav item and pages.
    /// Disable on deployments where override management is handled externally or not needed.
    /// </summary>
    public bool Overrides { get; set; }

    /// <summary>
    /// Controls visibility of the Counter Scheduler nav item and pages.
    /// Disable on deployments that do not use airport ticket counter scheduling.
    /// </summary>
    public bool CounterScheduler { get; set; }

    /// <summary>
    /// Enables uploadable location bundles: the Location Bundles config tab, the bundle
    /// management/serving endpoints, and the Custom / Custom Display location types.
    /// </summary>
    public bool LocationBundles { get; set; }

    /// <summary>
    /// Enables panels: anonymous touch control surfaces that serve a bundle's files outside
    /// the Blazor app and let an unauthenticated caller assign content to one location's
    /// displays. Panels reuse bundle storage for their files but not the bundle runtime, so
    /// this flag is independent of <see cref="LocationBundles"/>.
    /// </summary>
    public bool Panels { get; set; }
}
