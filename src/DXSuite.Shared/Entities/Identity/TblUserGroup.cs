using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblUserGroup")]
[Index(nameof(GroupName), IsUnique = true)]
public partial class TblUserGroup
{
    [Key]
    public int UserGroupId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string GroupName { get; set; } = null!;

    [StringLength(500)]
    [Unicode(false)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Group purpose: "director" (manually managed) or "ad-sync" (Azure AD synced)
    /// </summary>
    [StringLength(30)]
    [Unicode(false)]
    public string GroupType { get; set; } = "director";

    /// <summary>
    /// Optional external identifier (e.g., Active Directory SID)
    /// </summary>
    [StringLength(255)]
    [Unicode(false)]
    public string? ExternalId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    [InverseProperty("UserGroup")]
    public virtual ICollection<TblUserGroupMembership> Memberships { get; set; } = new List<TblUserGroupMembership>();

    [InverseProperty("UserGroup")]
    public virtual ICollection<TblUserGroupMetadata> GroupMetadata { get; set; } = new List<TblUserGroupMetadata>();
}
