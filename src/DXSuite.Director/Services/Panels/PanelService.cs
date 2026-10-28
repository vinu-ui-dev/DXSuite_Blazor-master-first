using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Models.Panels;
using DXSuite.Director.Services.LocationBundles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services.Panels;

public class PanelService : IPanelService
{
    /// <summary>
    /// Storage key panels publish to. Carries the "public." prefix so
    /// LocationBundlePlayerController will serve it to a signage:read API key.
    /// </summary>
    public const string PublishStorageKey = LocationBundleConstants.PublicKeyPrefix + "panel";

    /// <summary>Minimum gap between LastSeen writes, so a 10s poll does not write every tick.</summary>
    private static readonly TimeSpan LastSeenThrottle = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDirectorDBContext _db;
    private readonly ILogger<PanelService> _logger;

    public PanelService(IDirectorDBContext db, ILogger<PanelService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Token handling
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a URL-safe token. 32 bytes because this credential sits in a player config
    /// indefinitely and is never rotated on a schedule.
    /// </summary>
    public static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    // ────────────────────────────────────────────────────────────────────────

    public async Task<TblPanel?> ResolveAsync(string panelKey, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(panelKey) || string.IsNullOrWhiteSpace(token))
            return null;

        var panel = await _db.TblPanels
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PanelKey == panelKey, ct);

        if (panel == null || !panel.IsEnabled)
            return null;

        // Fixed-time compare so response latency cannot be used to recover the token a byte
        // at a time. Both sides are hex of a SHA-256, so lengths always match on a real row.
        var supplied = Encoding.UTF8.GetBytes(HashToken(token));
        var stored = Encoding.UTF8.GetBytes(panel.TokenHash ?? string.Empty);
        if (supplied.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(supplied, stored))
            return null;

        return panel;
    }

    public bool IsMappedDisplay(TblPanel panel, int displayId) =>
        ParseHotspots(panel).Any(h => h.DisplayId == displayId);

    public async Task<PanelContext> GetContextAsync(TblPanel panel, CancellationToken ct = default)
    {
        var location = await _db.TblLocations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.LocationId == panel.LocationId, ct);

        return new PanelContext
        {
            PanelKey = panel.PanelKey,
            PanelName = panel.PanelName,
            LocationId = panel.LocationId,
            LocationName = location?.LocationName ?? string.Empty,
            Hotspots = ParseHotspots(panel)
        };
    }

    public async Task<List<PanelAsset>> GetAssetsAsync(TblPanel panel, string siteUrl, CancellationToken ct = default)
    {
        if (!panel.AssetLibraryLocationId.HasValue)
            return new List<PanelAsset>();

        var libraryId = panel.AssetLibraryLocationId.Value;

        var assets = await _db.TblAssets
            .AsNoTracking()
            .Include(a => a.ContentMetadata)
            .Where(a => a.LocationId == libraryId && !a.IsDeleted)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.AssetName)
            .ToListAsync(ct);

        // A panel has no reviewer standing behind it, so anything not cleared for air is not
        // offered. Assets with no approval record are corporate content and always allowed.
        var assetIds = assets.Select(a => a.AssetId).ToList();
        var blocked = await _db.TblAssetApprovals
            .AsNoTracking()
            .Where(ap => assetIds.Contains(ap.AssetId) && ap.ApprovalStatus != (int)Types.ApprovalStatus.Approved)
            .Select(ap => ap.AssetId)
            .ToListAsync(ct);
        var blockedSet = blocked.ToHashSet();

        return assets
            .Where(a => !blockedSet.Contains(a.AssetId))
            .Select(a => new PanelAsset
            {
                AssetId = a.AssetId,
                AssetName = ResolveAssetName(a),
                Kind = AssetContentKind.FromAsset(a),
                Url = AssetContentKind.ResolveUrl(a, siteUrl),
                ThumbnailUrl = ResolveThumbnailUrl(a, siteUrl),
                Duration = (int)a.Duration,
                Width = (int)a.AssetWidth,
                Height = (int)a.AssetHeight,
                Metadata = (a.ContentMetadata ?? new List<TblAssetContentMetadata>())
                    .Select(m => new PanelAssetMetadata
                    {
                        MetadataKey = m.MetadataKey,
                        MetadataValue = m.MetadataValue ?? string.Empty
                    })
                    .ToList()
            })
            .ToList();
    }

    public async Task<List<PanelDisplayState>> GetDisplayStatesAsync(TblPanel panel, string siteUrl, CancellationToken ct = default)
    {
        var hotspots = ParseHotspots(panel);
        if (hotspots.Count == 0) return new List<PanelDisplayState>();

        var displays = await LoadMappedDisplaysAsync(hotspots, ct);
        var states = new List<PanelDisplayState>();

        foreach (var hotspot in hotspots)
        {
            if (!displays.TryGetValue(hotspot.DisplayId, out var display))
                continue;

            var asset = await ResolveActiveAssetAsync(display, siteUrl, ct);

            states.Add(new PanelDisplayState
            {
                HotspotKey = hotspot.HotspotKey,
                DisplayId = display.DisplayId,
                DisplayName = display.DisplayName,
                AssetId = asset?.AssetId ?? 0,
                AssetName = asset == null ? string.Empty : ResolveAssetName(asset),
                Kind = asset == null ? AssetContentKind.None : AssetContentKind.FromAsset(asset),
                ThumbnailUrl = asset == null ? string.Empty : ResolveThumbnailUrl(asset, siteUrl)
            });
        }

        return states;
    }

    public async Task PublishAsync(TblPanel panel, string siteUrl, CancellationToken ct = default)
    {
        var hotspots = ParseHotspots(panel);
        var displays = await LoadMappedDisplaysAsync(hotspots, ct);

        var document = new PanelPublishedDocument
        {
            Revision = await NextRevisionAsync(panel.LocationId, ct),
            UpdatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            PanelKey = panel.PanelKey,
            PanelName = panel.PanelName,
            LocationId = panel.LocationId
        };

        for (int i = 0; i < hotspots.Count; i++)
        {
            var hotspot = hotspots[i];
            if (!displays.TryGetValue(hotspot.DisplayId, out var display))
                continue;

            var asset = await ResolveActiveAssetAsync(display, siteUrl, ct);

            // An unassigned screen is emitted with kind "none" rather than omitted, so a
            // consumer can tell a deliberate blank from a screen that is no longer mapped.
            document.Screens.Add(new PanelPublishedScreen
            {
                Index = i,
                HotspotKey = hotspot.HotspotKey,
                DisplayId = display.DisplayId,
                DisplayName = display.DisplayName,
                Kind = asset == null ? AssetContentKind.None : AssetContentKind.FromAsset(asset),
                Url = asset == null ? string.Empty : AssetContentKind.ResolveUrl(asset, siteUrl),
                AssetId = asset?.AssetId ?? 0,
                AssetName = asset == null ? string.Empty : ResolveAssetName(asset),
                Duration = asset == null ? 0 : (int)asset.Duration,
                Width = asset == null ? 0 : (int)asset.AssetWidth,
                Height = asset == null ? 0 : (int)asset.AssetHeight
            });
        }

        var json = JsonSerializer.Serialize(document, WriteOptions);

        var row = await _db.TblLocationBundleStorages
            .FirstOrDefaultAsync(s => s.LocationId == panel.LocationId && s.StorageKey == PublishStorageKey, ct);

        if (row == null)
        {
            _db.TblLocationBundleStorages.Add(new TblLocationBundleStorage
            {
                LocationId = panel.LocationId,
                StorageKey = PublishStorageKey,
                Value = json,
                LastEditDate = DateTime.UtcNow,
                LastEditUser = $"panel:{panel.PanelKey}"
            });
        }
        else
        {
            row.Value = json;
            row.LastEditDate = DateTime.UtcNow;
            row.LastEditUser = $"panel:{panel.PanelKey}";
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Panel {PanelKey} published revision {Revision} with {ScreenCount} screens",
            panel.PanelKey, document.Revision, document.Screens.Count);
    }

    public async Task TouchLastSeenAsync(TblPanel panel, string? remoteIp, CancellationToken ct = default)
    {
        if (panel.LastSeenUtc.HasValue && DateTime.UtcNow - panel.LastSeenUtc.Value < LastSeenThrottle)
            return;

        // ResolveAsync returns a no-tracking instance, so update by key rather than attaching
        // an entity the panel request path also reads from.
        var tracked = await _db.TblPanels.FirstOrDefaultAsync(p => p.PanelId == panel.PanelId, ct);
        if (tracked == null) return;

        tracked.LastSeenUtc = DateTime.UtcNow;
        tracked.LastSeenIp = remoteIp;
        await _db.SaveChangesAsync(ct);

        panel.LastSeenUtc = tracked.LastSeenUtc;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Internals
    // ────────────────────────────────────────────────────────────────────────

    private static List<PanelHotspotMapping> ParseHotspots(TblPanel panel)
    {
        if (string.IsNullOrWhiteSpace(panel.HotspotMapJson))
            return new List<PanelHotspotMapping>();

        try
        {
            return JsonSerializer.Deserialize<List<PanelHotspotMapping>>(panel.HotspotMapJson, ReadOptions)
                   ?? new List<PanelHotspotMapping>();
        }
        catch (JsonException)
        {
            // A malformed map must not take the panel down: it renders with no hotspots and an
            // admin can repair the mapping in the config tab.
            return new List<PanelHotspotMapping>();
        }
    }

    private async Task<Dictionary<int, TblDisplay>> LoadMappedDisplaysAsync(
        List<PanelHotspotMapping> hotspots, CancellationToken ct)
    {
        var ids = hotspots.Select(h => h.DisplayId).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, TblDisplay>();

        return await _db.TblDisplays
            .AsNoTracking()
            .Include(d => d.TblScheduleEvents)
            .Where(d => ids.Contains(d.DisplayId))
            .ToDictionaryAsync(d => d.DisplayId, ct);
    }

    /// <summary>
    /// The asset currently resolved for a display. Delegates to DisplayItem so a panel sees
    /// exactly what a player would, including schedule windows and the DefaultContent fallback.
    /// </summary>
    private async Task<TblAsset?> ResolveActiveAssetAsync(TblDisplay display, string siteUrl, CancellationToken ct)
    {
        var item = new DisplayItem(display);
        // Location-scoped with no device in hand, so this stays on the server clock by decision.
        var assignment = await item.GetContentAssignmentAsync(display, _db, siteUrl, DateTime.Now);
        return assignment.Content.FirstOrDefault()?.Asset;
    }

    private static string ResolveAssetName(TblAsset asset) =>
        !string.IsNullOrWhiteSpace(asset.AssetName)
            ? asset.AssetName
            : System.IO.Path.GetFileNameWithoutExtension(asset.AssetPath) ?? string.Empty;

    /// <summary>
    /// A still image safe to put in a grid. Videos resolve to the sibling Thumbnails/*.png the
    /// ingest pipeline writes; images are their own thumbnail. Web assets have none, and the
    /// bundle renders a placeholder rather than framing a live page in a tile.
    /// </summary>
    private static string ResolveThumbnailUrl(TblAsset asset, string siteUrl)
    {
        var kind = AssetContentKind.FromAsset(asset);
        if (kind == AssetContentKind.Web) return string.Empty;
        if (kind == AssetContentKind.Image) return siteUrl + asset.AssetPath;

        var path = asset.AssetPath ?? string.Empty;
        var slash = path.LastIndexOf('/');
        if (slash < 0) return string.Empty;

        var directory = path.Substring(0, slash);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        return $"{siteUrl}{directory}/Thumbnails/{fileName}.png";
    }

    /// <summary>
    /// Revision is read back out of the previously published document rather than kept in a
    /// column, so the number cannot drift from the document it describes.
    /// </summary>
    private async Task<long> NextRevisionAsync(int locationId, CancellationToken ct)
    {
        var existing = await _db.TblLocationBundleStorages
            .AsNoTracking()
            .Where(s => s.LocationId == locationId && s.StorageKey == PublishStorageKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(existing)) return 1;

        try
        {
            var previous = JsonSerializer.Deserialize<PanelPublishedDocument>(existing, ReadOptions);
            return (previous?.Revision ?? 0) + 1;
        }
        catch (JsonException)
        {
            return 1;
        }
    }
}
