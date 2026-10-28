using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblOverrideDisplay")]
[Index(nameof(DisplayId), Name = "IX_tblOverrideDisplay_DisplayId")]
[Index(nameof(OverrideId), Name = "IX_tblOverrideDisplay_OverrideId")]
public partial class TblOverrideDisplay
{
    [Key]
    public int OverrideDisplayId { get; set; }

    public int OverrideId { get; set; }

    public int DisplayId { get; set; }

    // Navigation properties
    [ForeignKey("OverrideId")]
    [InverseProperty("OverrideDisplays")]
    public virtual TblOverride Override { get; set; } = null!;

    [ForeignKey("DisplayId")]
    [InverseProperty("OverrideDisplays")]
    public virtual TblDisplay Display { get; set; } = null!;
}
