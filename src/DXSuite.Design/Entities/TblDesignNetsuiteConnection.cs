using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignNetsuiteConnection")]
public class TblDesignNetsuiteConnection
{
    [Key]
    public int ConnectionId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? AccountId { get; set; }

    [MaxLength(500)]
    public string? ConsumerKey { get; set; }

    /// <summary>Stored encrypted with ENC: prefix via NetsuiteCredentialProtector.</summary>
    [MaxLength(2000)]
    public string? ConsumerSecret { get; set; }

    [MaxLength(500)]
    public string? TokenId { get; set; }

    /// <summary>Stored encrypted with ENC: prefix via NetsuiteCredentialProtector.</summary>
    [MaxLength(2000)]
    public string? TokenSecret { get; set; }

    /// <summary>
    /// NS internal ID of the custom form used when creating estimates (e.g. "163").
    /// NetSuite accounts can have multiple estimate form templates; this pins the one
    /// configured for this integration. Null = use NS account default.
    /// </summary>
    [MaxLength(50)]
    public string? CustomFormId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastEditDate { get; set; }
}
