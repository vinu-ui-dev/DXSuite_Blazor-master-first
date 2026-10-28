using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblZoneDisplay")]
[PrimaryKey(nameof(ZoneId), nameof(DisplayId))]
[Index(nameof(DisplayId), Name = "IX_tblZoneDisplay_DisplayId")]
public partial class TblZoneDisplay
{
    public int ZoneId { get; set; }

    public int DisplayId { get; set; }

    [ForeignKey("ZoneId")]
    [InverseProperty("ZoneDisplays")]
    public virtual TblZone Zone { get; set; } = null!;

    [ForeignKey("DisplayId")]
    public virtual TblDisplay Display { get; set; } = null!;
}
