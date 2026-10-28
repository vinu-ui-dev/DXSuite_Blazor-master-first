using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

[Table("tblAirline")]
[Index("AirlineCode", Name = "IX_tblAirline", IsUnique = true)]
public partial class TblAirline
{
    [Key]
    [Column("RecordID")]
    public int RecordId { get; set; }

    [Column("Airline_Code")]
    [StringLength(3)]
    [Unicode(false)]
    public string AirlineCode { get; set; } = null!;

    [Column("ICAO")]
    [StringLength(255)]
    [Unicode(false)]
    public string? Icao { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? CarrierName { get; set; }

    public bool DisplayWithinSettingsMenu { get; set; }

    public int LastBagExpirationMinutes { get; set; }

    public int LastBagDelaySeconds { get; set; }

    [Column("CBIDIconWidth")]
    public int CbidiconWidth { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? CarrierLogoPath { get; set; }

    public int? CarrierStatus1 { get; set; }

    public int? CarrierStatus2 { get; set; }

    [Column("TPEnabled1")]
    public bool? Tpenabled1 { get; set; }

    [Column("TPEnabled2")]
    public bool? Tpenabled2 { get; set; }

    public int? Pincode { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CreateDate { get; set; }

    [InverseProperty("AirlineCodeNavigation")]
    public virtual ICollection<TblAirlineGate> TblAirlineGates { get; set; } = new List<TblAirlineGate>();

    [InverseProperty("FlcNavigation")]
    public virtual ICollection<TblFlight> TblFlights { get; set; } = new List<TblFlight>();

    [ForeignKey("AirlineCode")]
    [InverseProperty("AirlineCodes")]
    public virtual ICollection<TblLocation> IataCodes { get; set; } = new List<TblLocation>();
}
