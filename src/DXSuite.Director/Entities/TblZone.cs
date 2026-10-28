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

    [InverseProperty("Zone")]
    public virtual ICollection<TblZoneDisplay> ZoneDisplays { get; set; } = new List<TblZoneDisplay>();
}
