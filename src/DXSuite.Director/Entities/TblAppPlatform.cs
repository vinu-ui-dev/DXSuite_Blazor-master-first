using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblAppPlatform")]
public partial class TblAppPlatform
{
    [Key]
    public int PlatformId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string PlatformName { get; set; } = null!;

    [StringLength(255)]
    [Unicode(false)]
    public string? PlatformDescription { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string FolderName { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    // Navigation properties
    [InverseProperty("Platform")]
    public virtual ICollection<TblAppVersion> AppVersions { get; set; } = new List<TblAppVersion>();

    [InverseProperty("Platform")]
    public virtual ICollection<TblDevice> Devices { get; set; } = new List<TblDevice>();
}
