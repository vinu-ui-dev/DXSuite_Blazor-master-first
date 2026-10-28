using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblUpdateDeploymentDevice")]
[Index(nameof(DeploymentId), nameof(DeviceId), IsUnique = true)]
public partial class TblUpdateDeploymentDevice
{
    [Key]
    public int DeploymentDeviceId { get; set; }

    public int DeploymentId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    // Navigation properties
    [ForeignKey("DeploymentId")]
    [InverseProperty("DeploymentDevices")]
    public virtual TblUpdateDeployment Deployment { get; set; } = null!;

    [ForeignKey("DeviceId")]
    [InverseProperty("UpdateDeploymentDevices")]
    public virtual TblDevice Device { get; set; } = null!;
}
