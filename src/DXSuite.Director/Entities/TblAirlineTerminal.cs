using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblAirlineTerminal", Schema = "aviation")]
[PrimaryKey(nameof(AirlineId), nameof(ZoneId))]
public partial class TblAirlineTerminal
{
    public int AirlineId { get; set; }

    public int ZoneId { get; set; }
}
