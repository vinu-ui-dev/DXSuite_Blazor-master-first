using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblPlatformUser")]
public class TblPlatformUser
{
    [Key]
    public Guid PlatformUserId { get; set; }

    [Required]
    [MaxLength(256)]
    public string PrimaryEmail { get; set; } = null!;

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    public bool IsPlatformOperator { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? LastLoginDate { get; set; }

    public ICollection<TblPlatformUserTenantMembership> TenantMemberships { get; set; } = new List<TblPlatformUserTenantMembership>();
}
