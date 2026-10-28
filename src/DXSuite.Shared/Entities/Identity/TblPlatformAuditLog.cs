using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Append-only audit record for platform-level events. In production, the underlying
/// SQL table should be created with LEDGER = ON for tamper-evident integrity.
/// The application role on the Platform DB must have INSERT-only on this table.
/// </summary>
[Table("tblPlatformAuditLog")]
public class TblPlatformAuditLog
{
    [Key]
    public Guid AuditId { get; set; }

    public DateTime EventTime { get; set; }

    public Guid? PlatformUserId { get; set; }

    [MaxLength(100)]
    public string? TenantId { get; set; }

    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = null!;

    [MaxLength(512)]
    public string? ResourcePath { get; set; }

    [MaxLength(100)]
    public string? TicketRef { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    public string? DetailsJson { get; set; }
}

/// <summary>
/// Well-known audit event types.
/// </summary>
public static class AuditEventType
{
    public const string TenantCreated             = "TenantCreated";
    public const string ElevationRequested        = "ElevationRequested";
    public const string ElevationApproved         = "ElevationApproved";
    public const string ElevationUsed             = "ElevationUsed";
    public const string ElevationRevoked          = "ElevationRevoked";
    public const string TenantDataRead            = "TenantDataRead";
    public const string TenantDataModified        = "TenantDataModified";
    public const string MembershipGranted         = "MembershipGranted";
    public const string MembershipRevoked         = "MembershipRevoked";
    public const string RoleChanged               = "RoleChanged";
    public const string TenantAdminGranted        = "TenantAdminGranted";
    public const string TenantAdminRevoked        = "TenantAdminRevoked";
    public const string TenantSettingsUpdated     = "TenantSettingsUpdated";
    public const string TenantProvisioned         = "TenantProvisioned";
    public const string TenantDeprovisioned       = "TenantDeprovisioned";
    public const string TenantElevationApproved   = "TenantElevationApproved";
    public const string TenantElevationRejected   = "TenantElevationRejected";
}
