using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Design.Entities;

[Table("tblDesignWorksheet")]
public class TblDesignWorksheet
{
    [Key]
    public int WorksheetId { get; set; }

    public int DesignId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Set after a successful Push to NetSuite. Null until first push.</summary>
    [MaxLength(100)]
    public string? NetsuiteEstimateId { get; set; }

    /// <summary>Human-readable estimate number (e.g. "QT-00123"). Fetched from NS after push.</summary>
    [MaxLength(50)]
    public string? NetsuiteEstimateTranId { get; set; }

    /// <summary>Default NS location applied to all push lines that don't have a per-row override.</summary>
    [MaxLength(100)]
    public string? DefaultLocationId { get; set; }

    [MaxLength(300)]
    public string? DefaultLocationName { get; set; }

    [MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreateDate { get; set; } = DateTime.UtcNow;
    public DateTime LastEditDate { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(DesignId))]
    public virtual TblDesign? Design { get; set; }

    public virtual ICollection<TblDesignWorksheetRoom> Rooms { get; set; } = [];
    public virtual ICollection<TblDesignWorksheetLineItem> LineItems { get; set; } = [];
    public virtual ICollection<TblDesignWorksheetLaborRow> LaborRows { get; set; } = [];
    public virtual ICollection<TblDesignWorksheetNonInventoryRow> NonInventoryRows { get; set; } = [];
    public virtual TblDesignTravelConfig? TravelConfig { get; set; }
}
