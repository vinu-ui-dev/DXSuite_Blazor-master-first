using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

/// <summary>
/// One-to-one with TblDesignWorksheet. Created on first access (lazy-created).
/// Stores travel cost inputs; derived totals are computed by WorksheetCalculator.
/// </summary>
[Table("tblDesignTravelConfig")]
public class TblDesignTravelConfig
{
    [Key]
    public int TravelConfigId { get; set; }

    public int WorksheetId { get; set; }

    public int Trips { get; set; }
    public int TechnicianCount { get; set; }

    public decimal AirfareCostPerPerson { get; set; }
    public decimal RentalCarCostPerDay { get; set; }
    public int RentalCarDays { get; set; }
    public decimal HotelCostPerNight { get; set; }
    public int HotelNights { get; set; }
    public decimal PerDiemPerPerson { get; set; }
    public int PerDiemDays { get; set; }

    /// <summary>Flat per-trip charge added regardless of headcount.</summary>
    public decimal FlatTripCharge { get; set; }

    // Navigation
    [ForeignKey(nameof(WorksheetId))]
    public virtual TblDesignWorksheet? Worksheet { get; set; }
}
