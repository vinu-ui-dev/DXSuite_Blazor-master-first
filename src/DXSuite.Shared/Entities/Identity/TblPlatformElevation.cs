using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Identity;

[Table("tblPlatformElevation")]
public class TblPlatformElevation
{
    [Key]
    public Guid ElevationId { get; set; }

    public Guid PlatformUserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TenantId { get; set; } = null!;

    /// <summary>
    /// The role being elevated into: TenantSupport or TenantSupportWrite.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string RoleName { get; set; } = null!;

    [MaxLength(100)]
    public string? TicketRef { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Reason { get; set; } = null!;

    /// <summary>
    /// Pending = requested, awaiting approval.
    /// Active  = approved and within the ExpiresAt window.
    /// Revoked = manually revoked before expiry.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = ElevationStatus.Pending;

    public DateTime IssuedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public Guid RequestedByPlatformUserId { get; set; }

    public Guid? ApprovedByPlatformUserId { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? RevokedByPlatformUserId { get; set; }

    /// <summary>
    /// Set when the tenant admin explicitly approves this elevation, for tenants where
    /// TblTenantSettings.RequireTenantApprovalForElevation = true. Null means tenant
    /// approval has not been recorded — ApproveElevationAsync will block activation.
    /// </summary>
    public DateTime? TenantApprovedAt { get; set; }

    [ForeignKey(nameof(PlatformUserId))]
    public TblPlatformUser PlatformUser { get; set; } = null!;
}

public static class ElevationStatus
{
    public const string Pending = "Pending";
    public const string Active  = "Active";
    public const string Revoked = "Revoked";
}
