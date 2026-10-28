using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

[Table("tblUserLocation")]
public partial class TblUserLocation
{
    [Key]
    public int UserLocationId { get; set; }

    public int LocationId { get; set; }

    public int UserId { get; set; }

    [ForeignKey("UserId")]
    public virtual TblUser User { get; set; } = null!;

    [ForeignKey("LocationId")]
    [InverseProperty("TblUserLocations")]
    public virtual TblLocation Location { get; set; } = null!;
}
