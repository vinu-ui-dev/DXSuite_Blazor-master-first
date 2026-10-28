using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelItsmAdapterConfig")]
public partial class ItsmAdapterConfig
{
    [Key]
    [Column("ConfigID")]
    public int ConfigId { get; set; }

    [Column("AdapterType")]
    [StringLength(50)]
    [Unicode(false)]
    public string AdapterType { get; set; } = null!;

    [Column("PlatformName")]
    [StringLength(100)]
    [Unicode(false)]
    public string PlatformName { get; set; } = null!;

    [Column("ConfigJson")]
    [Unicode(false)]
    public string ConfigJson { get; set; } = null!;

    [Column("IsActive")]
    public bool IsActive { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }
}
