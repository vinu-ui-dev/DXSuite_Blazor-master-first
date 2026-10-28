using DXSuite.Director.Models;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Models.ZoneContent;

namespace DXSuite.Director.Services
{
    /// <summary>
    /// What a cached manifest is keyed by. A display and a location can share a numeric id, so the
    /// type is part of the key and of every bucket that invalidation reaches.
    /// </summary>
    public enum ManifestBucketType
    {
        Location,
        Display
    }

    public readonly record struct ManifestBucket(ManifestBucketType Type, int Id)
    {
        public static ManifestBucket ForLocation(int locationId) => new(ManifestBucketType.Location, locationId);
        public static ManifestBucket ForDisplay(int displayId) => new(ManifestBucketType.Display, displayId);
    }

    /// <summary>
    /// Caches player content publications to reduce DB queries and file I/O. One entry per tenant
    /// and bucket: a publication is a function of content, and anything that depends on the
    /// requesting device is resolved at delivery.
    /// </summary>
    public interface IManifestCacheService
    {
        bool TryGetManifest(string tenantId, ManifestBucket bucket, out CachedManifest? manifest);

        /// <summary>
        /// Caches a publication and computes its content-hash Version. maxAge overrides the default TTL.
        /// </summary>
        void SetManifest(string tenantId, ManifestBucket bucket, CachedManifest manifest, TimeSpan? maxAge = null);

        /// <summary>
        /// Invalidates a location's publication (call when content changes), and every layout or
        /// display publication recorded as drawing content from it.
        /// </summary>
        void InvalidateLocation(string tenantId, int locationId);

        /// <summary>
        /// Invalidates a display's publication, for changes to the display itself such as its mode
        /// or schedule events.
        /// </summary>
        void InvalidateDisplay(string tenantId, int displayId);

        /// <summary>
        /// Records which playlist locations a layout's zones source, replacing any earlier record,
        /// so invalidating a source also invalidates the layout. Called whenever a layout is published.
        /// </summary>
        void SetLayoutSources(string tenantId, int layoutLocationId, IEnumerable<int> sourceLocationIds);

        /// <summary>
        /// Records which locations a display's schedule draws its assets from, replacing any earlier
        /// record, so invalidating one of those locations also invalidates the display. Called
        /// whenever a display is published.
        /// </summary>
        void SetDisplaySources(string tenantId, int displayId, IEnumerable<int> sourceLocationIds);

        /// <summary>
        /// Remembers the delivery version last handed to one device, and reads it back.
        ///
        /// The cache holds publications, which are shared and clock-free, while a player echoes the
        /// delivery version it was actually served. Comparing an echo against a publication would
        /// call every device stale forever, so "is this device showing current content" is answered
        /// against what that device was last sent.
        ///
        /// In memory and per device, like the manifests themselves. A restart forgets it and every
        /// device reads as reported-but-uncomparable until its next poll, which is at most a few
        /// minutes away.
        /// </summary>
        void RecordDeliveredVersion(string tenantId, string deviceId, string? version);

        string? GetDeliveredVersion(string tenantId, string deviceId);

        /// <summary>
        /// Hashes a resolved delivery so a player can be told NotModified. The cached publication
        /// carries its own version; this one changes at window and day boundaries too.
        /// </summary>
        string ComputeDeliveryVersion(CachedManifest manifest);

        /// <summary>
        /// Hit and miss counts since startup, and the number of publications currently cached.
        /// </summary>
        CacheStatistics GetStatistics();
    }

    /// <summary>
    /// One schedule event of a display, with its content hydrated and device-neutral. Which event
    /// is on depends on the requesting device's clock, so the choice is deferred to delivery while
    /// the hydration, which is the expensive and clock-free half, is shared by every device under
    /// the display.
    /// </summary>
    public class CachedDisplayEvent : DXSuite.Director.Entities.IScheduleWindow
    {
        public int ScheduleEventId { get; set; }
        public bool DefaultContent { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public bool Sunday { get; set; }
        public bool Monday { get; set; }
        public bool Tuesday { get; set; }
        public bool Wednesday { get; set; }
        public bool Thursday { get; set; }
        public bool Friday { get; set; }
        public bool Saturday { get; set; }

        public List<PlayerContentItem> Assets { get; set; } = new();

        /// <summary>Parallel to <see cref="Assets"/>, for the weighted loop expansion.</summary>
        public List<int> Weights { get; set; } = new();
    }

    /// <summary>
    /// Cached manifest data (excludes device-specific fields like Update, CecCommand)
    /// </summary>
    public class CachedManifest
    {
        public int? LocationId { get; set; }
        public int? DisplayId { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool? Shuffle { get; set; }
        public bool? EnableFade { get; set; }
        public bool IsOverride { get; set; }
        public int? OverrideId { get; set; }
        public string? OverrideName { get; set; }
        public List<PlayerContentItem> Assets { get; set; } = new();
        public List<ZoneDisplayContent>? Zones { get; set; }

        /// <summary>
        /// The display path's publication: every schedule event with its assets already hydrated,
        /// so choosing between them costs nothing at request time. Null on every other path.
        /// </summary>
        public List<CachedDisplayEvent>? DisplayEvents { get; set; }
        public int? LayoutWidth { get; set; }
        public int? LayoutHeight { get; set; }
        public DateTime CachedAt { get; set; }

        /// <summary>
        /// Content hash of the manifest fields, assigned by SetManifest. Players echo this
        /// value in their heartbeat; an equality match answers the poll with NotModified.
        /// A hash (not a counter) so it survives restarts and cannot collide with a stale echo.
        /// </summary>
        public string? Version { get; set; }
    }

    public class CacheStatistics
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public int CurrentEntries { get; set; }
        public double HitRate => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) * 100 : 0;
    }
}
