using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblLocationType")]
public partial class TblLocationType
{
    [Key]
    public int LocationTypeId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LocationTypeName { get; set; }

    public bool LinkedToDisplay { get; set; }

    public bool IsCustom { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? ComponentTypeName { get; set; }

    public bool ShowOverrideSection { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [InverseProperty("LocationType")]
    public virtual ICollection<TblLocation> TblLocations { get; set; } = new List<TblLocation>();
}
