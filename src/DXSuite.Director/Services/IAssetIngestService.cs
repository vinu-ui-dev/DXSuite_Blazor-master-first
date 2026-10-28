using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DXSuite.Director.Services;

/// <summary>
/// Canonical asset ingest pipeline: file save under MediaAssets, XML metadata sidecar,
/// video thumbnail, TblAsset row, content-metadata tagging, and approval-chain
/// submission. Produces artifacts identical to MediaAssetUploader.razor's upload path
/// so content ingested through the Location Content API is indistinguishable from
/// content uploaded through the Blazor UI.
/// </summary>
public interface IAssetIngestService
{
    Task<AssetIngestResult> IngestAsync(AssetIngestRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes an asset's file, sidecar, thumbnail, and row, withdrawing any pending
    /// approval. Caller is responsible for authorization.
    /// </summary>
    Task<bool> DeleteAssetAsync(int assetId, CancellationToken ct = default);
}

public class AssetIngestRequest
{
    public int LocationId { get; set; }

    /// <summary>0 = no constraint subfolder.</summary>
    public int ConstraintId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public Stream Content { get; set; } = null!;

    /// <summary>The user the upload is made by, who owns the asset. 0 when no user is behind it, and the asset is then unowned.</summary>
    public int UploaderUserId { get; set; }

    /// <summary>
    /// The group the uploader is acting for, which becomes the asset's owning group and supplies its tags.
    /// Null means the uploader acts for no group. Checked against current rules and membership before
    /// anything is written.
    /// </summary>
    public int? UploadingAsUserGroupId { get; set; }

    /// <summary>
    /// Explicit tags. A participating key named here keeps these values; any other participating key is
    /// tagged from the uploading identity.
    /// </summary>
    public Dictionary<string, string>? CustomMetadata { get; set; }

    /// <summary>When true, the asset enters the approval chain as Pending.</summary>
    public bool EnableContentApproval { get; set; }

    /// <summary>Recorded in the approval row's ExtendedData for audit (e.g. "location-content-api").</summary>
    public string Source { get; set; } = "api";
}

public class AssetIngestResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>Set when the upload was refused because the uploading identity is not one the user holds.</summary>
    public UploadCandidates? IdentityCandidates { get; set; }

    public int AssetId { get; set; }
    public string? AssetPath { get; set; }

    /// <summary>Null when approval is not enabled; 0 (Pending) otherwise.</summary>
    public int? ApprovalStatus { get; set; }
}
