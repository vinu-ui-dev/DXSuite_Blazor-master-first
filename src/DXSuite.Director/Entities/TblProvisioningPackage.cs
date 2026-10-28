using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// One generated provisioning card image: a single autorun.zip plus a family firmware file.
///
/// A package binds to a tenant and a hardware family, never to a location or a device. It carries
/// no time zone deliberately, because the card has the wrong granularity for one; a device's zone
/// reaches it per device on the player content response.
///
/// Network settings live inline rather than as a shared "network profile" entity, with
/// clone-from-existing as the reuse mechanism. The trigger to promote them is the first customer
/// whose sites need genuinely different profiles edited centrally and re-applied.
/// </summary>
[Table("tblProvisioningPackage")]
[Index(nameof(ConfigFingerprint), Name = "IX_tblProvisioningPackage_Fingerprint")]
public class TblProvisioningPackage
{
    [Key]
    public int ProvisioningPackageId { get; set; }

    [Required]
    [StringLength(100)]
    [Unicode(false)]
    public string PackageName { get; set; } = null!;

    [Required]
    [StringLength(10)]
    [Unicode(false)]
    public string HardwareFamily { get; set; } = null!;

    public int FirmwareImageId { get; set; }

    // True when the operator overrode the family default, which the record has to carry because
    // the default moves and a card in the field does not.
    public bool FirmwarePinOverridden { get; set; }

    // Resolved from TblTenant.SiteUrl at generation. Recorded rather than resolved on read:
    // package generation has no HTTP request to lean on, and a card in the field cannot follow a
    // later change to the tenant's site URL.
    [Required]
    [StringLength(500)]
    [Unicode(false)]
    public string EndpointUrl { get; set; } = null!;

    // A site NTP override. Null means the player uses the DXSuite time route at the player-API origin.
    [StringLength(500)]
    [Unicode(false)]
    public string? TimeSourceUrl { get; set; }

    public string? CustomRootCaPem { get; set; }

    // A literal host:port. PAC files are refused.
    [StringLength(100)]
    [Unicode(false)]
    public string? ProxyHostPort { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? WifiSsid { get; set; }

    // Protected at rest. The package's genuinely sensitive content: a stolen card carrying one is a
    // customer network-credential incident, and a lost card requires PSK rotation.
    public string? WifiPskProtected { get; set; }

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string DwsPosition { get; set; } = DwsPositions.OffEntirely;

    // Protected at rest, shown to the operator once. Shared by every player made from this package,
    // which is why enrollment rotates to a per-device secret and why reissuing mints a new one:
    // a leak is bounded to a package rather than a tenant.
    [Required]
    public string BootstrapDwsPasswordProtected { get; set; } = null!;

    // Off in every shipped package. The Chromium inspector needs the html/enable_web_inspector
    // registry key in addition to the widget's inspector_server port, so this is set deliberately
    // either way rather than left to a default.
    public bool WebInspectorEnabled { get; set; }

    // Hash of the config payload only, never the whole zip. DXSuite computes it and writes it into
    // config.json as a literal string; the player only string-compares.
    [Required]
    [StringLength(64)]
    [Unicode(false)]
    public string ConfigFingerprint { get; set; } = null!;

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
    public string? ZipSha256 { get; set; }

    // A superseded package stays downloadable because cards in the field reference it. Revocation
    // is the escape hatch when one leaks, and the fingerprint on each device row answers which
    // players came off it.
    public bool IsRevoked { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? RevokedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? RevokedDate { get; set; }

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

    [ForeignKey("FirmwareImageId")]
    [InverseProperty("Packages")]
    public virtual TblFirmwareImage FirmwareImage { get; set; } = null!;
}

/// <summary>
/// Where a package leaves the player's local web server. The DWS is no longer assumed present, so
/// what Sentinel can do locally is a per-device fact rather than a flavour-wide one.
/// </summary>
public static class DwsPositions
{
    public const string OnAlways = "OnAlways";
    public const string OnAtPark = "OnAtPark";
    public const string OffEntirely = "OffEntirely";

    public static bool IsValid(string? value)
        => value is OnAlways or OnAtPark or OffEntirely;
}
