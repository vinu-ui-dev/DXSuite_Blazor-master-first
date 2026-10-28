using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Services;

/// <summary>
/// Turns a cached playlist publication into what one device should be showing right now. Pure over
/// its inputs so the decision is testable; the caller stamps and records the delivery version.
/// </summary>
public static class PlaylistDelivery
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg" };

    /// <summary>
    /// Every window is resolved here, on the server, against the requesting device's own time zone,
    /// and the four window fields then ship as null. The player's own filter treats an asset with no
    /// window as always playable, so nulling them takes the device's OS clock out of content
    /// selection without needing a release on either fleet.
    ///
    /// Metadata targeting and the window are independent filters, both applied before the weighted
    /// loop is expanded, so a targeted-out asset contributes nothing to the loop. When nothing survives
    /// the player receives an empty manifest; there is no fallback content.
    ///
    /// The publication is shared by every device at the location, so each item is copied before
    /// anything here touches it.
    /// </summary>
    /// <param name="localNow">The device's local time, or null when its time zone does not resolve.</param>
    /// <param name="imageDurationSeconds">The location's image duration override, if it has one.</param>
    public static CachedManifest Resolve(
        CachedManifest publication,
        LocationTargeting targeting,
        IReadOnlyDictionary<string, string>? deviceMetadata,
        DateTime? localNow,
        int? imageDurationSeconds)
    {
        var player = MetadataSubjectResolver.DeviceSubject(deviceMetadata);

        // An unresolvable zone skips the window filter only. Declining to filter picks no clock and
        // fails toward a screen that is showing something: an unfiltered playlist is a logged defect,
        // a blank screen at a customer site is a truck roll. Targeting does not depend on a clock.
        var assets = publication.Assets
            .Where(a => PlaylistManifestRules.IsTargetedAt(targeting, a.MetadataValues, player))
            .Where(a => localNow is null || PlaylistWindow.IsActiveAt(a, localNow.Value))
            .Select(a => a.Copy())
            .ToList();

        foreach (var asset in assets)
        {
            asset.StartDate = null;
            asset.EndDate = null;
            asset.StartTime = null;
            asset.EndTime = null;
            asset.MetadataValues = null;
        }

        if (imageDurationSeconds.HasValue)
        {
            foreach (var asset in assets.Where(a => !a.IsWeb &&
                ImageExtensions.Contains(Path.GetExtension(a.FileName ?? ""))))
            {
                asset.Duration = imageDurationSeconds.Value;
            }
        }

        // Sort by PlayOrder, putting items without PlayOrder at the end
        assets = assets.OrderBy(item => item.PlayOrder ?? int.MaxValue).ToList();

        // Weights only apply to shuffled playlists; ordered playlists express
        // repeats as explicit uploads at each position
        if (publication.Shuffle == true)
        {
            assets = WeightedLoopExpander.Expand(assets, item => item.Weight);
        }

        return new CachedManifest
        {
            LocationId = publication.LocationId,
            Status = publication.Status,
            Shuffle = publication.Shuffle,
            EnableFade = publication.EnableFade,
            Assets = assets,
            CachedAt = publication.CachedAt
        };
    }
}
