using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Per-record audit line for a single content package run. Captures what was done to
/// each ContentPackageId so operators can trace "did this specific asset actually move?"
/// without reconstructing state from the bundle file.
/// </summary>
[Table("tblContentPackageHistoryItem")]
[Index(nameof(ContentPackageHistoryId))]
[Index(nameof(ContentPackageId))]
public partial class TblContentPackageHistoryItem
{
    [Key]
    public int ContentPackageHistoryItemId { get; set; }

    public int ContentPackageHistoryId { get; set; }

    public Guid ContentPackageId { get; set; }

    /// <summary>
    /// Short entity type tag ("Location", "Display", "Asset", ...) matching the
    /// manifest DTO names so audit queries can group/filter by kind.
    /// </summary>
    [StringLength(50)]
    [Unicode(false)]
    public string EntityType { get; set; } = null!;

    /// <summary>
    /// "insert", "update", "skip".
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string Action { get; set; } = null!;

    /// <summary>
    /// UserPrincipalName originally recorded as the record's creator/editor in the
    /// source environment. Kept verbatim even when the target has no matching user.
    /// </summary>
    [StringLength(255)]
    public string? SourceUserPrincipalName { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [ForeignKey("ContentPackageHistoryId")]
    [InverseProperty("Items")]
    public virtual TblContentPackageHistory ContentPackageHistory { get; set; } = null!;
}
