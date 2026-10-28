using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Services.Identity;

namespace DXSuite.Director.Services;

/// <summary>
/// Director's stores of registry keys: asset and device tags, and the four configuration columns that
/// reference a key by name. Counts are of distinct subjects (assets, displays), not rows, because that is
/// what an operator deciding whether to delete a key is weighing.
/// </summary>
public class DirectorMetadataUsageContributor : IMetadataUsageContributor
{
    private readonly IDirectorDBContext _db;

    public DirectorMetadataUsageContributor(IDirectorDBContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MetadataUsageCount>> CountKeyAsync(string key, CancellationToken ct = default) =>
    [
        new(MetadataUsageKind.Tag, "asset", "assets",
            await _db.TblAssetContentMetadata.Where(m => m.MetadataKey == key).Select(m => m.AssetId).Distinct().CountAsync(ct)),
        new(MetadataUsageKind.Tag, "device", "devices",
            await _db.TblDeviceMetadata.Where(m => m.MetadataKey == key).Select(m => m.DeviceId).Distinct().CountAsync(ct)),
        new(MetadataUsageKind.Reference, "display", "displays",
            await _db.TblDisplays.CountAsync(d => d.StatusSelectorMetadataKey == key, ct)),
        new(MetadataUsageKind.Reference, "access schedule", "access schedules",
            await _db.TblAccessSchedules.CountAsync(s => s.MetadataKey == key, ct)),
        new(MetadataUsageKind.Reference, "floor plan icon schedule", "floor plan icon schedules",
            await _db.TblFloorPlanIconSchedules.CountAsync(s => s.MetadataKey == key, ct))
    ];

    public async Task<IReadOnlyList<MetadataUsageCount>> CountValuesAsync(string key, IReadOnlyCollection<string> values, CancellationToken ct = default) =>
    [
        new(MetadataUsageKind.Tag, "asset", "assets",
            await _db.TblAssetContentMetadata.Where(m => m.MetadataKey == key && values.Contains(m.MetadataValue))
                .Select(m => m.AssetId).Distinct().CountAsync(ct)),
        new(MetadataUsageKind.Tag, "device", "devices",
            await _db.TblDeviceMetadata.Where(m => m.MetadataKey == key && values.Contains(m.MetadataValue))
                .Select(m => m.DeviceId).Distinct().CountAsync(ct)),
        new(MetadataUsageKind.Reference, "floor plan icon schedule", "floor plan icon schedules",
            await _db.TblFloorPlanIconSchedules.CountAsync(s => s.MetadataKey == key && s.MetadataValue != null && values.Contains(s.MetadataValue), ct))
    ];
}
