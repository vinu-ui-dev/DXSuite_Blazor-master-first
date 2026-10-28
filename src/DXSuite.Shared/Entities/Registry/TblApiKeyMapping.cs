using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Shared.Entities.Registry;

/// <summary>
/// Central API key → tenant mapping. Replaces the O(N-tenant) scan in ConfigTenantStore
/// with an O(1) lookup. Each device API key hash is registered here alongside the tenant
/// it belongs to; the hash is the same SHA-256 value stored in the tenant's Director DB.
/// </summary>
[Table("tblApiKeyMapping")]
public class TblApiKeyMapping
{
    [Key]
    public int ApiKeyMappingId { get; set; }

    /// <summary>
    /// SHA-256 hash of the API key, Base64-encoded (44 chars). Unique index enforced by the DB.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ApiKeyHash { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string TenantId { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; }
}
