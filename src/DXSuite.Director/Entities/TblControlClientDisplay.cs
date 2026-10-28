using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblControlClientDisplay")]
[Index(nameof(ControlClientId), nameof(DisplayId), IsUnique = true, Name = "UQ_ControlClientDisplay")]
public class TblControlClientDisplay
{
    [Key]
    [Column("ControlClientDisplayId")]
    public int ControlClientDisplayId { get; set; }

    [Column("ControlClientId")]
    public int ControlClientId { get; set; }

    [Column("DisplayId")]
    public int DisplayId { get; set; }

    [ForeignKey("ControlClientId")]
    [InverseProperty("ControlClientDisplays")]
    public virtual TblControlClient ControlClient { get; set; } = null!;

    [ForeignKey("DisplayId")]
    public virtual TblDisplay Display { get; set; } = null!;
}
