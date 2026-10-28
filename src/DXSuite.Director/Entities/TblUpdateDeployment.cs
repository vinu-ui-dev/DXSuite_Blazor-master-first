using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblUpdateDeployment")]
public partial class TblUpdateDeployment
{
    [Key]
    public int DeploymentId { get; set; }

    public int AppVersionId { get; set; }

    [Required]
    [StringLength(100)]
    [Unicode(false)]
    public string DeploymentName { get; set; } = null!;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string DeploymentScope { get; set; } = null!;  // 'ALL', 'LOCATION', 'DEVICE'

    public int? LocationId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Lets this deployment send a device BACKWARDS to an older version than it is running.
    ///
    /// Off by default, and the default is the point. Resolution asks only what is targeted, so
    /// before this existed a deployment naming an older build quietly rolled the fleet back to it.
    /// That is not theoretical: on the bench it downgraded two players onto a bundle that could not
    /// authenticate, which no further deployment could then reach, and recovering them needed a
    /// visit to each. Across sites that is the one mistake that cannot be undone from a desk.
    ///
    /// It stays available because going back is sometimes exactly right: a bad release that boots
    /// and heartbeats will not trigger the bootloader's rollback, which only counts unhealthy boots,
    /// so deliberately deploying the previous version is the way out.
    /// </summary>
    public bool AllowDowngrade { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ScheduledDate { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreatedDate { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? ModifiedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ModifiedDate { get; set; }

    // Batch deployment properties
    public bool EnableBatching { get; set; } = false;

    public int BatchSize { get; set; } = 10;

    [StringLength(20)]
    [Unicode(false)]
    public string? BatchStatus { get; set; }  // PENDING, IN_PROGRESS, PAUSED, COMPLETED, FAILED

    public int CurrentBatchNumber { get; set; } = 0;

    public int TotalBatches { get; set; } = 0;

    [Column(TypeName = "datetime")]
    public DateTime? BatchStartedAt { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastBatchCompletedAt { get; set; }

    // Navigation properties
    [ForeignKey("AppVersionId")]
    [InverseProperty("Deployments")]
    public virtual TblAppVersion AppVersion { get; set; } = null!;

    [ForeignKey("LocationId")]
    [InverseProperty("UpdateDeployments")]
    public virtual TblLocation? Location { get; set; }

    [InverseProperty("Deployment")]
    public virtual ICollection<TblUpdateDeploymentDevice> DeploymentDevices { get; set; } = new List<TblUpdateDeploymentDevice>();

    [InverseProperty("Deployment")]
    public virtual ICollection<TblUpdateDeploymentProgress> DeploymentProgress { get; set; } = new List<TblUpdateDeploymentProgress>();
}

public static class DeploymentScope
{
    public const string All = "ALL";
    public const string Location = "LOCATION";
    public const string Device = "DEVICE";
}

public static class BatchDeploymentStatus
{
    public const string Pending = "PENDING";
    public const string InProgress = "IN_PROGRESS";
    public const string Paused = "PAUSED";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
}
