using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblUserRole")]
public partial class TblUserRole
{
    [Key]
    public int RoleId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string RoleName { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string? RoleDescription { get; set; }

    public bool IsGlobal { get; set; }

    /// <summary>
    /// For custom roles: the built-in role (RoleId 1–4) whose behavior this role
    /// inherits for all legacy RoleEnum checks. Null for the built-in roles themselves.
    /// </summary>
    public int? BaseRoleId { get; set; }

    /// <summary>
    /// True for the four built-in roles (Admin/Architect/Contributor/NewUser),
    /// which cannot be renamed or deleted.
    /// </summary>
    public bool IsSystem { get; set; }

    [ForeignKey("BaseRoleId")]
    public virtual TblUserRole? BaseRole { get; set; }

    [InverseProperty("Role")]
    public virtual ICollection<TblUser> TblUsers { get; set; } = new List<TblUser>();

    [InverseProperty("Role")]
    public virtual ICollection<TblRolePermission> Permissions { get; set; } = new List<TblRolePermission>();
}
