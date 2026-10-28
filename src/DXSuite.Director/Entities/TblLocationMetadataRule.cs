using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One registry key participating in a location's content metadata rules. Participation is the row
/// existing: removing a key deletes the row and leaves every asset tag for that key in place, so
/// adding the key back restores the targeting it had. The row never holds a value; values belong to
/// assets, devices, users and groups.
/// </summary>
[Table("tblLocationMetadataRule")]
[Index(nameof(LocationId), nameof(MetadataKey), IsUnique = true, Name = "UX_tblLocationMetadataRule_LocationKey")]
public partial class TblLocationMetadataRule
{
    [Key]
    public int LocationMetadataRuleId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    public int LocationId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string MetadataKey { get; set; } = null!;

    /// <summary>
    /// How an asset holding no value for this key is treated: <c>all</c> or <c>none</c>. The truth
    /// table in the metadata content targeting spec, section 3.4, gives each combination's verdict.
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string UntaggedContentPolicy { get; set; } = "all";

    /// <summary>
    /// How a device holding no value for this key is treated: <c>all</c>, <c>untaggedOnly</c> or
    /// <c>none</c>. Read by targeting only; scope never consults it.
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string UntaggedPlayerPolicy { get; set; } = "all";

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastEditUser { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("MetadataRules")]
    public virtual TblLocation Location { get; set; } = null!;
}
