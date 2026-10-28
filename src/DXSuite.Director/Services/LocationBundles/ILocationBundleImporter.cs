using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DXSuite.Director.Services.LocationBundles;

public interface ILocationBundleImporter
{
    /// <summary>
    /// Validates and installs a location bundle zip. Never throws for content problems —
    /// all validation failures come back in <see cref="LocationBundleImportResult.Errors"/>.
    /// </summary>
    Task<LocationBundleImportResult> ImportAsync(Stream zipStream, string? initiatedBy, CancellationToken ct = default);
}

public class LocationBundleImportResult
{
    public bool Success { get; set; }

    public string? BundleKey { get; set; }
    public string? Version { get; set; }
    public string? Name { get; set; }

    /// <summary>"install" for a new bundle key, "update" for a new version of an existing one.</summary>
    public string? Action { get; set; }

    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
