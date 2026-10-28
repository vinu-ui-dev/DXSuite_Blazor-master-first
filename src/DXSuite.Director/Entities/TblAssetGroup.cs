using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblAssetGroup")]
public partial class TblAssetGroup
{
    [Key]
    public int AssetGroupId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    [StringLength(50)]
    [Unicode(false)]
    public string? AssetGroupName { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [InverseProperty("AssetGroup")]
    public virtual ICollection<TblAsset> TblAssets { get; set; } = new List<TblAsset>();
}
