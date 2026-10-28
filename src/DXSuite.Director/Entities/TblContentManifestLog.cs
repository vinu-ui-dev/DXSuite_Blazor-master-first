using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Append-only record of what content a manifest bucket served over time. A row is written
/// whenever a rebuilt manifest carries a new version hash, so each row's effective range
/// runs from EffectiveFromUtc until the bucket's next row. Playlist and schedule edits
/// overwrite their source data in place, so this log is the only reconstructable history
/// of what was scheduled to play.
/// </summary>
[Table("tblContentManifestLog")]
[Index(nameof(BucketType), nameof(BucketId), nameof(FilterValue), nameof(EffectiveFromUtc), Name = "IX_tblContentManifestLog_Bucket")]
public class TblContentManifestLog
{
    [Key]
    public int ContentManifestLogId { get; set; }

    /// <summary>"location" (playlist manifests) or "display" (schedule manifests)</summary>
    [Required]
    [StringLength(10)]
    [Unicode(false)]
    public string BucketType { get; set; } = null!;

    public int BucketId { get; set; }

    /// <summary>
    /// Metadata filter variant the manifest was built for. Always "all" now that a publication is
    /// shared by every device at its bucket; rows from when filtered locations published one
    /// manifest per value keep that value.
    /// </summary>
    [Required]
    [StringLength(255)]
    public string FilterValue { get; set; } = "all";

    [Required]
    [StringLength(16)]
    [Unicode(false)]
    public string Version { get; set; } = null!;

    /// <summary>Distinct items with per-loop play counts and schedule windows, as JSON.</summary>
    public string ItemsJson { get; set; } = "[]";

    public int ItemCount { get; set; }

    /// <summary>Total plays per loop (expanded weight sequence length).</summary>
    public int PlayCount { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime EffectiveFromUtc { get; set; }
}
