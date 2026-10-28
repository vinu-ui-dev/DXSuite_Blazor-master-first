using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.CoreData.Entities;

namespace DXSuite.Aviation.Entities;

/// <summary>
/// Aviation location (airport/city codes). Separate from Director TblLocation.
/// References TblWeather from CoreData.
/// </summary>
[Table("tblLocation")]
public partial class TblLocation
{
    [Key]
    [Column("IATA_CODE")]
    [StringLength(3)]
    [Unicode(false)]
    public string IataCode { get; set; } = null!;

    [Column("ICAO_CODE")]
    [StringLength(255)]
    [Unicode(false)]
    public string? IcaoCode { get; set; }

    [Column("NAME")]
    [StringLength(255)]
    [Unicode(false)]
    public string? Name { get; set; }

    [Column("COUNTRY")]
    [StringLength(255)]
    [Unicode(false)]
    public string? Country { get; set; }

    [Column("COUNTRY_SYMBOL")]
    [StringLength(255)]
    [Unicode(false)]
    public string? CountrySymbol { get; set; }

    [Column("FLIGHT_SECTOR")]
    [StringLength(255)]
    [Unicode(false)]
    public string? FlightSector { get; set; }

    [Column("ALT_LANGUAGE")]
    public double? AltLanguage { get; set; }

    [Column("LANGUAGE_CODES")]
    [StringLength(50)]
    [Unicode(false)]
    public string LanguageCodes { get; set; } = null!;

    [Column("ELAPSED_TIME_JET")]
    [StringLength(255)]
    [Unicode(false)]
    public string? ElapsedTimeJet { get; set; }

    [Column("ELAPSED_TIME_PROP")]
    [StringLength(255)]
    [Unicode(false)]
    public string? ElapsedTimeProp { get; set; }

    [Column("DESCRIPTION")]
    [StringLength(255)]
    [Unicode(false)]
    public string? Description { get; set; }

    [Column("MAXFLTS")]
    [StringLength(255)]
    [Unicode(false)]
    public string? Maxflts { get; set; }

    [Column("LATITUDE")]
    public double? Latitude { get; set; }

    [Column("LONGITUDE")]
    public double? Longitude { get; set; }

    [Column("WEATHER_UNAVAILABLE")]
    public bool WeatherUnavailable { get; set; }

    [Column("PULL_FORECAST")]
    public bool PullForecast { get; set; }

    [Column("DISPLAY_WITHIN_DSC")]
    public bool DisplayWithinDsc { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [ForeignKey("IataCode")]
    [InverseProperty("IataCodes")]
    public virtual ICollection<TblAirline> AirlineCodes { get; set; } = new List<TblAirline>();
}
