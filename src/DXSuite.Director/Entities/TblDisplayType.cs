using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblDisplayType")]
public partial class TblDisplayType
{
    [Key]
    public int DisplayTypeId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? DisplayTypeName { get; set; }

    [InverseProperty("DisplayType")]
    public virtual ICollection<TblDisplay> TblDisplays { get; set; } = new List<TblDisplay>();
}
