using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

/// <summary>
/// The CUPPS feature options of an airline, beside tblAirlineAccount. An airline with no row here has the default
/// options (Sunflower off, ASL videos on, Destination Zone on).
/// </summary>
[Table("tblAirlineProfile", Schema = "aviation")]
public partial class TblAirlineProfile
{
    [Key]
    public int AirlineId { get; set; }

    public bool SunflowerOption { get; set; }

    public bool AslVideos { get; set; } = true;

    public bool DestinationZone { get; set; } = true;
}
