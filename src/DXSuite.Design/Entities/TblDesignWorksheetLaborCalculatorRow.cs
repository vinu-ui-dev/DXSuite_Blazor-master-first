using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Per-worksheet person count for a labor type × column pair.
/// PersonCount multiplies the base hours from category/item assignments so that
/// staffing level (e.g. 2 cable techs) is reflected in the cost model without
/// requiring the user to edit every item's hours.
/// </summary>
[Table("tblDesignWorksheetLaborCalculatorRow")]
public class TblDesignWorksheetLaborCalculatorRow
{
    [Key]
    public int Id { get; set; }

    public int WorksheetId { get; set; }
    public int LaborTypeId { get; set; }
    public int ColumnId { get; set; }

    /// <summary>Number of people assigned to this labor type for this column on this worksheet.</summary>
    public int PersonCount { get; set; } = 1;

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }

    [ForeignKey(nameof(LaborTypeId))]
    public virtual TblDesignLaborType? LaborType { get; set; }

    [ForeignKey(nameof(ColumnId))]
    public virtual TblDesignLaborColumn? Column { get; set; }
}
