using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

/// <summary>
/// A manual counter allocation (GTAA FSD 1.2 section 3.4): which airline holds a check-in counter, and for how
/// long. New work, not ported from a reference build — there is no RMS/CoreAPI allocation feed to fall back to
/// yet, so every row here is entered by hand; <see cref="IsManual"/> exists so a future real feed import can
/// tell its own rows apart from ones RMU entered directly. One row per counter per time range; overlapping
/// ranges for the same counter are refused by the service, not by a database constraint, so the refusal can
/// explain which existing row is in the way.
/// </summary>
[Table("tblCounterAllocation")]
[Index(nameof(Counter), nameof(StartUtc), Name = "IX_tblCounterAllocation_Counter_Start")]
public partial class TblCounterAllocation
{
    [Key]
    public int CounterAllocationId { get; set; }

    [Required]
    [StringLength(20)]
    public string Counter { get; set; } = null!;

    [Required]
    [StringLength(10)]
    public string AirlineCode { get; set; } = null!;

    /// <summary>Common, Dedicated or Info (FSD 3.1 CTR-xx).</summary>
    [Required]
    [StringLength(20)]
    public string AllocationType { get; set; } = "Common";

    /// <summary>Set only for a Dedicated allocation.</summary>
    [StringLength(100)]
    public string? Destination { get; set; }

    [StringLength(10)]
    public string? FlightNumber { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    /// <summary>Always true in this build — every row is hand-entered. Kept so a future CoreAPI-fed row is
    /// never silently confused with one RMU typed in during an outage.</summary>
    public bool IsManual { get; set; } = true;

    public DateTime CreatedUtc { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }
}
