using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

/// <summary>
/// A tenant-scoped, multi-use enrollment token. Lets agents self-register (8E): an installer is given
/// the token once, and each machine claims its own gateway identity + API key on first run. Only the
/// SHA-256 hash is stored; the token value is shown once at creation.
/// </summary>
[Table("SentinelEnrollmentToken")]
public partial class SentinelEnrollmentToken
{
    [Key]
    [Column("TokenID")]
    public int TokenId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("Label")]
    [StringLength(100)]
    [Unicode(false)]
    public string Label { get; set; } = null!;

    /// <summary>Base64 SHA-256 of the token value (matches GatewayApiKeyHelper.ComputeApiKeyHash).</summary>
    [Column("TokenHash")]
    [StringLength(100)]
    [Unicode(false)]
    public string TokenHash { get; set; } = null!;

    /// <summary>Comma-separated roles assigned to agents enrolled with this token, e.g. "selfmonitor,tasks".</summary>
    [Column("DefaultRoles")]
    [StringLength(100)]
    [Unicode(false)]
    public string? DefaultRoles { get; set; }

    [Column("ExpiresUtc", TypeName = "datetime")]
    public DateTime? ExpiresUtc { get; set; }

    /// <summary>Maximum number of agents that may enroll with this token; null = unlimited.</summary>
    [Column("MaxUses")]
    public int? MaxUses { get; set; }

    [Column("UseCount")]
    public int UseCount { get; set; }

    [Column("IsRevoked")]
    public bool IsRevoked { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("CreatedBy")]
    [StringLength(50)]
    [Unicode(false)]
    public string? CreatedBy { get; set; }
}
