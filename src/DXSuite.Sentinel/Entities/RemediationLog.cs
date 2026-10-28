using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelRemediationLog")]
public partial class RemediationLog
{
    [Key]
    [Column("LogID")]
    public long LogId { get; set; }

    [Column("DeviceInstanceID")]
    public int DeviceInstanceId { get; set; }

    [Column("Tier")]
    public int Tier { get; set; }

    [Column("TriggerCondition")]
    [StringLength(200)]
    [Unicode(false)]
    public string TriggerCondition { get; set; } = null!;

    [Column("ActionTaken")]
    [StringLength(200)]
    [Unicode(false)]
    public string ActionTaken { get; set; } = null!;

    [Column("ActionParameters")]
    [Unicode(false)]
    public string? ActionParameters { get; set; }

    [Column("Result")]
    [StringLength(50)]
    [Unicode(false)]
    public string Result { get; set; } = null!;

    [Column("ResultDetails")]
    [Unicode(false)]
    public string? ResultDetails { get; set; }

    [Column("AutomationID")]
    public int? AutomationId { get; set; }

    [Column("LlmInteractionID")]
    public long? LlmInteractionId { get; set; }

    [Column("DurationMs")]
    public long? DurationMs { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [ForeignKey("DeviceInstanceId")]
    [InverseProperty("RemediationLogs")]
    public virtual DeviceInstance DeviceInstance { get; set; } = null!;

    [ForeignKey("AutomationId")]
    [InverseProperty("RemediationLogs")]
    public virtual AutomationDefinition? AutomationDefinition { get; set; }

}
