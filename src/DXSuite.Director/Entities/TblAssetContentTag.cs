using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

/// <summary>
/// Associates uploaded content with metadata for filtering.
/// Ties an asset to a specific metadata scope (e.g., BuildingNo=A).
/// An asset may hold several values for one key, one row each; the same value twice is refused.
/// </summary>
[Table("tblAssetContentMetadata")]
[Index(nameof(AssetId), nameof(MetadataKey), Name = "IX_ContentMetadata_AssetKey")]
[Index(nameof(AssetId), nameof(MetadataKey), nameof(MetadataValue), IsUnique = true, Name = "UQ_ContentMetadata_AssetKeyValue")]
public partial class TblAssetContentMetadata
{
    [Key]
    public int AssetContentMetadataId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    public int AssetId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string MetadataKey { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string MetadataValue { get; set; } = null!;

    public int? SetByUserId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("AssetId")]
    [InverseProperty("ContentMetadata")]
    public virtual TblAsset Asset { get; set; } = null!;

    [ForeignKey("SetByUserId")]
    public virtual TblUser? SetByUser { get; set; }
}
