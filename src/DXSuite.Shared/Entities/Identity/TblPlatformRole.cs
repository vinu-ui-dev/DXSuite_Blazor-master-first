using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblPlatformRole")]
public class TblPlatformRole
{
    [Key]
    public int RoleId { get; set; }

    [Required]
    [MaxLength(50)]
    public string RoleName { get; set; } = null!;

    [MaxLength(256)]
    public string? Description { get; set; }

    public ICollection<TblPlatformUserRole> UserRoles { get; set; } = new List<TblPlatformUserRole>();
}

/// <summary>
/// Well-known role names stored in TblPlatformRole. Seeded at startup.
/// </summary>
public static class PlatformRoles
{
    public const string PlatformOperator   = "PlatformOperator";
    public const string TenantSupport      = "TenantSupport";
    public const string TenantSupportWrite = "TenantSupportWrite";
    public const string Auditor            = "Auditor";
}
