using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Director.Entities;

[Table("tblControlClient")]
public class TblControlClient
{
    [Key]
    [Column("ControlClientId")]
    public int ControlClientId { get; set; }

    [Required]
    [StringLength(100)]
    public string ClientName { get; set; } = null!;

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string AuthMode { get; set; } = "accesscode";

    [StringLength(128)]
    [Unicode(false)]
    public string? AccessCodeHash { get; set; }

    [StringLength(16)]
    [Unicode(false)]
    public string? AccessCodePrefix { get; set; }

    [Column("LocationId")]
    public int LocationId { get; set; }

    public bool IsActive { get; set; } = true;

    [Column(TypeName = "datetime")]
    public DateTime? LastAuthAt { get; set; }

    [StringLength(45)]
    [Unicode(false)]
    public string? LastAuthIp { get; set; }

    [Column("CreatedBy")]
    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("ControlClients")]
    public virtual TblLocation Location { get; set; } = null!;

    [ForeignKey("CreatedBy")]
    public virtual TblUser? CreatedByUser { get; set; }

    [InverseProperty("ControlClient")]
    public virtual ICollection<TblControlClientDisplay> ControlClientDisplays { get; set; } = new List<TblControlClientDisplay>();

    [InverseProperty("ControlClient")]
    public virtual ICollection<TblControlClientToken> ControlClientTokens { get; set; } = new List<TblControlClientToken>();
}
