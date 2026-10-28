using System.IO.Compression;
using System.Text.Json;

namespace DXSuite.Director.Services;

/// <summary>
/// Reads the <c>manifest.json</c> that every Engage for BrightSign artifact carries at its root.
///
/// The manifest is the version of record: the operator never types a version, because a typed
/// value that disagreed with the manifest would leave the device reporting one version forever
/// while the pipeline believed another, and no comparison in the resolver would ever converge.
/// </summary>
public static class ArtifactManifest
{
    private const string ManifestEntryName = "manifest.json";

    /// <summary>
    /// Applets built for SignageOS declare their version here instead; the convention predates
    /// manifests and those artifacts are still published.
    /// </summary>
    private const string LegacyConfigEntryName = "config.json";

    /// <summary>
    /// Returns the version declared by the artifact at <paramref name="zipPath"/>, or null with a
    /// reason. A missing or unreadable manifest is a refusal rather than a fallback, because the
    /// only other source of a version is a human typing one.
    /// </summary>
    public static string? TryReadVersion(string zipPath, out string? error)
    {
        if (!File.Exists(zipPath))
        {
            error = $"Artifact not found at {zipPath}.";
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (!TryReadFrom(archive, ManifestEntryName, out var version, out var entryError))
            {
                error = "Artifact contains no manifest.json.";
                return null;
            }

            error = entryError;
            return version;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            error = $"Artifact manifest could not be read: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Returns the version an already-buffered artifact declares about itself, preferring
    /// <c>manifest.json</c> and falling back to a legacy SignageOS <c>config.json</c>.
    ///
    /// This exists so the upload form and the publish API resolve a version through the same
    /// parse. They diverged once: the form read only config.json and so refused every BrightSign
    /// artifact ever built, on every version, for every customer.
    ///
    /// The stream is left open and its position is not guaranteed on return; callers that go on
    /// to store the artifact must reset it.
    /// </summary>
    public static string? TryReadDeclaredVersion(Stream zipStream, out string? error)
    {
        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            if (TryReadFrom(archive, ManifestEntryName, out var version, out var manifestError))
            {
                error = manifestError;
                return version;
            }

            if (TryReadFrom(archive, LegacyConfigEntryName, out var legacyVersion, out var legacyError))
            {
                error = legacyError;
                return legacyVersion;
            }

            error = "Artifact contains neither manifest.json nor config.json.";
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            error = $"Artifact manifest could not be read: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// False when the archive carries no such entry, which lets a caller move on to the next
    /// convention. True means the entry was there and either yielded a version or a reason it
    /// did not, so a present-but-broken document is never silently skipped.
    /// </summary>
    private static bool TryReadFrom(ZipArchive archive, string entryName, out string? version, out string? error)
    {
        version = null;
        error = null;

        var entry = FindShallowest(archive, entryName);
        if (entry == null)
            return false;

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.String)
        {
            error = $"{entryName} carries no version string.";
            return true;
        }

        var value = versionElement.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{entryName} carries an empty version.";
            return true;
        }

        version = value.Trim();
        return true;
    }

    /// <summary>
    /// Prefers the entry at the zip root, then the shallowest one, so a bundle wrapped in a
    /// single directory still resolves while a nested dependency's manifest never wins.
    /// </summary>
    private static ZipArchiveEntry? FindShallowest(ZipArchive archive, string entryName)
        => archive.Entries
            .Where(e => string.Equals(e.Name, entryName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName.Count(c => c is '/' or '\\'))
            .FirstOrDefault();
}
