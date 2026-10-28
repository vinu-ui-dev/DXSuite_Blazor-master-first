using DXSuite.Director.Models.ZoneContent;

namespace DXSuite.Director.Services;

/// <summary>
/// Decides what a playlist-sourced layout zone plays. The layout publication is cached once per layout
/// and shared by every device polling it, so it may only hold decisions that are the same for every
/// device; anything that depends on the asking device is made at delivery.
/// </summary>
public static class LayoutZoneRules
{
    /// <summary>
    /// Whether a source location's file belongs in the layout publication. Matches the direct
    /// playlist poll: a missing asset or approval row is legacy content that predates approvals.
    /// </summary>
    /// <param name="approvalStatus">The asset's approval status, or null when it has no asset or approval row.</param>
    public static bool IsPublished(bool enableContentApproval, int? approvalStatus) =>
        PlaylistManifestRules.IsPublished(enableContentApproval, approvalStatus);

    /// <summary>
    /// Builds the copy of a published layout that one device receives, targeting each playlist zone by
    /// its source location's rules. Targeting is a property of the source location and the device, so it
    /// is evaluated once per source and reused by every zone drawing on it. Card zones are returned as
    /// published for the caller to fill in. The published zones are shared, so they are never modified.
    /// </summary>
    /// <param name="targetingBySource">Rules for every <see cref="ZoneDisplayContent.TargetingLocationId"/> in the layout.</param>
    public static List<ZoneDisplayContent> ResolvePlaylistZones(
        IEnumerable<ZoneDisplayContent> published,
        IReadOnlyDictionary<int, LocationTargeting> targetingBySource,
        Dictionary<string, string>? deviceMetadata,
        string? deviceId)
    {
        var player = MetadataSubjectResolver.DeviceSubject(deviceMetadata);
        var targetedBySource = new Dictionary<int, List<ZoneContentItem>>();

        return published.Select(zone =>
        {
            if (zone.ZoneType == "card")
                return zone;

            List<ZoneContentItem> content;
            if (zone.TargetingLocationId is not int sourceId)
            {
                content = zone.Content;
            }
            else if (!targetedBySource.TryGetValue(sourceId, out content!))
            {
                var targeting = targetingBySource.TryGetValue(sourceId, out var rules) ? rules : LocationTargeting.Off;
                content = zone.Content
                    .Where(item => PlaylistManifestRules.IsTargetedAt(targeting, item.MetadataValues, player))
                    .ToList();
                targetedBySource[sourceId] = content;
            }

            return new ZoneDisplayContent
            {
                DisplayId = zone.DisplayId,
                ZoneNumber = zone.ZoneNumber,
                ZoneName = zone.ZoneName,
                ZoneType = zone.ZoneType,
                Layout = zone.Layout,
                Content = content.Select(item => new ZoneContentItem
                {
                    Url = item.ContentType == "web"
                        ? DeviceMetadataService.AppendMetadataToUrl(item.Url, deviceMetadata, deviceId)
                        : item.Url,
                    ContentType = item.ContentType,
                    Duration = item.Duration,
                    AssetId = item.AssetId
                }).ToList()
            };
        }).ToList();
    }
}
