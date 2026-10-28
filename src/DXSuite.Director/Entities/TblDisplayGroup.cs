using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblDisplayGroup")]
public partial class TblDisplayGroup
{
    [Key]
    public int DisplayGroupId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert records
    /// when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    [StringLength(50)]
    [Unicode(false)]
    public string? DisplayGroupName { get; set; }

    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [InverseProperty("DisplayGroup")]
    public virtual ICollection<TblDisplay> TblDisplays { get; set; } = new List<TblDisplay>();
}
