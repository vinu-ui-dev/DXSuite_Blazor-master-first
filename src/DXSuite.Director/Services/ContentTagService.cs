using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;
using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Services;

public class ContentMetadataService : IContentMetadataService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IManifestCacheService _manifestCache;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ContentMetadataService> _logger;

    public ContentMetadataService(
        IServiceScopeFactory scopeFactory,
        IManifestCacheService manifestCache,
        ITenantContext tenantContext,
        ILogger<ContentMetadataService> logger)
    {
        _scopeFactory = scopeFactory;
        _manifestCache = manifestCache;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<List<TblAssetContentMetadata>> GetMetadataForAssetAsync(int assetId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblAssetContentMetadata
            .Where(t => t.AssetId == assetId)
            .OrderBy(t => t.MetadataKey)
            .ToListAsync();
    }

    public async Task SetMetadataAsync(int assetId, string key, IEnumerable<string> values, int? setByUserId)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Metadata key is required", nameof(key));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var existing = await db.TblAssetContentMetadata
            .Where(t => t.AssetId == assetId && t.MetadataKey == key)
            .OrderBy(t => t.AssetContentMetadataId)
            .ToListAsync();

        var plan = MetadataValueSet.Reconcile(existing.Select(t => t.MetadataValue).ToList(), values);
        if (plan.IsEmpty) return;

        foreach (var (index, value) in plan.Updates)
            existing[index].MetadataValue = value;
        foreach (var index in plan.Removes)
            db.TblAssetContentMetadata.Remove(existing[index]);
        foreach (var value in plan.Adds)
        {
            db.TblAssetContentMetadata.Add(new TblAssetContentMetadata
            {
                AssetId = assetId,
                MetadataKey = key,
                MetadataValue = value,
                SetByUserId = setByUserId,
                CreateDate = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        await InvalidateAssetLocationAsync(db, assetId);
        _logger.LogDebug("Set content metadata for asset {AssetId}: {Key} ({Added} added, {Updated} updated, {Removed} removed)",
            assetId, key, plan.Adds.Count, plan.Updates.Count, plan.Removes.Count);
    }

    public async Task RemoveMetadataAsync(int assetId, string key)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var existing = await db.TblAssetContentMetadata
            .Where(t => t.AssetId == assetId && t.MetadataKey == key)
            .ToListAsync();

        if (existing.Count > 0)
        {
            db.TblAssetContentMetadata.RemoveRange(existing);
            await db.SaveChangesAsync();
            await InvalidateAssetLocationAsync(db, assetId);
            _logger.LogDebug("Removed content metadata for asset {AssetId}: {Key}", assetId, key);
        }
    }

    // Tags travel inside the cached publication, so a tag edit has to drop it or players keep the old
    // targeting until the cache expires. Layouts and displays sourcing the location drop with it.
    private async Task InvalidateAssetLocationAsync(IDirectorDBContext db, int assetId)
    {
        var locationId = await db.TblAssets
            .Where(a => a.AssetId == assetId)
            .Select(a => a.LocationId)
            .FirstOrDefaultAsync();
        if (locationId is int id)
            _manifestCache.InvalidateLocation(_tenantContext.TenantId, id);
    }

    public async Task<List<string>> GetDistinctMetadataValuesAsync(int locationId, string metadataKey)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblAssetContentMetadata
            .Where(t => t.MetadataKey == metadataKey)
            .Join(db.TblAssets.Where(a => a.LocationId == locationId && !a.IsDeleted),
                  t => t.AssetId, a => a.AssetId, (t, a) => t.MetadataValue)
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync();
    }
}
