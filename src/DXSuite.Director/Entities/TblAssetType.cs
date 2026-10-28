using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblAssetType")]
public partial class TblAssetType
{
    [Key]
    public int AssetTypeId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? AssetTypeName { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [InverseProperty("AssetType")]
    public virtual ICollection<TblAsset> TblAssets { get; set; } = new List<TblAsset>();
}
