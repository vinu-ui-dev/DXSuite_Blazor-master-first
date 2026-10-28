using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

[Table("tblAsset")]
public partial class TblAsset
{
    [Key]
    public int AssetId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    public int LocationId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? AssetName { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string AssetPath { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string? AssetInkPath { get; set; }

    public int AssetGroupId { get; set; }

    public int AssetTypeId { get; set; }

    public double AssetHeight { get; set; }

    public double AssetWidth { get; set; }

    public int ConstraintId { get; set; }

    public double Duration { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? VideoMaskPath { get; set; }

    public int? EndpointId { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? EndpointPath { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? WebRenderMode { get; set; }

    /// <summary>
    /// When true, DirectorClient will monitor this web content's health via postMessage
    /// </summary>
    public bool EnableHealthMonitoring { get; set; }

    /// <summary>
    /// External content source URL for Ink-synced assets (CDN redirect, local URL, etc.)
    /// </summary>
    [StringLength(500)]
    [Unicode(false)]
    public string? ContentSourceUrl { get; set; }

    /// <summary>
    /// JSON column for type-specific extended properties (e.g., stream settings)
    /// </summary>
    public string? ExtendedData { get; set; }

    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// The user who owns this asset, and with <see cref="UploadedByUserGroupId"/> decides who may
    /// edit, delete and re-tag it. Null on both means the asset is managed by full-access users only.
    /// Deliberately not read from the approval record, which does not exist for every asset.
    /// </summary>
    public int? UploadedByUserId { get; set; }

    /// <summary>
    /// The group the uploader acted for, so ownership follows the group rather than the person.
    /// </summary>
    public int? UploadedByUserGroupId { get; set; }

    [ForeignKey("AssetGroupId")]
    [InverseProperty("TblAssets")]
    public virtual TblAssetGroup AssetGroup { get; set; } = null!;

    [ForeignKey("AssetTypeId")]
    [InverseProperty("TblAssets")]
    public virtual TblAssetType AssetType { get; set; } = null!;

    [ForeignKey("ConstraintId")]
    [InverseProperty("TblAssets")]
    public virtual TblConstraint Constraint { get; set; } = null!;

    [ForeignKey("LocationId")]
    [InverseProperty("TblAssets")]
    public virtual TblLocation Location { get; set; } = null!;

    [InverseProperty("OverrideAsset")]
    public virtual ICollection<TblDisplay> TblDisplays { get; set; } = new List<TblDisplay>();

    [InverseProperty("Asset")]
    public virtual ICollection<TblEventLog> TblEventLogs { get; set; } = new List<TblEventLog>();

    [InverseProperty("Asset")]
    public virtual ICollection<TblOverrideContent> OverrideContents { get; set; } = new List<TblOverrideContent>();

    /// <summary>
    /// Approval record for hotel-uploaded content (null for corporate content)
    /// </summary>
    [InverseProperty("Asset")]
    public virtual TblAssetApproval? AssetApproval { get; set; }

    [InverseProperty("Asset")]
    public virtual ICollection<TblAssetContentMetadata> ContentMetadata { get; set; } = new List<TblAssetContentMetadata>();

    [ForeignKey("UploadedByUserId")]
    public virtual TblUser? UploadedByUser { get; set; }

    [ForeignKey("UploadedByUserGroupId")]
    public virtual TblUserGroup? UploadedByUserGroup { get; set; }
}
