using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

/// <summary>The user and user group tag stores, which live on the identity context.</summary>
public class IdentityMetadataUsageContributor : IMetadataUsageContributor
{
    private readonly IIdentityDbContext _db;

    public IdentityMetadataUsageContributor(IIdentityDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MetadataUsageCount>> CountKeyAsync(string key, CancellationToken ct = default) =>
    [
        new(MetadataUsageKind.Tag, "user", "users",
            await _db.TblUserMetadata.Where(m => m.MetadataKey == key).Select(m => m.UserId).Distinct().CountAsync(ct)),
        new(MetadataUsageKind.Tag, "group", "groups",
            await _db.TblUserGroupMetadata.Where(m => m.MetadataKey == key).Select(m => m.UserGroupId).Distinct().CountAsync(ct))
    ];

    public async Task<IReadOnlyList<MetadataUsageCount>> CountValuesAsync(string key, IReadOnlyCollection<string> values, CancellationToken ct = default) =>
    [
        new(MetadataUsageKind.Tag, "user", "users",
            await _db.TblUserMetadata.Where(m => m.MetadataKey == key && values.Contains(m.MetadataValue))
                .Select(m => m.UserId).Distinct().CountAsync(ct)),
        new(MetadataUsageKind.Tag, "group", "groups",
            await _db.TblUserGroupMetadata.Where(m => m.MetadataKey == key && values.Contains(m.MetadataValue))
                .Select(m => m.UserGroupId).Distinct().CountAsync(ct))
    ];
}
