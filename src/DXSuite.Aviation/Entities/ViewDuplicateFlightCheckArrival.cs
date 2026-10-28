using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Keyless]
public partial class ViewDuplicateFlightCheckArrival
{
    [Column("FLT")]
    [StringLength(20)]
    [Unicode(false)]
    public string? Flt { get; set; }

    [Column("STADate")]
    public DateOnly? Stadate { get; set; }

    [Column("minModifiedDate", TypeName = "datetime")]
    public DateTime? MinModifiedDate { get; set; }

    [Column("maxModifiedDate", TypeName = "datetime")]
    public DateTime? MaxModifiedDate { get; set; }

    [Column("minSTA", TypeName = "datetime")]
    public DateTime? MinSta { get; set; }

    [Column("maxSTA", TypeName = "datetime")]
    public DateTime? MaxSta { get; set; }
}
