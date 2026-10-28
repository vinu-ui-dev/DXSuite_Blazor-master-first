using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public interface IContentMetadataService
{
    Task<List<TblAssetContentMetadata>> GetMetadataForAssetAsync(int assetId);
    /// <summary>
    /// Makes the asset hold exactly <paramref name="values"/> for the key, one row per trimmed value.
    /// An empty set removes the key.
    /// </summary>
    Task SetMetadataAsync(int assetId, string key, IEnumerable<string> values, int? setByUserId);
    Task RemoveMetadataAsync(int assetId, string key);
    Task<List<string>> GetDistinctMetadataValuesAsync(int locationId, string metadataKey);
}
