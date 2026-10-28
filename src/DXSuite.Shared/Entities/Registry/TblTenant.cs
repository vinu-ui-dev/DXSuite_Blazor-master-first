using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Registry;

/// <summary>
/// Central tenant registry row. One row per tenant; the single source of truth for
/// connection strings, auth config, enabled modules, and feature flags.
/// Connection strings should be encrypted at rest via infrastructure controls
/// (e.g. Azure SQL Always Encrypted or Key Vault column encryption references).
/// </summary>
[Table("tblTenant")]
public class TblTenant
{
    [Key]
    [MaxLength(100)]
    public string TenantId { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Subdomain { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string CustomerCode { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;

    [MaxLength(1000)]
    public string? ConnectionString_Director { get; set; }

    [MaxLength(1000)]
    public string? ConnectionString_Sentinel { get; set; }

    [MaxLength(1000)]
    public string? ConnectionString_CoreData { get; set; }

    [MaxLength(1000)]
    public string? ConnectionString_Aviation { get; set; }

    [MaxLength(1000)]
    public string? ConnectionString_Design { get; set; }

    /// <summary>
    /// JSON-serialized TenantAuthConfig. Null = use the top-level Authentication:Providers config.
    /// </summary>
    public string? AuthConfigJson { get; set; }

    /// <summary>
    /// JSON-serialized TenantModuleConfig. Null = all modules default to disabled.
    /// </summary>
    public string? ModulesJson { get; set; }

    /// <summary>
    /// JSON-serialized TenantFeatureFlags. Null = all features default to disabled.
    /// </summary>
    public string? FeaturesJson { get; set; }

    /// <summary>
    /// JSON-serialized SmtpConfig. Null = use the system-default SMTP settings.
    /// </summary>
    public string? SmtpConfigJson { get; set; }

    /// <summary>
    /// Override for the tenant's media root path. Null = derive from subdomain.
    /// </summary>
    [MaxLength(500)]
    public string? FilePaths_MediaRoot { get; set; }

    /// <summary>
    /// Absolute base URL for this tenant (e.g. https://demo.dxsuite.com), used when generating
    /// URLs outside an active request. Null = fall back to the global FilePaths:SiteUrl config.
    /// </summary>
    [MaxLength(500)]
    public string? SiteUrl { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime LastEditDate { get; set; }

    /// <summary>
    /// Maximum number of licensed users for this tenant. Null = unlimited.
    /// </summary>
    public int? MaxUsers { get; set; }
}
