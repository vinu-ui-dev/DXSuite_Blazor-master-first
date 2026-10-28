using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.CoreData.Entities;

[Table("tblWeather")]
public partial class TblWeather
{
    [Key]
    [Column("IATA_CODE")]
    [StringLength(3)]
    [Unicode(false)]
    public string IataCode { get; set; } = null!;

    [StringLength(25)]
    [Unicode(false)]
    public string? RadarStation { get; set; }

    [Column("Temperature_F")]
    public double? TemperatureF { get; set; }

    [Column("Temperature_C")]
    public double? TemperatureC { get; set; }

    public double? Humidity { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Description { get; set; }

    [StringLength(25)]
    [Unicode(false)]
    public string? WindDirection { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? WindSpeed { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Icon { get; set; }

    public int CallCount { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? StationUpdateTime { get; set; }

    [Column("ForecastURI")]
    [StringLength(500)]
    [Unicode(false)]
    public string? ForecastUri { get; set; }

    [Column("ForecastTemperature_F")]
    public double? ForecastTemperatureF { get; set; }

    [Column("ForecastTemperature_C")]
    public double? ForecastTemperatureC { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? ForecastDescription { get; set; }

    [Unicode(false)]
    public string? ForecastDescriptionDetailed { get; set; }

    [StringLength(25)]
    [Unicode(false)]
    public string? ForecastWindDirection { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? ForecastWindSpeed { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? ForecastPeriodName { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? ForecastTemperatureTrend { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? ForecastIcon { get; set; }

    public int ForecastCallCount { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ForecastStationUpdateTime { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? SyncTime { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }
}
