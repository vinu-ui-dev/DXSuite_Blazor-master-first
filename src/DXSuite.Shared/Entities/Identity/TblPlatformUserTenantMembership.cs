using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblPlatformUserTenantMembership")]
public class TblPlatformUserTenantMembership
{
    [Key]
    public Guid MembershipId { get; set; }

    public Guid PlatformUserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TenantId { get; set; } = null!;

    /// <summary>
    /// The UserId (int PK) of the corresponding TblUser row in the tenant's Director database.
    /// Null for operator memberships bootstrapped before the user has logged into that tenant.
    /// </summary>
    public int? TenantUserId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, this user can approve/reject operator elevation requests targeting this tenant
    /// and manage tenant security settings. Scoped to this specific tenant membership.
    /// </summary>
    public bool IsTenantAdmin { get; set; } = false;

    public DateTime GrantedDate { get; set; }

    /// <summary>
    /// Null when bootstrapped automatically on first login; set when provisioned by an operator.
    /// </summary>
    public Guid? GrantedByPlatformUserId { get; set; }

    [ForeignKey(nameof(PlatformUserId))]
    public TblPlatformUser PlatformUser { get; set; } = null!;
}
