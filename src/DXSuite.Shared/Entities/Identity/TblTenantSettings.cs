using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Identity;

/// <summary>
/// Per-tenant opt-in security controls for platform operator access.
/// A row only exists for tenants that have customised the defaults.
/// </summary>
[Table("tblTenantSettings")]
public class TblTenantSettings
{
    [Key]
    public int TenantSettingsId { get; set; }

    [Required]
    [MaxLength(100)]
    public string TenantId { get; set; } = null!;

    /// <summary>
    /// When true, elevation requests for this tenant require explicit approval by a tenant admin
    /// before the elevation becomes active — in addition to the standard operator approval.
    /// </summary>
    public bool RequireTenantApprovalForElevation { get; set; }

    /// <summary>
    /// Caps the maximum elevation duration in minutes. Null means no cap beyond the system default.
    /// </summary>
    public int? ElevationMaxDurationMinutes { get; set; }

    /// <summary>
    /// JSON array of email addresses to notify when an elevation is issued for this tenant.
    /// </summary>
    [MaxLength(2000)]
    public string? NotifyOnElevationEmailsJson { get; set; }

    /// <summary>
    /// JSON array of ISO 3166-1 alpha-2 country codes from which operator access is blocked.
    /// </summary>
    [MaxLength(500)]
    public string? BlockedRegionsJson { get; set; }
}
