using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.LocationBundles;

public interface ILocationBundleService
{
    Task<List<LocationBundleInfo>> GetBundlesAsync(CancellationToken ct = default);

    /// <summary>
    /// Parsed manifest for a bundle version. Null version resolves the bundle's CurrentVersion.
    /// </summary>
    Task<LocationBundleManifest?> GetManifestAsync(string bundleKey, string? version = null, CancellationToken ct = default);

    /// <summary>
    /// Effective version a location assignment resolves to: the pinned version when set
    /// and it still exists, otherwise the bundle's CurrentVersion. Null when the bundle
    /// or version is missing or disabled.
    /// </summary>
    Task<LocationBundleResolvedVersion?> ResolveVersionAsync(string bundleKey, string? pinnedVersion, CancellationToken ct = default);

    Task<LocationBundleOperationResult> SetCurrentVersionAsync(string bundleKey, string version, string? initiatedBy, CancellationToken ct = default);

    /// <summary>Fails with the referencing locations listed when any location still uses the bundle.</summary>
    Task<LocationBundleOperationResult> DeleteBundleAsync(string bundleKey, string? initiatedBy, CancellationToken ct = default);

    /// <summary>Fails when the version is the bundle's CurrentVersion or is pinned by a location.</summary>
    Task<LocationBundleOperationResult> DeleteVersionAsync(string bundleKey, string version, string? initiatedBy, CancellationToken ct = default);

    Task<List<LocationBundleReference>> GetReferencingLocationsAsync(string bundleKey, CancellationToken ct = default);

    Task<List<TblLocationBundleHistory>> GetHistoryAsync(int take = 100, CancellationToken ct = default);
}

public class LocationBundleInfo
{
    public TblLocationBundle Bundle { get; set; } = null!;
    public List<TblLocationBundleVersion> Versions { get; set; } = new();
    public List<LocationBundleReference> ReferencedBy { get; set; } = new();
}

public class LocationBundleReference
{
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string? PinnedVersion { get; set; }
}

public class LocationBundleResolvedVersion
{
    public string Version { get; set; } = string.Empty;
    public string EntryPath { get; set; } = string.Empty;
    public LocationBundleManifest? Manifest { get; set; }
}

public class LocationBundleOperationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<LocationBundleReference> ReferencingLocations { get; set; } = new();

    public static LocationBundleOperationResult Ok() => new() { Success = true };
    public static LocationBundleOperationResult Fail(string error) => new() { Error = error };
}
