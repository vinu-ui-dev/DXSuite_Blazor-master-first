using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// A named work-type column (e.g. "System Build", "Cabling & Termination") shared across the catalog
/// and worksheet. SortOrder controls left-to-right column order in the worksheet hardware table.
/// </summary>
[Table("tblDesignLaborColumn")]
public class TblDesignLaborColumn
{
    [Key]
    public int ColumnId { get; set; }

    [Required, MaxLength(100)]
    public string CustomerId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Controls left-to-right display order in the worksheet equipment table.</summary>
    public int SortOrder { get; set; }

    // Navigation
    public virtual ICollection<TblDesignLaborTypeColumn> LaborTypeColumns { get; set; } = [];
}
