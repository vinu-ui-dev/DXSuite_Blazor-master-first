using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblEndpoint")]
public partial class TblEndpoint
{
    [Key]
    public int EndpointId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string Type { get; set; } = null!;

    [Column("IPAddress")]
    [StringLength(15)]
    [Unicode(false)]
    public string Ipaddress { get; set; } = null!;

    [StringLength(50)]
    [Unicode(false)]
    public string? SerialNo { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    [InverseProperty("Endpoint")]
    public virtual ICollection<TblDisplay> TblDisplays { get; set; } = new List<TblDisplay>();
}
