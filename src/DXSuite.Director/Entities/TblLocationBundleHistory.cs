using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Audit log for location bundle lifecycle operations. BundleKey/Version are stored as
/// plain strings (not FKs) so history survives bundle deletion.
/// </summary>
[Table("tblLocationBundleHistory")]
[Index(nameof(StartedAt))]
public partial class TblLocationBundleHistory
{
    [Key]
    public int LocationBundleHistoryId { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string BundleKey { get; set; } = null!;

    [StringLength(32)]
    [Unicode(false)]
    public string? Version { get; set; }

    /// <summary>
    /// "install", "update", "delete", "rollback", or "dev-push".
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string Action { get; set; } = null!;

    /// <summary>
    /// "Success", "Failed", "InProgress".
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    /// <summary>
    /// UserPrincipalName of the initiating user.
    /// </summary>
    [StringLength(255)]
    public string? InitiatedByUser { get; set; }

    [StringLength(2000)]
    public string? Message { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime StartedAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CompletedAt { get; set; }
}
