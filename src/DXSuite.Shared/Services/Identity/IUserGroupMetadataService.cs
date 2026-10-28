using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public interface IUserGroupMetadataService
{
    /// <summary>Every key's values, one entry per key.</summary>
    Task<Dictionary<string, List<string>>> GetMetadataAsync(int userGroupId);

    /// <summary>The values held for one key, empty when the key is absent.</summary>
    Task<List<string>> GetValuesAsync(int userGroupId, string key);
    Task<List<TblUserGroupMetadata>> GetMetadataRecordsAsync(int userGroupId);
    /// <summary>
    /// Makes the key hold exactly <paramref name="values"/>, one row per trimmed value. An empty set
    /// removes the key.
    /// </summary>
    Task SetMetadataAsync(int userGroupId, string key, IEnumerable<string> values);
    Task DeleteMetadataAsync(int userGroupId, string key);
    Task DeleteMetadataByIdAsync(int userGroupMetadataId);
    Task<List<string>> GetKnownKeysAsync();
}
