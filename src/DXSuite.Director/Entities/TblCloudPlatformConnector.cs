using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblCloudPlatformConnector")]
public partial class TblCloudPlatformConnector
{
    [Key]
    [Column("ConnectorID")]
    public int ConnectorId { get; set; }

    [Column("PlatformID")]
    [StringLength(50)]
    [Unicode(false)]
    public string PlatformId { get; set; } = null!;

    [Column("DisplayName")]
    [StringLength(100)]
    [Unicode(false)]
    public string DisplayName { get; set; } = null!;

    [Column("CustomerID")]
    [StringLength(20)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("ConfigJson")]
    [Unicode(false)]
    public string ConfigJson { get; set; } = null!;

    [Column("IsEnabled")]
    public bool IsEnabled { get; set; }

    [Column("LastSyncAt", TypeName = "datetime")]
    public DateTime? LastSyncAt { get; set; }

    [Column("LastSyncStatus")]
    [StringLength(50)]
    [Unicode(false)]
    public string? LastSyncStatus { get; set; }

    [Column("LastSyncMessage")]
    [StringLength(500)]
    [Unicode(false)]
    public string? LastSyncMessage { get; set; }

    [Column("CreateDate", TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column("LastEditDate", TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [Column("GatewayMode")]
    [StringLength(20)]
    [Unicode(false)]
    public string GatewayMode { get; set; } = "none";

    [Column("ContentMode")]
    [StringLength(30)]
    [Unicode(false)]
    public string ContentMode { get; set; } = "monitoring-only";

    [Column("EnabledImports")]
    [StringLength(500)]
    [Unicode(false)]
    public string? EnabledImports { get; set; }

    [Column("CredentialExpiresAt", TypeName = "datetime")]
    public DateTime? CredentialExpiresAt { get; set; }

    [NotMapped]
    public bool HasContentSync => ContentMode == "content-only" || ContentMode == "content-and-monitoring";

    [NotMapped]
    public bool HasMonitoringSync => ContentMode == "monitoring-only" || ContentMode == "content-and-monitoring";
}
