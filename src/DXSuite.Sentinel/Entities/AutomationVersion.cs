using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelAutomationVersion")]
public partial class AutomationVersion
{
    [Key]
    [Column("VersionID")]
    public int VersionId { get; set; }

    [Column("AutomationID")]
    public int AutomationId { get; set; }

    [Column("Version")]
    [StringLength(20)]
    [Unicode(false)]
    public string Version { get; set; } = null!;

    [Column("ContentJson")]
    [Unicode(false)]
    public string ContentJson { get; set; } = null!;

    [Column("Status")]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column("IsActive")]
    public bool IsActive { get; set; }

    [Column("PublishedAt", TypeName = "datetime")]
    public DateTime? PublishedAt { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("CreatedBy")]
    [StringLength(50)]
    [Unicode(false)]
    public string CreatedBy { get; set; } = null!;

    [ForeignKey("AutomationId")]
    [InverseProperty("AutomationVersions")]
    public virtual AutomationDefinition AutomationDefinition { get; set; } = null!;

    [InverseProperty("AutomationVersion")]
    public virtual ICollection<AutomationExecutionLog> AutomationExecutionLogs { get; set; } = new List<AutomationExecutionLog>();
}
