using System.Collections.Generic;
using System.Text.Json;

namespace DXSuite.Director.Models.LocationBundles;

/// <summary>
/// Per-location bundle assignment, stored inside TblLocation.ExtendedData as the
/// "bundle" property. References bundles by key (not PK) so assignments survive
/// content-package export/import across environments.
/// </summary>
public class LocationBundleAssignment
{
    public string BundleKey { get; set; } = string.Empty;

    /// <summary>
    /// Null tracks the bundle's CurrentVersion; a value pins this location to a
    /// specific uploaded version.
    /// </summary>
    public string? PinnedVersion { get; set; }

    /// <summary>
    /// Bundle-defined settings, validated against the manifest's settingsSchema and
    /// exposed to the bundle via the Location Content API context endpoint.
    /// </summary>
    public Dictionary<string, JsonElement>? Settings { get; set; }

    /// <summary>
    /// Dev-mode only: base URL of an engineer's local static server. The serving
    /// controller proxies file requests here instead of the bundle store — but only
    /// when LocationBundles:AllowDevOverride is enabled AND the caller is an Admin.
    /// </summary>
    public string? DevOverrideUrl { get; set; }
}
