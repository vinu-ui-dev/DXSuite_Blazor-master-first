using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One uploaded version of a location bundle. Rows are immutable after import — the
/// stored files at location-bundles/{key}/{version}/ are never rewritten, which is what
/// makes long-lived immutable cache headers on the serving endpoint safe.
/// </summary>
[Table("tblLocationBundleVersion")]
[Index(nameof(LocationBundleId), nameof(Version), IsUnique = true)]
public partial class TblLocationBundleVersion
{
    [Key]
    public int LocationBundleVersionId { get; set; }

    public int LocationBundleId { get; set; }

    /// <summary>
    /// Semver from the manifest. Used as a storage path segment, so charset-validated at import.
    /// </summary>
    [StringLength(32)]
    [Unicode(false)]
    public string Version { get; set; } = null!;

    /// <summary>
    /// Relative path of the entry HTML file inside the bundle (manifest "entry").
    /// </summary>
    [StringLength(255)]
    [Unicode(false)]
    public string EntryPath { get; set; } = null!;

    /// <summary>
    /// Raw manifest.json — the source of truth for capabilities and settingsSchema.
    /// </summary>
    public string ManifestJson { get; set; } = null!;

    public int FileCount { get; set; }

    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// SHA-256 of the uploaded zip, for support diagnostics and duplicate detection.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string ContentHash { get; set; } = null!;

    [StringLength(255)]
    public string? UploadedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime UploadDate { get; set; }

    [ForeignKey("LocationBundleId")]
    [InverseProperty("Versions")]
    public virtual TblLocationBundle LocationBundle { get; set; } = null!;
}
