using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Authorization;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblUser")]
public partial class TblUser : ICurrentUser
{
    [Key]
    public int UserId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? FriendlyName { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? GivenName { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Surname { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastLoginTime { get; set; }

    [StringLength(128)]
    [Unicode(false)]
    public string? UserPrincipalName { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Title { get; set; }

    public int RoleId { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string? AirlineCode { get; set; }

    public bool PulseAccess { get; set; }

    public bool IsDisabled { get; set; }

    /// <summary>
    /// JSON for flexible user properties like hotel assignments
    /// Example: {"assignedHotelNumbers": ["12345", "67890"]}
    /// </summary>
    public string? ExtendedData { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [ForeignKey("RoleId")]
    [InverseProperty("TblUsers")]
    public virtual TblUserRole Role { get; set; } = null!;

    [InverseProperty("User")]
    public virtual ICollection<TblUserGroupMembership> UserGroupMemberships { get; set; } = new List<TblUserGroupMembership>();

    [InverseProperty("User")]
    public virtual ICollection<TblUserMetadata> UserMetadata { get; set; } = new List<TblUserMetadata>();

    // Explicit ICurrentUser implementation for non-nullable interface members
    string ICurrentUser.UserPrincipalName => UserPrincipalName ?? string.Empty;
}
