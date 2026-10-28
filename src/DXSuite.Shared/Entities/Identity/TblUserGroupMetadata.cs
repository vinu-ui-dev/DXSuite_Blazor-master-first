using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Key/value pairs for group-specific metadata used in access control filtering.
/// Example keys: BuildingNo, Region
/// </summary>
[Table("tblUserGroupMetadata")]
[Index("UserGroupId", Name = "IX_UserGroupMetadata_UserGroupId")]
public partial class TblUserGroupMetadata
{
    [Key]
    public int UserGroupMetadataId { get; set; }

    public int UserGroupId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string MetadataKey { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string MetadataValue { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("UserGroupId")]
    [InverseProperty("GroupMetadata")]
    public virtual TblUserGroup UserGroup { get; set; } = null!;
}
