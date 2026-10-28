using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Personal access token for the read-only Design MCP endpoint. Only the SHA-256 hash of the
/// raw token (dxd_-prefixed) is stored; the raw value is shown once at creation.
/// </summary>
[Table("tblDesignMcpToken")]
public class TblDesignMcpToken
{
    [Key]
    public int TokenId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>Identity user id as string — no FK, identity lives in a different database.</summary>
    [Required, MaxLength(100)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>User-chosen label, e.g. "Claude desktop".</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the raw token.</summary>
    [Required, MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;

    /// <summary>Updated at most once per validation window, not on every request.</summary>
    public DateTime? LastUsedDate { get; set; }

    public DateTime ExpiresDate { get; set; }

    public bool IsRevoked { get; set; }
}
