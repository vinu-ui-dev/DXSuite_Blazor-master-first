using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Entities;

/// <summary>
/// An anonymous touch control surface for one location. A panel serves a location bundle's
/// files outside the Blazor app (no login, no nav, not a location page) and exposes a narrow
/// API that lets whoever is standing at it assign content to that location's displays.
///
/// Panels are addressed as /panel/{PanelKey}/{token}/ and the token is the only credential.
/// It lives in the URL permanently because the consumer is a player webzone that performs a
/// plain navigation to a URL configured once and cannot be relied on to keep a cookie across
/// restarts. See PanelController for the full reasoning.
/// </summary>
[Table("tblPanel")]
[Index(nameof(PanelKey), IsUnique = true)]
public partial class TblPanel
{
    [Key]
    public int PanelId { get; set; }

    /// <summary>
    /// Stable cross-environment identifier used by the content package subsystem to upsert
    /// records when migrating content between Dev, Pre-Prod, and Prod environments.
    /// </summary>
    public Guid ContentPackageId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// URL slug identifying the panel. Used as a path segment, so restricted to lowercase
    /// alphanumerics and hyphen.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string PanelKey { get; set; } = null!;

    [StringLength(100)]
    public string PanelName { get; set; } = null!;

    /// <summary>
    /// The location whose displays this panel controls. The panel's authority never extends
    /// beyond it.
    /// </summary>
    public int LocationId { get; set; }

    /// <summary>
    /// Asset Library location every screen on this panel draws from. A single shared library
    /// is what allows the asset-first interaction order: the grid can render before the user
    /// has chosen a screen.
    /// </summary>
    public int? AssetLibraryLocationId { get; set; }

    /// <summary>
    /// Location bundle supplying the panel's HTML/JS/CSS. Referenced by key rather than PK so
    /// the reference survives export/import across environments.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string BundleKey { get; set; } = null!;

    /// <summary>
    /// Version to serve, or null to follow the bundle's CurrentVersion.
    /// </summary>
    [StringLength(32)]
    [Unicode(false)]
    public string? PinnedVersion { get; set; }

    /// <summary>
    /// SHA-256 of the URL token. The plaintext token is shown once at creation and never
    /// stored, so a database read cannot yield a working panel URL.
    /// </summary>
    [StringLength(64)]
    [Unicode(false)]
    public string TokenHash { get; set; } = null!;

    /// <summary>
    /// Maps the bundle's declared hotspot keys to displays:
    /// [{"hotspotKey":"screen-1","displayId":8123}, ...]. Keyed on DisplayId rather than name
    /// so renaming a display does not break the panel.
    /// </summary>
    public string? HotspotMapJson { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Last time the panel called in, so a silent panel is visible in the admin tab. Updated
    /// at most once a minute rather than on every poll.
    /// </summary>
    [Column(TypeName = "datetime")]
    public DateTime? LastSeenUtc { get; set; }

    [StringLength(45)]
    [Unicode(false)]
    public string? LastSeenIp { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreateDate { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? LastEditDate { get; set; }

    [StringLength(255)]
    public string? LastEditUser { get; set; }

    [ForeignKey("LocationId")]
    public virtual TblLocation Location { get; set; } = null!;
}
