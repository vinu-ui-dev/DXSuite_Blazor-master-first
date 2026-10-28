using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Grants a single permission key to a role. Keys use the schemes
/// "settings-tab:{key}", "config-tab:{key}", "feature:{name}", and "page:{name}"
/// (see DXSuite.Shared.Authorization.PermissionKeys). The Admin role (RoleId 1)
/// has no rows here — it is bypassed in code and always has every permission.
/// </summary>
[Table("tblRolePermission")]
public partial class TblRolePermission
{
    [Key]
    public int RolePermissionId { get; set; }

    public int RoleId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string PermissionKey { get; set; } = null!;

    [ForeignKey("RoleId")]
    [InverseProperty("Permissions")]
    public virtual TblUserRole Role { get; set; } = null!;
}
