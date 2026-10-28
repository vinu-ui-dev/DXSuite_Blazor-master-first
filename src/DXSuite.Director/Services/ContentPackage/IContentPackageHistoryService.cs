using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Read-only queries over the content package audit log (<c>tblContentPackageHistory</c> +
/// <c>tblContentPackageHistoryItem</c>). Write paths are owned by the bundle importer
/// and the direct-push service; this interface exists purely for the UI.
/// </summary>
public interface IContentPackageHistoryService
{
    Task<List<ContentPackageHistoryListRow>> ListAsync(
        string? directionFilter,
        int take,
        CancellationToken ct = default);

    Task<ContentPackageHistoryDetail?> GetDetailAsync(int historyId, CancellationToken ct = default);
}

public class ContentPackageHistoryListRow
{
    public int ContentPackageHistoryId { get; set; }
    public string Direction { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? BundleFileName { get; set; }
    public string? InitiatedByUser { get; set; }
    public string? SourceEnvironment { get; set; }
    public string? ScopeDescription { get; set; }
    public int TotalRecords { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ContentPackageHistoryDetail
{
    public ContentPackageHistoryListRow Header { get; set; } = null!;
    public List<ContentPackageHistoryDetailItem> Items { get; set; } = new();
}

public class ContentPackageHistoryDetailItem
{
    public Guid ContentPackageId { get; set; }
    public string EntityType { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string? SourceUserPrincipalName { get; set; }
    public string? Notes { get; set; }
}
