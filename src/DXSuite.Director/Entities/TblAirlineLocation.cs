using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblAirlineLocation")]
public partial class TblAirlineLocation
{
    [Key]
    public int AirlineLocationId { get; set; }

    public int LocationId { get; set; }

    [StringLength(3)]
    [Unicode(false)]
    public string? AirlineCode { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("TblAirlineLocations")]
    public virtual TblLocation Location { get; set; } = null!;
}
