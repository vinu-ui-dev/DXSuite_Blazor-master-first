using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblUpdateLog")]
[Index(nameof(DeviceId), Name = "IX_tblUpdateLog_DeviceId")]
[Index(nameof(Timestamp), Name = "IX_tblUpdateLog_Timestamp")]
[Index(nameof(AppVersionId), Name = "IX_tblUpdateLog_AppVersionId")]
public partial class TblUpdateLog
{
    [Key]
    public int LogId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    public int? AppVersionId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string EventType { get; set; } = null!;

    [StringLength(1000)]
    public string? EventMessage { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? PreviousVersion { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? NewVersion { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Timestamp { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? IpAddress { get; set; }

    // Navigation properties
    [ForeignKey("DeviceId")]
    [InverseProperty("UpdateLogs")]
    public virtual TblDevice Device { get; set; } = null!;

    [ForeignKey("AppVersionId")]
    [InverseProperty("UpdateLogs")]
    public virtual TblAppVersion? AppVersion { get; set; }
}

public static class UpdateEventType
{
    public const string UpdateStarted = "UPDATE_STARTED";
    public const string DownloadComplete = "DOWNLOAD_COMPLETE";
    public const string UpdateComplete = "UPDATE_COMPLETE";
    public const string UpdateFailed = "UPDATE_FAILED";
    public const string Rollback = "ROLLBACK";
    public const string VersionReported = "VERSION_REPORTED";
}
