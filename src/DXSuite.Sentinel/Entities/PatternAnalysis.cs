using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelPatternAnalysis")]
public partial class PatternAnalysis
{
    [Key]
    [Column("AnalysisID")]
    public long AnalysisId { get; set; }

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("PatternType")]
    [StringLength(50)]
    [Unicode(false)]
    public string PatternType { get; set; } = null!;

    [Column("Description")]
    [StringLength(500)]
    [Unicode(false)]
    public string Description { get; set; } = null!;

    [Column("DeviceInstanceID")]
    public int? DeviceInstanceId { get; set; }

    [Column("AnalysisJson")]
    [Unicode(false)]
    public string AnalysisJson { get; set; } = null!;

    [Column("Severity")]
    [StringLength(20)]
    [Unicode(false)]
    public string Severity { get; set; } = null!;

    [Column("AcknowledgedAt", TypeName = "datetime")]
    public DateTime? AcknowledgedAt { get; set; }

    [Column("AcknowledgedBy")]
    [StringLength(50)]
    [Unicode(false)]
    public string? AcknowledgedBy { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }
}
