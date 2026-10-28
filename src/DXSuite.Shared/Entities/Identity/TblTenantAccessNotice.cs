using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Tenant-visible projection of elevation and access events.
/// Surfaced in each tenant's admin UI as the Vendor Access Log.
/// Written alongside TblPlatformAuditLog — scoped to the affected tenant.
/// </summary>
[Table("tblTenantAccessNotice")]
public class TblTenantAccessNotice
{
    [Key]
    public Guid NoticeId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TenantId { get; set; } = null!;

    public DateTime EventTime { get; set; }

    /// <summary>
    /// Operator display name — not a full identity to avoid leaking org chart.
    /// </summary>
    [MaxLength(100)]
    public string? OperatorDisplayName { get; set; }

    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = null!;

    public Guid? ElevationId { get; set; }

    [MaxLength(100)]
    public string? TicketRef { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    [MaxLength(512)]
    public string? ResourcePath { get; set; }
}
