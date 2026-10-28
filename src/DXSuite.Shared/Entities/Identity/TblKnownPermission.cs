using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Records every permission key that has ever been discovered, so that default
/// grants for built-in roles are applied exactly once per key. A key present here
/// but absent from tblRolePermission means an admin deliberately revoked it —
/// it must not be re-granted when the app restarts or a module re-registers the tab.
/// </summary>
[Table("tblKnownPermission")]
public partial class TblKnownPermission
{
    [Key]
    [StringLength(100)]
    [Unicode(false)]
    public string PermissionKey { get; set; } = null!;

    public DateTime DiscoveredDate { get; set; }
}
