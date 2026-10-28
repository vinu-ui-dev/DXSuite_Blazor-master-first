using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Sentinel.Entities;

[Table("SentinelAutomationDefinition")]
public partial class AutomationDefinition
{
    [Key]
    [Column("AutomationID")]
    public int AutomationId { get; set; }

    [Column("Name")]
    [StringLength(100)]
    [Unicode(false)]
    public string Name { get; set; } = null!;

    [Column("Description")]
    [StringLength(500)]
    [Unicode(false)]
    public string? Description { get; set; }

    [Column("AuthorType")]
    [StringLength(20)]
    [Unicode(false)]
    public string AuthorType { get; set; } = null!;

    [Column("DeviceDescriptorID")]
    public int? DeviceDescriptorId { get; set; }

    [Column("Category")]
    [StringLength(50)]
    [Unicode(false)]
    public string? Category { get; set; }

    [Column("TriggerJson")]
    [Unicode(false)]
    public string TriggerJson { get; set; } = null!;

    [Column("CurrentVersion")]
    [StringLength(20)]
    [Unicode(false)]
    public string CurrentVersion { get; set; } = null!;

    [Column("Status")]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column("Priority")]
    public int Priority { get; set; }

    [Column("CreatedBy")]
    [StringLength(50)]
    [Unicode(false)]
    public string CreatedBy { get; set; } = null!;

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [ForeignKey("DeviceDescriptorId")]
    [InverseProperty("AutomationDefinitions")]
    public virtual DeviceDescriptor? DeviceDescriptor { get; set; }

    [InverseProperty("AutomationDefinition")]
    public virtual ICollection<AutomationVersion> AutomationVersions { get; set; } = new List<AutomationVersion>();

    [InverseProperty("AutomationDefinition")]
    public virtual ICollection<AutomationExecutionLog> AutomationExecutionLogs { get; set; } = new List<AutomationExecutionLog>();

    [InverseProperty("AutomationDefinition")]
    public virtual ICollection<RemediationLog> RemediationLogs { get; set; } = new List<RemediationLog>();
}
