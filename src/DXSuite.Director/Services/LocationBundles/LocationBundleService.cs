using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services.LocationBundles;

/// <summary>
/// Registry operations for uploaded location bundles. Delete operations are guarded by
/// a referenced-by scan of TblLocation.ExtendedData — locations reference bundles by
/// key inside JSON, so the scan parses client-side rather than relying on JSON SQL.
/// </summary>
public class LocationBundleService : ILocationBundleService
{
    private readonly IDirectorDBContext _db;
    private readonly IMediaStore _mediaStore;
    private readonly ITenantContext _tenant;
    private readonly ILogger<LocationBundleService> _logger;

    public LocationBundleService(
        IDirectorDBContext db,
        IMediaStore mediaStore,
        ITenantContext tenant,
        ILogger<LocationBundleService> logger)
    {
        _db = db;
        _mediaStore = mediaStore;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<List<LocationBundleInfo>> GetBundlesAsync(CancellationToken ct = default)
    {
        var bundles = await _db.TblLocationBundles
            .Include(b => b.Versions)
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

        var references = await GetAllReferencesAsync(ct);

        return bundles.Select(b => new LocationBundleInfo
        {
            Bundle = b,
            Versions = b.Versions.OrderByDescending(v => v.UploadDate).ToList(),
            ReferencedBy = references.TryGetValue(b.BundleKey, out var refs) ? refs : new List<LocationBundleReference>()
        }).ToList();
    }

    public async Task<LocationBundleManifest?> GetManifestAsync(string bundleKey, string? version = null, CancellationToken ct = default)
    {
        var resolved = await ResolveVersionAsync(bundleKey, version, ct);
        return resolved?.Manifest;
    }

    public async Task<LocationBundleResolvedVersion?> ResolveVersionAsync(string bundleKey, string? pinnedVersion, CancellationToken ct = default)
    {
        var bundle = await _db.TblLocationBundles
            .Include(b => b.Versions)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BundleKey == bundleKey, ct);

        if (bundle == null || !bundle.IsEnabled)
            return null;

        var versionRow = pinnedVersion != null
            ? bundle.Versions.FirstOrDefault(v => v.Version == pinnedVersion)
            : null;

        // A pin to a version that no longer exists falls back to CurrentVersion rather
        // than breaking the location.
        versionRow ??= bundle.Versions.FirstOrDefault(v => v.Version == bundle.CurrentVersion);

        if (versionRow == null)
            return null;

        var errors = new List<string>();
        var warnings = new List<string>();
        return new LocationBundleResolvedVersion
        {
            Version = versionRow.Version,
            EntryPath = versionRow.EntryPath,
            Manifest = LocationBundleManifestValidator.Validate(versionRow.ManifestJson, errors, warnings)
        };
    }

    public async Task<LocationBundleOperationResult> SetCurrentVersionAsync(string bundleKey, string version, string? initiatedBy, CancellationToken ct = default)
    {
        var bundle = await _db.TblLocationBundles
            .Include(b => b.Versions)
            .FirstOrDefaultAsync(b => b.BundleKey == bundleKey, ct);

        if (bundle == null)
            return LocationBundleOperationResult.Fail($"Bundle \"{bundleKey}\" not found.");

        if (bundle.Versions.All(v => v.Version != version))
            return LocationBundleOperationResult.Fail($"Version {version} does not exist for bundle \"{bundleKey}\".");

        var isRollback = string.Compare(version, bundle.CurrentVersion, StringComparison.OrdinalIgnoreCase) < 0;
        bundle.CurrentVersion = version;
        bundle.LastEditDate = DateTime.UtcNow;
        bundle.LastEditUser = initiatedBy;
        await _db.SaveChangesAsync(ct);

        await WriteHistoryAsync(bundleKey, version, isRollback ? "rollback" : "update", "Success", initiatedBy,
            $"Current version set to {version}", ct);

        return LocationBundleOperationResult.Ok();
    }

    public async Task<LocationBundleOperationResult> DeleteBundleAsync(string bundleKey, string? initiatedBy, CancellationToken ct = default)
    {
        var references = await GetReferencingLocationsAsync(bundleKey, ct);
        if (references.Count > 0)
        {
            return new LocationBundleOperationResult
            {
                Error = $"Bundle \"{bundleKey}\" is still assigned to {references.Count} location(s).",
                ReferencingLocations = references
            };
        }

        var bundle = await _db.TblLocationBundles
            .Include(b => b.Versions)
            .FirstOrDefaultAsync(b => b.BundleKey == bundleKey, ct);

        if (bundle == null)
            return LocationBundleOperationResult.Fail($"Bundle \"{bundleKey}\" not found.");

        var versions = bundle.Versions.Select(v => v.Version).ToList();
        _db.TblLocationBundles.Remove(bundle); // versions cascade
        await _db.SaveChangesAsync(ct);

        foreach (var version in versions)
            await DeleteStoredFilesAsync(bundleKey, version, ct);

        await WriteHistoryAsync(bundleKey, null, "delete", "Success", initiatedBy,
            $"Deleted bundle with {versions.Count} version(s)", ct);

        return LocationBundleOperationResult.Ok();
    }

    public async Task<LocationBundleOperationResult> DeleteVersionAsync(string bundleKey, string version, string? initiatedBy, CancellationToken ct = default)
    {
        var bundle = await _db.TblLocationBundles
            .Include(b => b.Versions)
            .FirstOrDefaultAsync(b => b.BundleKey == bundleKey, ct);

        if (bundle == null)
            return LocationBundleOperationResult.Fail($"Bundle \"{bundleKey}\" not found.");

        var versionRow = bundle.Versions.FirstOrDefault(v => v.Version == version);
        if (versionRow == null)
            return LocationBundleOperationResult.Fail($"Version {version} does not exist for bundle \"{bundleKey}\".");

        if (bundle.CurrentVersion == version)
            return LocationBundleOperationResult.Fail($"Version {version} is the current version. Set a different current version first.");

        var pinnedBy = (await GetReferencingLocationsAsync(bundleKey, ct))
            .Where(r => r.PinnedVersion == version)
            .ToList();
        if (pinnedBy.Count > 0)
        {
            return new LocationBundleOperationResult
            {
                Error = $"Version {version} is pinned by {pinnedBy.Count} location(s).",
                ReferencingLocations = pinnedBy
            };
        }

        _db.TblLocationBundleVersions.Remove(versionRow);
        await _db.SaveChangesAsync(ct);

        await DeleteStoredFilesAsync(bundleKey, version, ct);
        await WriteHistoryAsync(bundleKey, version, "delete", "Success", initiatedBy, "Deleted single version", ct);

        return LocationBundleOperationResult.Ok();
    }

    public async Task<List<LocationBundleReference>> GetReferencingLocationsAsync(string bundleKey, CancellationToken ct = default)
    {
        var references = await GetAllReferencesAsync(ct);
        return references.TryGetValue(bundleKey, out var refs) ? refs : new List<LocationBundleReference>();
    }

    public async Task<List<TblLocationBundleHistory>> GetHistoryAsync(int take = 100, CancellationToken ct = default)
    {
        return await _db.TblLocationBundleHistories
            .AsNoTracking()
            .OrderByDescending(h => h.StartedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    // ────────────────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, List<LocationBundleReference>>> GetAllReferencesAsync(CancellationToken ct)
    {
        var locations = await _db.TblLocations
            .AsNoTracking()
            .Where(l => l.ExtendedData != null)
            .Select(l => new { l.LocationId, l.LocationName, l.ExtendedData })
            .ToListAsync(ct);

        var result = new Dictionary<string, List<LocationBundleReference>>(StringComparer.OrdinalIgnoreCase);
        foreach (var loc in locations)
        {
            var assignment = ExtendedDataHelper.Parse<LocationExtendedData>(loc.ExtendedData)?.Bundle;
            if (assignment == null || string.IsNullOrEmpty(assignment.BundleKey))
                continue;

            if (!result.TryGetValue(assignment.BundleKey, out var list))
                result[assignment.BundleKey] = list = new List<LocationBundleReference>();

            list.Add(new LocationBundleReference
            {
                LocationId = loc.LocationId,
                LocationName = loc.LocationName ?? $"Location {loc.LocationId}",
                PinnedVersion = assignment.PinnedVersion
            });
        }

        return result;
    }

    private async Task DeleteStoredFilesAsync(string bundleKey, string version, CancellationToken ct)
    {
        try
        {
            var prefix = LocationBundleConstants.StoragePath(bundleKey, version);
            var stored = await _mediaStore.ListAsync(_tenant.TenantId, prefix, ct);
            foreach (var path in stored)
                await _mediaStore.DeleteAsync(_tenant.TenantId, path, ct);
        }
        catch (Exception ex)
        {
            // Orphaned files are harmless (never served once the registry row is gone);
            // don't fail the delete over storage hiccups.
            _logger.LogWarning(ex, "Failed to delete stored files for bundle {BundleKey} {Version}", bundleKey, version);
        }
    }

    private async Task WriteHistoryAsync(string bundleKey, string? version, string action, string status,
        string? initiatedBy, string? message, CancellationToken ct)
    {
        try
        {
            _db.TblLocationBundleHistories.Add(new TblLocationBundleHistory
            {
                BundleKey = bundleKey,
                Version = version,
                Action = action,
                Status = status,
                InitiatedByUser = initiatedBy,
                Message = message,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write location bundle history for {BundleKey}", bundleKey);
        }
    }
}
