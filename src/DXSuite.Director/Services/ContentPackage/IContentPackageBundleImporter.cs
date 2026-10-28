using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DXSuite.Director.Services.ContentPackage;

/// <summary>
/// Reads a content package bundle, stages it on disk, and either previews or applies
/// the changes to the local database. Preview and Apply are separate calls so
/// the UI can show a diff before the user commits the import.
/// </summary>
public interface IContentPackageBundleImporter
{
    /// <summary>
    /// Stages <paramref name="bundleStream"/> in a temp directory and returns a
    /// summary of what the apply step will do. The stagingId returned here is
    /// passed to <see cref="ApplyAsync"/>.
    /// </summary>
    Task<ContentPackageImportPreview> CreatePreviewAsync(
        Stream bundleStream,
        string? originalFileName,
        CancellationToken ct = default);

    /// <summary>
    /// Applies a previously-previewed bundle. Must be called with a stagingId
    /// returned from <see cref="CreatePreviewAsync"/>. On success the staged
    /// bundle is deleted; on failure it is left in place for inspection.
    /// </summary>
    /// <param name="replaceMode">
    /// When true, after all upserts complete the importer deletes any child
    /// records (displays, assets, schedule events, etc.) that belong to the
    /// bundle's locations but whose ContentPackageId is not in the bundle. Used to
    /// force the target to exactly mirror the source within the promoted scope.
    /// Destructive — never use for granular single-asset imports.
    /// </param>
    Task<ContentPackageImportResult> ApplyAsync(
        Guid stagingId,
        string? initiatedByUser,
        bool replaceMode = false,
        CancellationToken ct = default);

    /// <summary>
    /// Discards a staged bundle without applying. Safe to call if the stagingId
    /// is unknown (returns false).
    /// </summary>
    bool CancelPreview(Guid stagingId);
}

public class ContentPackageImportPreview
{
    public Guid StagingId { get; set; }
    public string? BundleFileName { get; set; }
    public int SchemaVersion { get; set; }
    public DateTime ExportedAt { get; set; }
    public string? ExportedByUser { get; set; }
    public string? SourceEnvironment { get; set; }
    public string? ScopeDescription { get; set; }
    public List<ContentPackageEntityDiff> EntityDiffs { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public int TotalInserts => EntityDiffs.Sum(d => d.InsertCount);
    public int TotalUpdates => EntityDiffs.Sum(d => d.UpdateCount);
    public int TotalSkips => EntityDiffs.Sum(d => d.SkipCount);
}

public class ContentPackageEntityDiff
{
    public string EntityType { get; set; } = null!;
    public int InsertCount { get; set; }
    public int UpdateCount { get; set; }
    public int SkipCount { get; set; }
}

public class ContentPackageImportResult
{
    public int ContentPackageHistoryId { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int MediaFilesCopied { get; set; }
    public string Status { get; set; } = "Success";
    public List<string> Warnings { get; set; } = new();
}
