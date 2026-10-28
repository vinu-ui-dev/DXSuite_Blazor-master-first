using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblUserGroupMembership")]
[Index(nameof(UserGroupId), nameof(UserId), IsUnique = true)]
public partial class TblUserGroupMembership
{
    [Key]
    public int MembershipId { get; set; }

    public int UserGroupId { get; set; }

    public int UserId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime AddedDate { get; set; }

    [ForeignKey("UserGroupId")]
    [InverseProperty("Memberships")]
    public virtual TblUserGroup UserGroup { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("UserGroupMemberships")]
    public virtual TblUser User { get; set; } = null!;
}
