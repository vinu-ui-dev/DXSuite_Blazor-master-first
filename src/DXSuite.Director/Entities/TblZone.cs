using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>Zones overlap (terminal, level, security side, direction), so displays join them many-to-many.</summary>
[Table("tblZone")]
[Index(nameof(ZoneName), IsUnique = true, Name = "UX_tblZone_ZoneName")]
public partial class TblZone
{
    [Key]
    public int ZoneId { get; set; }

    [Required]
    [StringLength(100)]
    public string ZoneName { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Set when a Display Zone that Activations have targeted is retired instead of deleted (ZONE-12): it leaves the
    /// pickers and its Message access rules stop working, while past Activations still name it.
    /// </summary>
    public DateTime? RetiredUtc { get; set; }

    [InverseProperty("Zone")]
    public virtual ICollection<TblZoneDisplay> ZoneDisplays { get; set; } = new List<TblZoneDisplay>();
}
