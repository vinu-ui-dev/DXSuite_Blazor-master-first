using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using DXSuite.Director.Models;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Director.Models.ZoneContent;

namespace DXSuite.Director.Services
{
    public class ManifestCacheService : IManifestCacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<ManifestCacheService> _logger;
        // Every write to a publication input invalidates through IPublicationInvalidator, so the TTL bounds
        // memory and database load rather than how stale a screen may get.
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);

        // Buckets holding a live entry. Only statistics read it: a bucket's cache key is derived from
        // the bucket itself, so invalidation never needs it.
        private readonly HashSet<(string, ManifestBucket)> _cachedBuckets = new();
        private readonly object _lockObject = new();

        // A layout or display is cached under its own bucket but built from content at other
        // locations, so invalidating a source location has to reach every publication drawing on it.
        private readonly Dictionary<(string, int), HashSet<ManifestBucket>> _dependentsBySource = new();
        private readonly Dictionary<(string, ManifestBucket), HashSet<int>> _sourcesByDependent = new();

        // Statistics
        private long _hits;
        private long _misses;

        private readonly IPlayerWakeNotifier _wakeNotifier;

        public ManifestCacheService(IMemoryCache cache, ILogger<ManifestCacheService> logger, IPlayerWakeNotifier wakeNotifier)
        {
            _cache = cache;
            _logger = logger;
            _wakeNotifier = wakeNotifier;
        }

        public bool TryGetManifest(string tenantId, ManifestBucket bucket, out CachedManifest? manifest)
        {
            var cacheKey = BuildCacheKey(tenantId, bucket);

            if (_cache.TryGetValue(cacheKey, out manifest) && manifest != null)
            {
                Interlocked.Increment(ref _hits);
                _logger.LogDebug("Manifest cache HIT: {Key}", cacheKey);
                return true;
            }

            Interlocked.Increment(ref _misses);
            _logger.LogDebug("Manifest cache MISS: {Key}", cacheKey);
            manifest = null;
            return false;
        }

        public void SetManifest(string tenantId, ManifestBucket bucket, CachedManifest manifest, TimeSpan? maxAge = null)
        {
            var cacheKey = BuildCacheKey(tenantId, bucket);
            var ttl = maxAge ?? _cacheDuration;
            manifest.CachedAt = DateTime.UtcNow;
            manifest.Version = ComputeVersion(manifest);

            var options = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(ttl)
                .RegisterPostEvictionCallback((key, value, reason, state) =>
                {
                    _logger.LogDebug("Manifest cache evicted: {Key}, Reason: {Reason}", key, reason);
                    if (reason == EvictionReason.Replaced) return;

                    // An expiry callback can arrive after the bucket was cached again, so the entry is
                    // checked rather than assumed gone.
                    lock (_lockObject)
                    {
                        if (!_cache.TryGetValue(key, out _))
                            _cachedBuckets.Remove((tenantId, bucket));
                    }
                });

            _cache.Set(cacheKey, manifest, options);

            lock (_lockObject)
            {
                _cachedBuckets.Add((tenantId, bucket));
            }

            _logger.LogDebug("Manifest cached: {Key}, TTL {TTL}s", cacheKey, ttl.TotalSeconds);
        }

        public void InvalidateLocation(string tenantId, int locationId)
        {
            List<ManifestBucket> dependents;
            lock (_lockObject)
            {
                RemoveBucket(tenantId, ManifestBucket.ForLocation(locationId));

                dependents = _dependentsBySource.TryGetValue((tenantId, locationId), out var tracked)
                    ? tracked.ToList()
                    : new List<ManifestBucket>();
                foreach (var dependent in dependents)
                    RemoveBucket(tenantId, dependent);
            }

            // Invalidation means content changed: wake the bucket's players even when no
            // cache entry existed (the next poll rebuilds and delivers the new manifest)
            _wakeNotifier.NotifyManifestBucket(tenantId, locationId);
            foreach (var dependent in dependents)
                _wakeNotifier.NotifyManifestBucket(tenantId, dependent.Id);
        }

        public void InvalidateDisplay(string tenantId, int displayId)
        {
            lock (_lockObject)
            {
                RemoveBucket(tenantId, ManifestBucket.ForDisplay(displayId));
            }

            _wakeNotifier.NotifyManifestBucket(tenantId, displayId);
        }

        public void SetLayoutSources(string tenantId, int layoutLocationId, IEnumerable<int> sourceLocationIds)
            => SetSources(tenantId, ManifestBucket.ForLocation(layoutLocationId), sourceLocationIds);

        public void SetDisplaySources(string tenantId, int displayId, IEnumerable<int> sourceLocationIds)
            => SetSources(tenantId, ManifestBucket.ForDisplay(displayId), sourceLocationIds);

        private void SetSources(string tenantId, ManifestBucket dependent, IEnumerable<int> sourceLocationIds)
        {
            var sources = sourceLocationIds.ToHashSet();
            lock (_lockObject)
            {
                if (_sourcesByDependent.TryGetValue((tenantId, dependent), out var previous))
                {
                    foreach (var source in previous.Except(sources))
                    {
                        if (_dependentsBySource.TryGetValue((tenantId, source), out var dependents)
                            && dependents.Remove(dependent) && dependents.Count == 0)
                            _dependentsBySource.Remove((tenantId, source));
                    }
                }

                _sourcesByDependent[(tenantId, dependent)] = sources;
                foreach (var source in sources)
                {
                    if (!_dependentsBySource.TryGetValue((tenantId, source), out var dependents))
                        _dependentsBySource[(tenantId, source)] = dependents = new HashSet<ManifestBucket>();
                    dependents.Add(dependent);
                }
            }
        }

        // Caller holds _lockObject.
        private void RemoveBucket(string tenantId, ManifestBucket bucket)
        {
            var cacheKey = BuildCacheKey(tenantId, bucket);
            _cache.Remove(cacheKey);
            if (_cachedBuckets.Remove((tenantId, bucket)))
                _logger.LogInformation("Manifest cache invalidated: {Key}", cacheKey);
        }

        public CacheStatistics GetStatistics()
        {
            lock (_lockObject)
            {
                return new CacheStatistics
                {
                    Hits = Interlocked.Read(ref _hits),
                    Misses = Interlocked.Read(ref _misses),
                    CurrentEntries = _cachedBuckets.Count
                };
            }
        }

        /// <summary>
        /// The delivery version identifies what one device should be showing right now, so it
        /// additionally changes at every window and day boundary and differs between time zones at
        /// the same location. It is deliberately not the version the manifest log records: a window
        /// boundary is not a content change and must not look like one in an audit table.
        /// </summary>
        public string ComputeDeliveryVersion(CachedManifest manifest) => ComputeVersion(manifest);

        public void RecordDeliveredVersion(string tenantId, string deviceId, string? version)
        {
            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(version)) return;

            _cache.Set(BuildDeliveryKey(tenantId, deviceId), version,
                new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromDays(1)));
        }

        public string? GetDeliveredVersion(string tenantId, string deviceId)
            => _cache.TryGetValue(BuildDeliveryKey(tenantId, deviceId), out string? version) ? version : null;

        private static string BuildDeliveryKey(string tenantId, string deviceId)
            => $"manifest:delivered:{tenantId}:{deviceId}";

        private static string BuildCacheKey(string tenantId, ManifestBucket bucket)
            => bucket.Type == ManifestBucketType.Display
                ? $"manifest:display:{tenantId}:{bucket.Id}"
                : $"manifest:loc:{tenantId}:{bucket.Id}";

        // Hash only the content fields: CachedAt and Version itself are excluded so
        // re-caching unchanged content yields the same version and players stay on
        // NotModified across cache rebuilds and server restarts.
        private static string ComputeVersion(CachedManifest m)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                m.LocationId,
                m.DisplayId,
                m.Status,
                m.Shuffle,
                m.EnableFade,
                m.IsOverride,
                m.OverrideId,
                m.Assets,
                m.DisplayEvents,
                m.Zones,
                m.LayoutWidth,
                m.LayoutHeight
            });

            // Tags never reach the wire, so the serialized assets omit them. A publication carrying
            // any hashes them on top: retagging an asset changes what players receive and has to read
            // as a content change. Untagged publications and every delivery hash exactly as they did
            // before tags were carried, so no player is sent a version it does not need.
            if (m.Assets.Any(a => a.MetadataValues is { Count: > 0 }))
            {
                payload = payload.Concat(JsonSerializer.SerializeToUtf8Bytes(
                    m.Assets.Select(a => a.MetadataValues?
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(kv => new { kv.Key, Values = kv.Value.Order(StringComparer.OrdinalIgnoreCase) }))
                )).ToArray();
            }

            return Convert.ToHexString(SHA256.HashData(payload))[..16];
        }
    }
}
