using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.LocationBundles;

/// <summary>
/// Validates a location bundle zip (manifest + zip-slip + size limits) and installs it:
/// files stream to the tenant's media store under location-bundles/{key}/{version}/,
/// then the registry and history rows are written. Version folders are write-once —
/// a duplicate (key, version) is rejected rather than overwritten, which is what makes
/// immutable cache headers on the serving endpoint safe.
/// </summary>
public class LocationBundleImporter : ILocationBundleImporter
{
    private readonly IDirectorDBContext _db;
    private readonly IMediaStore _mediaStore;
    private readonly ITenantContext _tenant;
    private readonly LocationBundleOptions _options;
    private readonly ILogger<LocationBundleImporter> _logger;

    public LocationBundleImporter(
        IDirectorDBContext db,
        IMediaStore mediaStore,
        ITenantContext tenant,
        IOptions<LocationBundleOptions> options,
        ILogger<LocationBundleImporter> logger)
    {
        _db = db;
        _mediaStore = mediaStore;
        _tenant = tenant;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LocationBundleImportResult> ImportAsync(Stream zipStream, string? initiatedBy, CancellationToken ct = default)
    {
        var result = new LocationBundleImportResult();

        if (!_tenant.IsPopulated)
        {
            result.Errors.Add("Tenant context is not available; cannot determine where to store bundle files.");
            return result;
        }

        // Stage to a temp file so the archive can be hashed and read twice without
        // holding the whole zip in memory.
        var stagingRoot = Path.Combine(Path.GetTempPath(), "DXSuite", "LocationBundleStaging");
        Directory.CreateDirectory(stagingRoot);
        var stagedPath = Path.Combine(stagingRoot, Guid.NewGuid() + ".zip");

        try
        {
            long zipBytes;
            string contentHash;
            await using (var staged = File.Create(stagedPath))
            {
                await zipStream.CopyToAsync(staged, ct);
                zipBytes = staged.Length;

                if (zipBytes > _options.MaxZipBytes)
                {
                    result.Errors.Add($"Bundle zip is {zipBytes:N0} bytes; the limit is {_options.MaxZipBytes:N0}.");
                    return result;
                }

                staged.Position = 0;
                using var sha = SHA256.Create();
                contentHash = Convert.ToHexString(await sha.ComputeHashAsync(staged, ct));
            }

            using var archive = OpenArchive(stagedPath, result);
            if (archive == null)
                return result;

            var manifest = ReadAndValidateManifest(archive, result);
            if (manifest == null || result.Errors.Count > 0)
                return result;

            var files = ValidateEntries(archive, manifest, result);
            if (files == null || result.Errors.Count > 0)
                return result;

            var existing = await _db.TblLocationBundles
                .Include(b => b.Versions)
                .FirstOrDefaultAsync(b => b.BundleKey == manifest.Id, ct);

            if (existing != null && existing.Versions.Any(v => v.Version == manifest.Version))
            {
                result.Errors.Add($"Version {manifest.Version} of bundle \"{manifest.Id}\" already exists. Bump the manifest version — uploaded versions are immutable.");
                return result;
            }

            result.BundleKey = manifest.Id;
            result.Version = manifest.Version;
            result.Name = manifest.Name;
            result.Action = existing == null ? "install" : "update";

            var startedAt = DateTime.UtcNow;
            try
            {
                await WriteFilesAsync(archive, files, manifest, ct);
                await WriteRegistryAsync(existing, manifest, files, zipBytes, contentHash, initiatedBy, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Location bundle import failed for {BundleKey} {Version}", manifest.Id, manifest.Version);
                await CleanupStoredFilesAsync(manifest.Id, manifest.Version);
                await WriteHistoryAsync(manifest.Id, manifest.Version, result.Action, "Failed", initiatedBy, startedAt, ex.Message, ct);
                result.Errors.Add("Import failed while storing the bundle: " + ex.Message);
                return result;
            }

            await WriteHistoryAsync(manifest.Id, manifest.Version, result.Action, "Success", initiatedBy, startedAt,
                $"{files.Count} files, {files.Sum(f => f.Length):N0} bytes", ct);

            result.Success = true;
            return result;
        }
        finally
        {
            try { File.Delete(stagedPath); } catch { /* best effort */ }
        }
    }

    // ────────────────────────────────────────────────────────────────────────

    private ZipArchive? OpenArchive(string stagedPath, LocationBundleImportResult result)
    {
        try
        {
            return ZipFile.OpenRead(stagedPath);
        }
        catch (InvalidDataException)
        {
            result.Errors.Add("The uploaded file is not a valid zip archive.");
            return null;
        }
    }

    private static LocationBundleManifest? ReadAndValidateManifest(ZipArchive archive, LocationBundleImportResult result)
    {
        var manifestEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(NormalizeEntryName(e.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));

        if (manifestEntry == null)
        {
            result.Errors.Add("Bundle is missing manifest.json at the zip root.");
            return null;
        }

        using var reader = new StreamReader(manifestEntry.Open());
        var json = reader.ReadToEnd();
        return LocationBundleManifestValidator.Validate(json, result.Errors, result.Warnings);
    }

    /// <summary>
    /// Returns the file entries to store, or null when validation failed. Rejects
    /// zip-slip entry names and enforces count/size limits before anything is written.
    /// </summary>
    private List<ZipArchiveEntry>? ValidateEntries(ZipArchive archive, LocationBundleManifest manifest, LocationBundleImportResult result)
    {
        var files = new List<ZipArchiveEntry>();
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var name = NormalizeEntryName(entry.FullName);
            if (name.Length == 0 || name.EndsWith('/'))
                continue; // directory entry

            if (name.StartsWith('/') || name.Contains("..") || name.Contains(':'))
            {
                result.Errors.Add($"Bundle contains an unsafe entry path: \"{entry.FullName}\".");
                return null;
            }

            totalBytes += entry.Length;
            files.Add(entry);
        }

        if (files.Count == 0)
        {
            result.Errors.Add("Bundle contains no files.");
            return null;
        }

        if (files.Count > _options.MaxFileCount)
        {
            result.Errors.Add($"Bundle contains {files.Count} files; the limit is {_options.MaxFileCount}.");
            return null;
        }

        if (totalBytes > _options.MaxUncompressedBytes)
        {
            result.Errors.Add($"Bundle uncompressed size is {totalBytes:N0} bytes; the limit is {_options.MaxUncompressedBytes:N0}.");
            return null;
        }

        var entryPath = NormalizeEntryName(manifest.Entry);
        if (!files.Any(f => string.Equals(NormalizeEntryName(f.FullName), entryPath, StringComparison.OrdinalIgnoreCase)))
        {
            result.Errors.Add($"Manifest entry \"{manifest.Entry}\" was not found in the bundle.");
            return null;
        }

        return files;
    }

    private async Task WriteFilesAsync(ZipArchive archive, List<ZipArchiveEntry> files, LocationBundleManifest manifest, CancellationToken ct)
    {
        foreach (var entry in files)
        {
            var relative = NormalizeEntryName(entry.FullName);
            var storagePath = LocationBundleConstants.StoragePath(manifest.Id, manifest.Version, relative);
            await using var entryStream = entry.Open();
            await _mediaStore.SaveAsync(_tenant.TenantId, storagePath, entryStream, ct);
        }
    }

    private async Task WriteRegistryAsync(
        TblLocationBundle? existing,
        LocationBundleManifest manifest,
        List<ZipArchiveEntry> files,
        long zipBytes,
        string contentHash,
        string? initiatedBy,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var bundle = existing;
        if (bundle == null)
        {
            bundle = new TblLocationBundle
            {
                BundleKey = manifest.Id,
                CreateDate = now
            };
            _db.TblLocationBundles.Add(bundle);
        }

        bundle.Name = manifest.Name;
        bundle.Description = manifest.Description;
        bundle.CurrentVersion = manifest.Version;
        bundle.LastEditDate = now;
        bundle.LastEditUser = initiatedBy;

        bundle.Versions.Add(new TblLocationBundleVersion
        {
            Version = manifest.Version,
            EntryPath = NormalizeEntryName(manifest.Entry),
            ManifestJson = SerializeManifestForStorage(manifest),
            FileCount = files.Count,
            TotalSizeBytes = files.Sum(f => f.Length),
            ContentHash = contentHash,
            UploadedBy = initiatedBy,
            UploadDate = now
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task WriteHistoryAsync(string bundleKey, string? version, string? action, string status,
        string? initiatedBy, DateTime startedAt, string? message, CancellationToken ct)
    {
        try
        {
            _db.TblLocationBundleHistories.Add(new TblLocationBundleHistory
            {
                BundleKey = bundleKey,
                Version = version,
                Action = action ?? "install",
                Status = status,
                InitiatedByUser = initiatedBy,
                Message = message?.Length > 2000 ? message[..2000] : message,
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // History must never turn a successful import into a failure.
            _logger.LogWarning(ex, "Failed to write location bundle history for {BundleKey}", bundleKey);
        }
    }

    private async Task CleanupStoredFilesAsync(string bundleKey, string version)
    {
        try
        {
            var prefix = LocationBundleConstants.StoragePath(bundleKey, version);
            var stored = await _mediaStore.ListAsync(_tenant.TenantId, prefix);
            foreach (var path in stored)
                await _mediaStore.DeleteAsync(_tenant.TenantId, path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up partially stored bundle files for {BundleKey} {Version}", bundleKey, version);
        }
    }

    private static string NormalizeEntryName(string name) =>
        name.Replace('\\', '/').Trim();

    /// <summary>
    /// Re-serializes the validated manifest rather than storing the raw upload bytes, so
    /// downstream consumers (settings form, capability display) always parse a known shape.
    /// </summary>
    private static string SerializeManifestForStorage(LocationBundleManifest manifest) =>
        System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
}
