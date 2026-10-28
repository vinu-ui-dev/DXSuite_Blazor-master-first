using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblUpdateDeploymentProgress")]
[Index(nameof(DeploymentId), nameof(DeviceId), IsUnique = true)]
[Index(nameof(DeploymentId), nameof(BatchNumber))]
public partial class TblUpdateDeploymentProgress
{
    [Key]
    public int ProgressId { get; set; }

    public int DeploymentId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    public int BatchNumber { get; set; }

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string Status { get; set; } = null!;  // PENDING, TRIGGERED, UPDATING, COMPLETED, FAILED

    [Column(TypeName = "datetime")]
    public DateTime? TriggeredAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? CompletedAt { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? PreviousVersion { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? NewVersion { get; set; }

    [StringLength(500)]
    public string? ErrorMessage { get; set; }

    // Navigation properties
    [ForeignKey("DeploymentId")]
    [InverseProperty("DeploymentProgress")]
    public virtual TblUpdateDeployment Deployment { get; set; } = null!;

    [ForeignKey("DeviceId")]
    [InverseProperty("UpdateDeploymentProgress")]
    public virtual TblDevice Device { get; set; } = null!;
}

public static class DeploymentProgressStatus
{
    public const string Pending = "PENDING";
    public const string Triggered = "TRIGGERED";
    public const string Updating = "UPDATING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}
