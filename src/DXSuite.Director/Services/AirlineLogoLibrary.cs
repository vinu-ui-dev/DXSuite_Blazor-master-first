using DXSuite.Director.Components.Modals;
using DXSuite.Director.Models;
using DXSuite.Director.Entities;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Logging;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DXSuite.Director.Services;

public sealed record AirlineLogoUploadContext(
    int LocationId, int ConstraintId, int? Width, int? Height, string ResolutionMode,
    bool EnableContentApproval, int LastAssetId);

/// <summary>
/// Airline logos live in an ordinary Director asset library so they share its resolution constraint,
/// approval chain and media storage. The library is found by name, so a renamed copy such as
/// "Airline wordmarks1" still matches; failing that, any asset library whose name mentions "wordmark" or "logo".
/// Set <c>Aviation:AirlineLogoLibrary</c> to pick it by name explicitly.
/// </summary>
public sealed class AirlineLogoLibrary : IAirlineLogoLibrary
{
    private const int AssetLibraryLocationTypeId = 1020;
    private const string DefaultLibraryName = "Airline wordmarks";

    // A context of its own per call: the circuit-wide scoped context is shared with every other component, and a second
    // query on it while one is running throws, which left the logo buttons disabled.
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly ISessionState _session;
    private readonly string _libraryName;

    public AirlineLogoLibrary(IDbContextFactory<DirectorDbContext> dbFactory, ISessionState session, IConfiguration config)
    {
        _dbFactory = dbFactory;
        _session = session;
        _libraryName = config["Aviation:AirlineLogoLibrary"] is { Length: > 0 } name ? name : DefaultLibraryName;
    }

    public Type UploaderComponentType => typeof(AirlineLogoUploader);

    public async Task<AirlineLogoLibraryInfo?> GetLibraryAsync(CancellationToken ct = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(ct);
        var library = await FindLibraryAsync(_db, ct);
        if (library is null) return null;

        var constraint = await GlobalConstraintAsync(_db, library.LocationId, ct);
        return new AirlineLogoLibraryInfo(library.LocationName ?? _libraryName,
            constraint?.ResolutionWidth ?? 0, constraint?.ResolutionHeight ?? 0);
    }

    public async Task<AirlineLogoLibraryInfo?> EnsureLibraryAsync(CancellationToken ct = default)
    {
        if (await GetLibraryAsync(ct) is { } existing) return existing;

        if (_session.RoleEnum != Roles.Role.Admin)
        {
            AirlineFileLog.Write($"[LOGO-LIB] no library exists and the signed-in user ('{_session.UserName}', role {_session.RoleEnum}) is not an Admin, so it was not created");
            return null;
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // An asset library sits in a location group; use the first one, or start one for it.
            var groupId = await db.TblLocationGroups.AsNoTracking().OrderBy(g => g.SortOrder).ThenBy(g => g.LocationGroupId)
                .Select(g => (int?)g.LocationGroupId).FirstOrDefaultAsync(ct);
            if (groupId is null)
            {
                var group = new TblLocationGroup { LocationGroupName = "Libraries", SortOrder = 0, CreateDate = DateTime.UtcNow };
                db.TblLocationGroups.Add(group);
                await db.SaveChangesAsync(ct);
                groupId = group.LocationGroupId;
            }

            // The wordmark rule: the same shape as 1200 x 400 (aspect ratio), as the Branding tab states.
            var location = new TblLocation
            {
                LocationName = _libraryName,
                LocationTypeId = AssetLibraryLocationTypeId,
                LocationGroupId = groupId.Value,
                BackgroundImageWidth = 1200,
                BackgroundImageHeight = 400,
                SortOrder = 0,
                CreateDate = DateTime.UtcNow,
            };
            db.TblLocations.Add(location);
            await db.SaveChangesAsync(ct);

            var constraint = new TblConstraint
            {
                ConstraintName = _libraryName,
                OwningLocationId = location.LocationId,
                IsGlobal = true,
                ResolutionWidth = 1200,
                ResolutionHeight = 400,
                ResolutionMode = "aspect-ratio",
                CreateDate = DateTime.UtcNow,
            };
            db.TblConstraints.Add(constraint);
            await db.SaveChangesAsync(ct);

            location.ExtendedData = ExtendedDataHelper.UpdateProperty<LocationExtendedData>(location.ExtendedData, ed =>
            {
                ed.GlobalConstraintId = constraint.ConstraintId;
            });
            await db.SaveChangesAsync(ct);

            AirlineFileLog.Write($"[LOGO-LIB] created the asset library '{_libraryName}' (LocationId {location.LocationId}, ConstraintId {constraint.ConstraintId}) for Admin '{_session.UserName}'. Content approval is off: turn it on, with an approval chain, on that library in Director to send uploads through Approvals.");
            return new AirlineLogoLibraryInfo(_libraryName, 1200, 400);
        }
        catch (Exception ex)
        {
            AirlineFileLog.Write($"[LOGO-LIB] creating the asset library '{_libraryName}' failed: {AirlineFileLog.Describe(ex)}");
            return null;
        }
    }

    public async Task<IReadOnlyList<AirlineLogoFile>> ListApprovedAsync(CancellationToken ct = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(ct);
        var library = await FindLibraryAsync(_db, ct);
        if (library is null) return [];

        // An asset with no approval row predates the chain and counts as approved.
        var assets = await _db.TblAssets.AsNoTracking()
            .Where(a => a.LocationId == library.LocationId && !a.IsDeleted
                && (a.AssetApproval == null || a.AssetApproval.ApprovalStatus == 1))
            .OrderByDescending(a => a.AssetId)
            .Select(a => new { a.AssetId, a.AssetName, a.AssetPath })
            .ToListAsync(ct);

        return assets.Select(a => new AirlineLogoFile(a.AssetId, a.AssetName ?? a.AssetPath, a.AssetPath, true)).ToList();
    }

    public async Task<IReadOnlyDictionary<int, LogoApprovalState>> GetApprovalStatesAsync(IReadOnlyCollection<int> assetIds, CancellationToken ct = default)
    {
        if (assetIds.Count == 0) return new Dictionary<int, LogoApprovalState>();

        await using var _db = await _dbFactory.CreateDbContextAsync(ct);
        var found = await _db.TblAssets.AsNoTracking()
            .Where(a => assetIds.Contains(a.AssetId) && !a.IsDeleted)
            .Select(a => new { a.AssetId, Status = (int?)a.AssetApproval!.ApprovalStatus })
            .ToListAsync(ct);
        var states = found.ToDictionary(a => a.AssetId, a => a.Status switch
        {
            null or 1 => LogoApprovalState.Approved,
            2 => LogoApprovalState.Declined,
            _ => LogoApprovalState.Pending,
        });
        return assetIds.Distinct().ToDictionary(id => id, id => states.TryGetValue(id, out var s) ? s : LogoApprovalState.Gone);
    }

    /// <summary>What the uploader dialog needs, or null when the library or its resolution constraint is missing.</summary>
    public async Task<AirlineLogoUploadContext?> GetUploadContextAsync(CancellationToken ct = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(ct);
        var library = await FindLibraryAsync(_db, ct);
        if (library is null) return null;
        var constraint = await GlobalConstraintAsync(_db, library.LocationId, ct);
        if (constraint is null)
        {
            AirlineFileLog.Write($"[LOGO-LIB] library '{library.LocationName}' (LocationId {library.LocationId}) has no global resolution constraint, so uploads are not offered");
            return null;
        }

        var lastAssetId = await _db.TblAssets.AsNoTracking()
            .Where(a => a.LocationId == library.LocationId)
            .Select(a => (int?)a.AssetId).MaxAsync(ct) ?? 0;

        return new AirlineLogoUploadContext(library.LocationId, constraint.ConstraintId,
            constraint.ResolutionWidth, constraint.ResolutionHeight, constraint.ResolutionMode,
            library.EnableContentApproval, lastAssetId);
    }

    /// <summary>The newest file the uploader added after <paramref name="lastAssetId"/>, with its approval state.</summary>
    public async Task<AirlineLogoFile?> FindUploadedSinceAsync(int locationId, int lastAssetId, CancellationToken ct = default)
    {
        await using var _db = await _dbFactory.CreateDbContextAsync(ct);
        var asset = await _db.TblAssets.AsNoTracking()
            .Where(a => a.LocationId == locationId && a.AssetId > lastAssetId && !a.IsDeleted)
            .OrderByDescending(a => a.AssetId)
            .Select(a => new { a.AssetId, a.AssetName, a.AssetPath, Status = (int?)a.AssetApproval!.ApprovalStatus })
            .FirstOrDefaultAsync(ct);

        return asset is null
            ? null
            : new AirlineLogoFile(asset.AssetId, asset.AssetName ?? asset.AssetPath, asset.AssetPath, asset.Status is null or 1);
    }

    private async Task<TblLocation?> FindLibraryAsync(DirectorDbContext db, CancellationToken ct)
    {
        var libraries = db.TblLocations.AsNoTracking().Where(l => l.LocationTypeId == AssetLibraryLocationTypeId);
        var found = await libraries.Where(l => l.LocationName == _libraryName).OrderBy(l => l.LocationId).FirstOrDefaultAsync(ct)
            ?? await libraries.Where(l => l.LocationName != null && l.LocationName.StartsWith(_libraryName))
                .OrderBy(l => l.LocationId).FirstOrDefaultAsync(ct)
            ?? await libraries.Where(l => l.LocationName != null && (l.LocationName.Contains("wordmark") || l.LocationName.Contains("logo")))
                .OrderBy(l => l.LocationId).FirstOrDefaultAsync(ct);

        if (found is null)
        {
            var names = await libraries.OrderBy(l => l.LocationId).Select(l => l.LocationName).Take(30).ToListAsync(ct);
            AirlineFileLog.Write($"[LOGO-LIB] no airline logo library found (looked for '{_libraryName}', then names containing 'wordmark' or 'logo', among asset libraries of LocationTypeId {AssetLibraryLocationTypeId}). Asset libraries present: [{string.Join("; ", names)}]. Create one named '{DefaultLibraryName}' or set Aviation:AirlineLogoLibrary to an existing library's name.");
        }
        return found;
    }

    private static Task<TblConstraint?> GlobalConstraintAsync(DirectorDbContext db, int locationId, CancellationToken ct) =>
        db.TblConstraints.AsNoTracking()
            .Where(c => c.OwningLocationId == locationId && c.IsGlobal)
            .OrderBy(c => c.ConstraintId)
            .FirstOrDefaultAsync(ct);
}
