using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// Soft-deletes an asset row. The file stays on disk, so the media folder watcher never sees the change and
/// the location's publication is invalidated here instead.
/// </summary>
public interface IAssetSoftDeleteService
{
    /// <returns>The deleted row, or null when it does not exist.</returns>
    Task<TblAsset?> SoftDeleteAsync(int assetId, CancellationToken ct = default);
}

public class AssetSoftDeleteService : IAssetSoftDeleteService
{
    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly IPublicationInvalidator _invalidator;

    public AssetSoftDeleteService(IDbContextFactory<DirectorDbContext> dbFactory, IPublicationInvalidator invalidator)
    {
        _dbFactory = dbFactory;
        _invalidator = invalidator;
    }

    public async Task<TblAsset?> SoftDeleteAsync(int assetId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var asset = await db.TblAssets.FirstOrDefaultAsync(a => a.AssetId == assetId, ct);
        if (asset == null)
            return null;

        asset.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        _invalidator.AssetsChanged([asset.LocationId]);
        return asset;
    }
}
