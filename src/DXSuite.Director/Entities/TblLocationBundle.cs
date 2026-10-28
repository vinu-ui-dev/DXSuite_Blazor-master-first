using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Registry entry for an uploaded location bundle (HTML/JS/CSS package rendered by
/// custom-bundle location types). One row per bundle; individual uploads live in
/// <see cref="TblLocationBundleVersion"/>. Locations reference bundles by
/// <see cref="BundleKey"/> (stored in TblLocation.ExtendedData), never by PK, so
/// references survive export/import across environments.
/// </summary>
[Table("tblLocationBundle")]
[Index(nameof(BundleKey), IsUnique = true)]
public partial class TblLocationBundle
{
    [Key]
    public int LocationBundleId { get; set; }

    /// <summary>
    /// Stable identifier from the bundle manifest's "id" field. Used as a storage path
    /// segment, so restricted to lowercase alphanumerics, hyphen, and underscore.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string BundleKey { get; set; } = null!;

    [StringLength(100)]
    public string Name { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Version served to locations that do not pin one. Must match a
    /// <see cref="TblLocationBundleVersion.Version"/> row; flipping this pointer is how
    /// rollback works.
    /// </summary>
    [StringLength(32)]
    [Unicode(false)]
    public string CurrentVersion { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(255)]
    public string? LastEditUser { get; set; }

    [InverseProperty("LocationBundle")]
    public virtual ICollection<TblLocationBundleVersion> Versions { get; set; } = new List<TblLocationBundleVersion>();
}
