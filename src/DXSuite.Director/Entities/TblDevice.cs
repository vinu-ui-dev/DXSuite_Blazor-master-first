using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

[Table("tblDevice")]
public partial class TblDevice
{
    [Key]
    [Column("DeviceID")]
    [StringLength(50)]
    [Unicode(false)]
    public string DeviceId { get; set; } = null!;

    [Column("Description")]
    [StringLength(255)]
    [Unicode(false)]
    public string? Description { get; set; }

    [Column("CustomerID")]
    [StringLength(3)]
    [Unicode(false)]
    public string CustomerId { get; set; } = null!;

    [Column("LocationID")]
    public int? LocationId { get; set; }

    [Column("DisplayID")]
    public int? DisplayId { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Manufacturer { get; set; }

    [StringLength(255)]
    [Unicode(false)]
    public string? Model { get; set; }


    [StringLength(50)]
    [Unicode(false)]
    public string SerialNumber { get; set; } = null!;

    [Column("IPAddress")]
    [StringLength(50)]
    [Unicode(false)]
    public string Ipaddress { get; set; } = null!;

    [Column("MACAddress")]
    [StringLength(50)]
    [Unicode(false)]
    public string Macaddress { get; set; } = null!;

    [Column("OSVersion")]
    [StringLength(50)]
    [Unicode(false)]
    public string Osversion { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime Heartbeat { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? StorageType { get; set; }

    public long? StorageSize { get; set; }

    public int? StorageHealth { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    // Auto-update related columns
    [StringLength(20)]
    [Unicode(false)]
    public string? ClientAppVersion { get; set; }

    public int? TargetAppVersionId { get; set; }

    // The bootloader version is a column rather than telemetry in ExtendedData because three
    // consumers query it: the conversion check, the MinBootloaderVersion gate, and the Player
    // Updates screen's two version lines.
    [StringLength(20)]
    [Unicode(false)]
    public string? ClientBootloaderVersion { get; set; }

    public int? TargetBootloaderVersionId { get; set; }

    // Director's device row is the authoritative store for the player's DWS credential, protected
    // at rest. Sentinel's driver resolves it from here rather than from its own CredentialsJson,
    // which has no data protector and no write path anywhere in the repo.
    public string? DwsPasswordProtected { get; set; }

    // Null means enrolled but still on the package's shared bootstrap password, which is a visible
    // device state rather than a silent one. A config re-apply clears this, because re-provisioning
    // writes the bootstrap password back and the app then rotates and re-reports.
    [Column(TypeName = "datetime")]
    public DateTime? DwsPasswordRotatedAt { get; set; }

    // Which provisioning package this player came off, so a leaked card can be answered with the
    // list of players made from it.
    [StringLength(64)]
    [Unicode(false)]
    public string? ProvisioningFingerprint { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastUpdateAttempt { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastUpdateStatus { get; set; }

    public int? PlatformId { get; set; }

    // Auto Heal (LG displays only - TCP power cycling)
    public bool AutoHealEnabled { get; set; }

    // Device Type (Player, Display, BrightSign, etc.)
    public int? DeviceTypeId { get; set; }

    // Flexible JSON storage for device-specific data (ContentHealth, ConnectedDisplays, InputStatus, etc.)
    public string? ExtendedData { get; set; }

    // User-facing friendly name; defaults to DeviceId when not set.
    // Shown in Device Management and as the channel name in Content Channels.
    [StringLength(100)]
    public string? Name { get; set; }

    /// <summary>
    /// IANA identifier such as America/Chicago. Never null: every row-creation path seeds it, so no
    /// schedule evaluator carries an unset branch. A value that does not resolve fails toward the
    /// serving path's own default rather than toward another clock.
    /// </summary>
    [StringLength(50)]
    [Unicode(false)]
    public string TimeZone { get; set; } = null!;

    /// <summary>
    /// Seeded or Operator, with Discovered reserved for a device-reported zone. It governs exactly
    /// one thing, whether the zone is confirmed enough to send out to the device. No evaluator reads
    /// it: windows resolve against TimeZone whatever its provenance.
    /// </summary>
    [StringLength(20)]
    [Unicode(false)]
    public string TimeZoneSource { get; set; } = null!;

    /// <summary>One of the DeviceOrientation names. Only Engage for BrightSign players apply it.</summary>
    [StringLength(20)]
    [Unicode(false)]
    public string Orientation { get; set; } = Services.DeviceOrientation.Landscape;

    /// <summary>One of the DeviceVideoPath names. Only Engage for BrightSign players apply it.</summary>
    [StringLength(10)]
    [Unicode(false)]
    public string VideoPath { get; set; } = Services.DeviceVideoPath.Hardware;

    public int? SiteId { get; set; }


    [ForeignKey("DeviceTypeId")]
    [InverseProperty("TblDevices")]
    public virtual TblDeviceType? DeviceType { get; set; }

    [ForeignKey("SiteId")]
    [InverseProperty("Devices")]
    public virtual TblSite? Site { get; set; }

    [ForeignKey("DisplayId")]
    [InverseProperty("TblDevices")]
    public virtual TblDisplay? Display { get; set; }

    [ForeignKey("LocationId")]
    [InverseProperty("TblDevices")]
    public virtual TblLocation? Location { get; set; }

    [InverseProperty("Device")]
    public virtual ICollection<TblOverrideDevice> OverrideDevices { get; set; } = new List<TblOverrideDevice>();

    [InverseProperty("Device")]
    public virtual ICollection<TblOverrideContent> OverrideContents { get; set; } = new List<TblOverrideContent>();

    // Auto-update navigation properties
    [ForeignKey("TargetAppVersionId")]
    [InverseProperty("TargetedDevices")]
    public virtual TblAppVersion? TargetAppVersion { get; set; }

    [ForeignKey("TargetBootloaderVersionId")]
    [InverseProperty("TargetedBootloaderDevices")]
    public virtual TblAppVersion? TargetBootloaderVersion { get; set; }

    [ForeignKey("PlatformId")]
    [InverseProperty("Devices")]
    public virtual TblAppPlatform? Platform { get; set; }

    [InverseProperty("Device")]
    public virtual ICollection<TblUpdateDeploymentDevice> UpdateDeploymentDevices { get; set; } = new List<TblUpdateDeploymentDevice>();

    [InverseProperty("Device")]
    public virtual ICollection<TblUpdateDeploymentProgress> UpdateDeploymentProgress { get; set; } = new List<TblUpdateDeploymentProgress>();

    [InverseProperty("Device")]
    public virtual ICollection<TblEventLog> TblEventLogs { get; set; } = new List<TblEventLog>();

    [InverseProperty("Device")]
    public virtual ICollection<TblDeviceMetadata> DeviceMetadata { get; set; } = new List<TblDeviceMetadata>();
}
