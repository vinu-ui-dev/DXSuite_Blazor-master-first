using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblConstraint")]
public partial class TblConstraint
{
    [Key]
    public int ConstraintId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    [StringLength(50)]
    [Unicode(false)]
    public string? ConstraintName { get; set; }

    /// <summary>
    /// The location that owns this constraint (for Asset Library global constraints)
    /// </summary>
    public int? OwningLocationId { get; set; }

    public bool IsGlobal { get; set; }

    public int? ResolutionWidth { get; set; }

    public int? ResolutionHeight { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string ResolutionMode { get; set; } = "aspect-ratio";

    /// <summary>
    /// JSON for constraint-specific settings (content tools, upload toggles, etc.)
    /// </summary>
    public string? ExtendedData { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [ForeignKey("OwningLocationId")]
    public virtual TblLocation? OwningLocation { get; set; }

    [InverseProperty("Constraint")]
    public virtual ICollection<TblAsset> TblAssets { get; set; } = new List<TblAsset>();

    [InverseProperty("Constraint")]
    public virtual ICollection<TblDisplay> TblDisplays { get; set; } = new List<TblDisplay>();
}
