using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Audit log header for one content package operation (export or import). Every run of
/// the bundle builder or importer writes exactly one row here plus one
/// <see cref="TblContentPackageHistoryItem"/> per affected record.
/// </summary>
[Table("tblContentPackageHistory")]
[Index(nameof(StartedAt))]
public partial class TblContentPackageHistory
{
    [Key]
    public int ContentPackageHistoryId { get; set; }

    /// <summary>
    /// "export", "import", "push", or "receive".
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string Direction { get; set; } = null!;

    /// <summary>
    /// "Success", "PartialSuccess", "Failed", "InProgress".
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [StringLength(255)]
    public string? BundleFileName { get; set; }

    /// <summary>
    /// UserPrincipalName of the user who initiated the operation in THIS environment.
    /// </summary>
    [StringLength(255)]
    public string? InitiatedByUser { get; set; }

    /// <summary>
    /// UserPrincipalName stamped into the manifest by the source environment (imports only).
    /// </summary>
    [StringLength(255)]
    public string? ExportedByUser { get; set; }

    [StringLength(100)]
    public string? SourceEnvironment { get; set; }

    [StringLength(500)]
    public string? ScopeDescription { get; set; }

    public int TotalRecords { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime StartedAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CompletedAt { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    [InverseProperty("ContentPackageHistory")]
    public virtual ICollection<TblContentPackageHistoryItem> Items { get; set; } = new List<TblContentPackageHistoryItem>();
}
