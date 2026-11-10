using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

/// <summary>
/// The logo an airline uses for one role (wordmark, square, reversed), linked to an asset in the Director
/// airline logo library. The file itself stays in the library; this row only points at it.
/// </summary>
[Table("tblAirlineLogo")]
[Index(nameof(AirlineCode), nameof(Role), Name = "IX_tblAirlineLogo_Airline_Role", IsUnique = true)]
public partial class TblAirlineLogo
{
    [Key]
    public int AirlineLogoId { get; set; }

    [Required]
    [StringLength(3)]
    [Unicode(false)]
    public string AirlineCode { get; set; } = null!;

    /// <summary>wordmark, square or reversed.</summary>
    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string Role { get; set; } = null!;

    public int AssetId { get; set; }

    [StringLength(255)]
    public string? AssetName { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string AssetPath { get; set; } = null!;

    public DateTime LinkedUtc { get; set; }
}
