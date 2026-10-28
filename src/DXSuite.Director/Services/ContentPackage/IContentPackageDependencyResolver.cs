using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Walks the content package entity graph to determine the complete set of records that must be
/// included in a bundle for the given selection. Callers provide a selection (locations,
/// assets, displays) and receive the expanded record-ID set grouped by entity type.
/// </summary>
public interface IContentPackageDependencyResolver
{
    Task<ContentPackageResolvedSet> ResolveAsync(ContentPackageSelection selection, CancellationToken ct = default);
}

/// <summary>
/// Expanded set of records to include in the bundle, keyed by int PK for efficient loading
/// by the bundle builder. The builder loads each set, serializes to ContentPackageRecord DTOs,
/// and packages media files for included assets.
/// </summary>
public class ContentPackageResolvedSet
{
    public HashSet<int> LocationIds { get; } = new();
    public HashSet<int> LocationGroupIds { get; } = new();
    public HashSet<int> LocationMetadataRuleIds { get; } = new();
    public HashSet<int> DisplayIds { get; } = new();
    public HashSet<int> DisplayGroupIds { get; } = new();
    public HashSet<int> AssetIds { get; } = new();
    public HashSet<int> AssetGroupIds { get; } = new();
    public HashSet<int> ConstraintIds { get; } = new();
    public HashSet<int> ScheduleEventIds { get; } = new();
    public HashSet<int> AssetContentMetadataIds { get; } = new();
    public HashSet<int> FloorPlanElementIds { get; } = new();
    public HashSet<int> LocationAccessRuleIds { get; } = new();
    public HashSet<int> AccessScheduleIds { get; } = new();
    public HashSet<int> ApprovalChainIds { get; } = new();

    public int TotalRecordCount =>
        LocationIds.Count + LocationGroupIds.Count + LocationMetadataRuleIds.Count + DisplayIds.Count + DisplayGroupIds.Count +
        AssetIds.Count + AssetGroupIds.Count + ConstraintIds.Count + ScheduleEventIds.Count +
        AssetContentMetadataIds.Count + FloorPlanElementIds.Count + LocationAccessRuleIds.Count +
        AccessScheduleIds.Count + ApprovalChainIds.Count;
}
