using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignWorksheetLaborRow")]
public class TblDesignWorksheetLaborRow
{
    [Key]
    public int LaborRowId { get; set; }

    public int WorksheetId { get; set; }
    public int LaborTypeId { get; set; }

    /// <summary>Sum of (qty × hours) for all line items whose category assigns this labor type.</summary>
    public decimal CalculatedHours { get; set; }

    /// <summary>Manual override value when IsOverridden is true.</summary>
    public decimal OverrideHours { get; set; }

    /// <summary>When true, OverrideHours is used instead of CalculatedHours for scenario pricing.</summary>
    public bool IsOverridden { get; set; }

    /// <summary>Sub cost rate per hour for this labor type on this worksheet, set in the Labor Calculator.</summary>
    public decimal SubCostPerHr { get; set; }

    /// <summary>Sub price rate per hour for this labor type on this worksheet, set in the Labor Calculator.</summary>
    public decimal SubPricePerHr { get; set; }

    /// <summary>
    /// Number of staff assigned to this labor type on this worksheet.
    /// Column-based hours are divided proportionally by staff count across all column-based types.
    /// </summary>
    public int PersonCount { get; set; } = 1;

    /// <summary>When true, custcol_acs_pdf_view is set on the NetSuite estimate line so the row appears in the PDF export.</summary>
    public bool PrintInPdf { get; set; }

    /// <summary>NS location override for this row. Null means use the worksheet default location.</summary>
    [MaxLength(100)]
    public string? LocationId { get; set; }

    [MaxLength(300)]
    public string? LocationName { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }

    [ForeignKey(nameof(LaborTypeId))]
    public virtual TblDesignLaborType? LaborType { get; set; }
}
