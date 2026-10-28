using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// An operator's standing permission for one serial to convert itself into this tenant, armed
/// before the player asks and living until it converts or is disarmed.
///
/// Its own table rather than a reuse of TblDevicePairingRequest, which is the inversion of this:
/// a pairing request is player-initiated, keyed on a six-digit code, and expires in ten minutes.
/// It is also the only place that can answer "how many armed serials have converted", since the
/// players being taken over are not yet rows in tblDevice.
/// </summary>
[Table("tblProvisioningAuthorization")]
[Index(nameof(SerialNumber), Name = "IX_tblProvisioningAuthorization_Serial")]
[Index(nameof(State), Name = "IX_tblProvisioningAuthorization_State")]
public class TblProvisioningAuthorization
{
    [Key]
    public int ProvisioningAuthorizationId { get; set; }

    [Required]
    [StringLength(50)]
    [Unicode(false)]
    public string SerialNumber { get; set; } = null!;

    [Required]
    [StringLength(20)]
    [Unicode(false)]
    public string State { get; set; } = ProvisioningAuthorizationStates.Armed;

    [StringLength(100)]
    [Unicode(false)]
    public string? ArmedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ArmedDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime ExpiresDate { get; set; }

    // Arming pins what this conversion installs, so a takeover straddling a release does not put
    // Monday's and Friday's cohorts on different bundles. Re-arming picks up current. These are a
    // separate pin from the known-good bootloader, which answers what rescues a broken player:
    // conflating them would let a rollout candidate become the rescue image.
    public int? PinnedAppVersionId { get; set; }

    public int? PinnedBootloaderVersionId { get; set; }

    public int? ProvisioningPackageId { get; set; }

    /// <summary>
    /// When an install bootstrap was last served for this serial, which suppresses serving another
    /// for a short grace period.
    ///
    /// The OS checks the network recovery override BEFORE it runs the card's autorun, so a serial
    /// that is armed and not yet converted would be handed a fresh bootstrap on every boot: the
    /// courier rewrites the card and reboots, and the autorun it just installed never gets to run,
    /// which is the only thing that could convert it. Cleared by construction on re-arm, because
    /// re-arming inserts a new row.
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime? BootstrapServedDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ConvertedDate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? ConvertedDeviceId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string? DisarmedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DisarmedDate { get; set; }

    [ForeignKey("PinnedAppVersionId")]
    public virtual TblAppVersion? PinnedAppVersion { get; set; }

    [ForeignKey("PinnedBootloaderVersionId")]
    public virtual TblAppVersion? PinnedBootloaderVersion { get; set; }

    [ForeignKey("ProvisioningPackageId")]
    public virtual TblProvisioningPackage? ProvisioningPackage { get; set; }
}

/// <summary>
/// The life of an authorisation. Disarming is the kill switch for a takeover in flight.
/// </summary>
public static class ProvisioningAuthorizationStates
{
    public const string Armed = "Armed";
    public const string Converted = "Converted";
    public const string Disarmed = "Disarmed";
    public const string Expired = "Expired";

    public static bool IsValid(string? value)
        => value is Armed or Converted or Disarmed or Expired;
}
