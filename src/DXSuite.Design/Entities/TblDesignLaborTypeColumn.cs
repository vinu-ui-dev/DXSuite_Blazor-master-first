using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// Junction table linking a labor type to the columns it contributes hours toward.
/// A labor type with no column assignments is treated as manually-entered (no auto-calculation).
/// </summary>
[Table("tblDesignLaborTypeColumn")]
public class TblDesignLaborTypeColumn
{
    [Key]
    public int Id { get; set; }

    public int LaborTypeId { get; set; }
    public int ColumnId { get; set; }

    // Navigation
    [ForeignKey(nameof(LaborTypeId))]
    public virtual TblDesignLaborType? LaborType { get; set; }

    [ForeignKey(nameof(ColumnId))]
    public virtual TblDesignLaborColumn? Column { get; set; }
}
