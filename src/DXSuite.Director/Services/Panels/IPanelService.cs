using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Director.Models.Panels;

namespace DXSuite.Director.Services.Panels;

/// <summary>
/// Resolution, read, and publish operations for panels. Every method takes an already-resolved
/// <see cref="TblPanel"/> rather than a key, so a caller cannot reach panel data without having
/// gone through <see cref="ResolveAsync"/> first.
/// </summary>
public interface IPanelService
{
    /// <summary>
    /// Resolves a panel from its URL key and token. Returns null for an unknown key, a token
    /// mismatch, or a disabled panel — the caller must not distinguish these cases to the
    /// client, since doing so turns the endpoint into a panel-key oracle.
    /// </summary>
    Task<TblPanel?> ResolveAsync(string panelKey, string token, CancellationToken ct = default);

    Task<PanelContext> GetContextAsync(TblPanel panel, CancellationToken ct = default);

    /// <summary>
    /// The panel's shared Asset Library, filtered to what a panel may offer. Excludes deleted
    /// assets and anything still awaiting approval, because a panel has no reviewer standing
    /// behind it.
    /// </summary>
    Task<List<PanelAsset>> GetAssetsAsync(TblPanel panel, string siteUrl, CancellationToken ct = default);

    /// <summary>Current assignment for each mapped screen, in hotspot order.</summary>
    Task<List<PanelDisplayState>> GetDisplayStatesAsync(TblPanel panel, string siteUrl, CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the "public.panel" document and writes it to bundle storage with an
    /// incremented revision. Called by the host after every successful write so the panel's
    /// own JavaScript never has to remember to publish.
    /// </summary>
    Task PublishAsync(TblPanel panel, string siteUrl, CancellationToken ct = default);

    /// <summary>
    /// Records that the panel called in. Throttled to at most one write a minute so a 10s poll
    /// does not generate a database write per tick.
    /// </summary>
    Task TouchLastSeenAsync(TblPanel panel, string? remoteIp, CancellationToken ct = default);

    /// <summary>Whether the display is one this panel is allowed to write to.</summary>
    bool IsMappedDisplay(TblPanel panel, int displayId);
}
