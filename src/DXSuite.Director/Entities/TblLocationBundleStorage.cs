using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// Per-location key-value storage exposed to location bundles through the Location
/// Content API. Lets custom locations persist their own state without schema changes.
/// Values are opaque to the host; size and key-count caps are enforced app-side.
/// </summary>
[Table("tblLocationBundleStorage")]
[Index(nameof(LocationId), nameof(StorageKey), IsUnique = true)]
public partial class TblLocationBundleStorage
{
    [Key]
    public int LocationBundleStorageId { get; set; }

    public int LocationId { get; set; }

    [StringLength(128)]
    [Unicode(false)]
    public string StorageKey { get; set; } = null!;

    public string Value { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime LastEditDate { get; set; }

    [StringLength(255)]
    public string? LastEditUser { get; set; }

    [ForeignKey("LocationId")]
    public virtual TblLocation Location { get; set; } = null!;
}
