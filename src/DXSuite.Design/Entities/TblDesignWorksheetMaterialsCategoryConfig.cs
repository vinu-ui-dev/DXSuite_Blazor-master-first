using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Per-worksheet, per-category configuration for the Materials section:
/// - PrintInPdf controls the custcol_acs_pdf_view flag on the category's rolled-up NS push line.
/// - IsDescEnabled / Description* fields control an optional description row pushed before the line.
/// Rows are created on first toggle and upserted on subsequent changes.
/// </summary>
[Table("tblDesignWorksheetMaterialsCategoryConfig")]
public class TblDesignWorksheetMaterialsCategoryConfig
{
    [Key]
    public int Id { get; set; }

    public int WorksheetId { get; set; }
    public int CategoryId { get; set; }

    /// <summary>When true, custcol_acs_pdf_view is set on the category's NS push line.</summary>
    public bool PrintInPdf { get; set; }

    /// <summary>When true, a description row is pushed immediately before the category's line item.</summary>
    public bool IsDescEnabled { get; set; }

    [MaxLength(100)]
    public string? DescriptionItemId { get; set; }

    [MaxLength(300)]
    public string? DescriptionItemName { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? LocationId { get; set; }

    [MaxLength(300)]
    public string? LocationName { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public virtual TblDesignMaterialsCategory? Category { get; set; }
}
