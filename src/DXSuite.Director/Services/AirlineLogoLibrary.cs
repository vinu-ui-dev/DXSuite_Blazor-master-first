using DXSuite.Director.Components.Modals;
using DXSuite.Director.Entities;
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
/// "Airline wordmarks1" still matches.
/// </summary>
public sealed class AirlineLogoLibrary : IAirlineLogoLibrary
{
    private const int AssetLibraryLocationTypeId = 1020;
    private const string DefaultLibraryName = "Airline wordmarks";

    private readonly IDirectorDBContext _db;
    private readonly string _libraryName;

    public AirlineLogoLibrary(IDirectorDBContext db, IConfiguration config)
    {
        _db = db;
        _libraryName = config["Aviation:AirlineLogoLibrary"] is { Length: > 0 } name ? name : DefaultLibraryName;
    }

    public Type UploaderComponentType => typeof(AirlineLogoUploader);

    public async Task<AirlineLogoLibraryInfo?> GetLibraryAsync(CancellationToken ct = default)
    {
        var library = await FindLibraryAsync(ct);
        if (library is null) return null;

        var constraint = await GlobalConstraintAsync(library.LocationId, ct);
        return new AirlineLogoLibraryInfo(library.LocationName ?? _libraryName,
            constraint?.ResolutionWidth ?? 0, constraint?.ResolutionHeight ?? 0);
    }

    public async Task<IReadOnlyList<AirlineLogoFile>> ListApprovedAsync(CancellationToken ct = default)
    {
        var library = await FindLibraryAsync(ct);
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

    /// <summary>What the uploader dialog needs, or null when the library or its resolution constraint is missing.</summary>
    public async Task<AirlineLogoUploadContext?> GetUploadContextAsync(CancellationToken ct = default)
    {
        var library = await FindLibraryAsync(ct);
        if (library is null) return null;
        var constraint = await GlobalConstraintAsync(library.LocationId, ct);
        if (constraint is null) return null;

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
        var asset = await _db.TblAssets.AsNoTracking()
            .Where(a => a.LocationId == locationId && a.AssetId > lastAssetId && !a.IsDeleted)
            .OrderByDescending(a => a.AssetId)
            .Select(a => new { a.AssetId, a.AssetName, a.AssetPath, Status = (int?)a.AssetApproval!.ApprovalStatus })
            .FirstOrDefaultAsync(ct);

        return asset is null
            ? null
            : new AirlineLogoFile(asset.AssetId, asset.AssetName ?? asset.AssetPath, asset.AssetPath, asset.Status is null or 1);
    }

    private async Task<TblLocation?> FindLibraryAsync(CancellationToken ct)
    {
        var libraries = _db.TblLocations.AsNoTracking().Where(l => l.LocationTypeId == AssetLibraryLocationTypeId);
        return await libraries.Where(l => l.LocationName == _libraryName).OrderBy(l => l.LocationId).FirstOrDefaultAsync(ct)
            ?? await libraries.Where(l => l.LocationName != null && l.LocationName.StartsWith(_libraryName))
                .OrderBy(l => l.LocationId).FirstOrDefaultAsync(ct);
    }

    private Task<TblConstraint?> GlobalConstraintAsync(int locationId, CancellationToken ct) =>
        _db.TblConstraints.AsNoTracking()
            .Where(c => c.OwningLocationId == locationId && c.IsGlobal)
            .OrderBy(c => c.ConstraintId)
            .FirstOrDefaultAsync(ct);
}
