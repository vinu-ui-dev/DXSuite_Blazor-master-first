using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Keyless]
public partial class ViewGetWeatherDestination
{
    [Column("DES")]
    [StringLength(3)]
    [Unicode(false)]
    public string? Des { get; set; }

    [Column("LATITUDE")]
    public double? Latitude { get; set; }

    [Column("LONGITUDE")]
    public double? Longitude { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? SyncTime { get; set; }

    [Column("FLIGHT_SECTOR")]
    [StringLength(255)]
    [Unicode(false)]
    public string? FlightSector { get; set; }

    [Column("ForecastURI")]
    [StringLength(500)]
    [Unicode(false)]
    public string? ForecastUri { get; set; }

    [Column("PULL_FORECAST")]
    public bool? PullForecast { get; set; }

    [StringLength(25)]
    [Unicode(false)]
    public string? RadarStation { get; set; }
}
