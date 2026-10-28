namespace DXSuite.Director.Services.LocationBundles;

/// <summary>
/// Bound from the "LocationBundles" configuration section.
/// </summary>
public class LocationBundleOptions
{
    /// <summary>
    /// Enables per-location dev-override proxying (serving bundle files from an
    /// engineer's local static server). Must remain false in production — the gate is
    /// environment-level by design so unvetted content can never be proxied there.
    /// </summary>
    public bool AllowDevOverride { get; set; }

    public long MaxZipBytes { get; set; } = 100L * 1024 * 1024;

    public long MaxUncompressedBytes { get; set; } = 300L * 1024 * 1024;

    public int MaxFileCount { get; set; } = 2000;
}
