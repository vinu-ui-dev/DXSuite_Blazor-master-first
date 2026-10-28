using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblAppVersion")]
[Index(nameof(PlatformId), Name = "IX_tblAppVersion_PlatformId")]
public partial class TblAppVersion
{
    [Key]
    public int AppVersionId { get; set; }

    public int PlatformId { get; set; }

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string VersionNumber { get; set; } = null!;

    [StringLength(100)]
    [Unicode(false)]
    public string? VersionName { get; set; }

    public string? ReleaseNotes { get; set; }

    [Required]
    [StringLength(255)]
    [Unicode(false)]
    public string ZipFileName { get; set; } = null!;

    [Required]
    [StringLength(500)]
    [Unicode(false)]
    public string ZipFilePath { get; set; } = null!;

    public long? ZipFileSize { get; set; }

    [StringLength(64)]
    [Unicode(false)]
    public string? ZipFileHash { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? MinPreviousVersion { get; set; }

    // The bootloader is a second kind under the one platform row rather than a platform of its
    // own: TblDevice.PlatformId holds a single value, so a device enrolled as EngageBrightSign
    // could never match a separate bootloader platform row in any scope query.
    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string ArtifactKind { get; set; } = ArtifactKinds.App;

    // Lowest bootloader version that can run this bundle, on App rows only. A string and not a
    // foreign key, because the bootloader that satisfies it need not be a row in this tenant's table.
    [StringLength(20)]
    [Unicode(false)]
    public string? MinBootloaderVersion { get; set; }

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

    // Navigation properties
    [ForeignKey("PlatformId")]
    [InverseProperty("AppVersions")]
    public virtual TblAppPlatform Platform { get; set; } = null!;

    [InverseProperty("TargetAppVersion")]
    public virtual ICollection<TblDevice> TargetedDevices { get; set; } = new List<TblDevice>();

    [InverseProperty("TargetBootloaderVersion")]
    public virtual ICollection<TblDevice> TargetedBootloaderDevices { get; set; } = new List<TblDevice>();

    [InverseProperty("AppVersion")]
    public virtual ICollection<TblUpdateDeployment> Deployments { get; set; } = new List<TblUpdateDeployment>();

}
