using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public interface IUserMetadataService
{
    /// <summary>Every key's values, one entry per key.</summary>
    Task<Dictionary<string, List<string>>> GetMetadataAsync(int userId);

    /// <summary>The values held for one key, empty when the key is absent.</summary>
    Task<List<string>> GetValuesAsync(int userId, string key);
    Task<List<TblUserMetadata>> GetMetadataRecordsAsync(int userId);
    /// <summary>
    /// Makes the key hold exactly <paramref name="values"/>, one row per trimmed value. An empty set
    /// removes the key.
    /// </summary>
    Task SetMetadataAsync(int userId, string key, IEnumerable<string> values);
    Task DeleteMetadataAsync(int userId, string key);
    Task DeleteMetadataByIdAsync(int userMetadataId);
    Task<List<string>> GetKnownKeysAsync();
}
