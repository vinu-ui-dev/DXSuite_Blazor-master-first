using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Key/value pairs for user-specific metadata used in access control filtering.
/// Example keys: BuildingNo, Region
/// </summary>
[Table("tblUserMetadata")]
[Index("UserId", Name = "IX_UserMetadata_UserId")]
public partial class TblUserMetadata
{
    [Key]
    public int UserMetadataId { get; set; }

    public int UserId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string MetadataKey { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string MetadataValue { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("UserMetadata")]
    public virtual TblUser User { get; set; } = null!;
}
