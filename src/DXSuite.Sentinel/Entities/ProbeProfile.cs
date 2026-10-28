using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelProbeProfile")]
public class ProbeProfile
{
    [Key]
    [Column("ProfileID")]
    public int ProfileId { get; set; }

    [Column("ProfileName")]
    [StringLength(100)]
    [Unicode(false)]
    public string ProfileName { get; set; } = null!;

    [Column("Description")]
    [StringLength(500)]
    [Unicode(false)]
    public string? Description { get; set; }

    /// <summary>
    /// "environment" | "make-model" | "device"
    /// </summary>
    [Column("Scope")]
    [StringLength(20)]
    [Unicode(false)]
    public string Scope { get; set; } = null!;

    [Column("MatchManufacturer")]
    [StringLength(100)]
    [Unicode(false)]
    public string? MatchManufacturer { get; set; }

    [Column("MatchModel")]
    [StringLength(100)]
    [Unicode(false)]
    public string? MatchModel { get; set; }

    [Column("DeviceInstanceID")]
    public int? DeviceInstanceId { get; set; }

    /// <summary>
    /// JSON-serialized ProbeStrategyConfig with ordered probe steps
    /// </summary>
    [Column("StrategyJson")]
    [Unicode(false)]
    public string StrategyJson { get; set; } = null!;

    [Column("IsActive")]
    public bool IsActive { get; set; }

    /// <summary>
    /// Lower = higher priority within same scope tier
    /// </summary>
    [Column("Priority")]
    public int Priority { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }
}
