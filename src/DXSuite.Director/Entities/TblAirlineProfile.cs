using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DXSuite.Director.Entities;

/// <summary>
/// What an airline carries beyond tblAirlineAccount: where it stands on approval, and its CUPPS feature options. An
/// airline with no row here predates approval and counts as approved with the default options.
/// </summary>
[Table("tblAirlineProfile", Schema = "aviation")]
public partial class TblAirlineProfile
{
    public const byte Pending = 0;
    public const byte Approved = 1;
    public const byte Declined = 2;

    [Key]
    public int AirlineId { get; set; }

    /// <summary>0 pending, 1 approved, 2 declined.</summary>
    public byte ApprovalStatus { get; set; } = Approved;

    [StringLength(256)]
    public string? RequestedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;

    [StringLength(256)]
    public string? DecidedBy { get; set; }

    [Column(TypeName = "datetime2")]
    public DateTime? DecidedUtc { get; set; }

    [StringLength(500)]
    public string? DecisionNote { get; set; }

    public bool SunflowerOption { get; set; }

    public bool AslVideos { get; set; } = true;

    public bool DestinationZone { get; set; } = true;
}
