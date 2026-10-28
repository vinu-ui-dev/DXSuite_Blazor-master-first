using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DXSuite.Shared.Models;
using System.Collections.Concurrent;

namespace DXSuite.Director.Services
{
    /// <summary>
    /// Caches media folder file listings with FileSystemWatcher-based invalidation.
    /// Eliminates repeated Directory.GetFiles() calls on hot paths.
    /// </summary>
    public interface IMediaFolderCacheService
    {
        /// <summary>
        /// Get cached file list for a tenant location's media folder
        /// </summary>
        string[] GetMediaFiles(string tenantId, int locationId);

        /// <summary>
        /// Invalidate cache for a specific tenant location
        /// </summary>
        void InvalidateLocation(string tenantId, int locationId);

        /// <summary>
        /// Get cache statistics
        /// </summary>
        MediaFolderCacheStatistics GetStatistics();
    }

    public class MediaFolderCacheStatistics
    {
        public long Hits { get; set; }
        public long Misses { get; set; }
        public int WatchedFolders { get; set; }
        public int CachedEntries { get; set; }
        public double HitRate => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) * 100 : 0;
    }

    public class MediaFolderCacheService : IMediaFolderCacheService, IDisposable
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<MediaFolderCacheService> _logger;
        private readonly IManifestCacheService _manifestCache;
        private readonly IMetadataCacheService _metadataCache;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly DXSuite.Shared.Multitenancy.ITenantStore _tenantStore;
        private readonly bool _legacyFallbackEnabled;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        // Key: "tenantId:locationId" — one watcher per unique tenant+location folder
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
        private readonly object _lockObject = new();

        private static readonly HashSet<string> _mediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".jpg", ".jpeg", ".png", ".json"
        };

        private long _hits;
        private long _misses;

        public MediaFolderCacheService(
            IMemoryCache cache,
            ILogger<MediaFolderCacheService> logger,
            IManifestCacheService manifestCache,
            IMetadataCacheService metadataCache,
            IWebHostEnvironment env,
            IConfiguration config,
            DXSuite.Shared.Multitenancy.ITenantStore tenantStore,
            IOptions<FeatureFlags> featureFlags)
        {
            _cache = cache;
            _logger = logger;
            _manifestCache = manifestCache;
            _metadataCache = metadataCache;
            _env = env;
            _config = config;
            _tenantStore = tenantStore;
            _legacyFallbackEnabled = featureFlags.Value.LegacyMediaFolderFallback;
        }

        /// <summary>
        /// MediaAssets base for a tenant: under the tenant's media root when tenant media
        /// isolation is enabled, the global web root otherwise. Store lookups are in-memory
        /// (both tenant store implementations cache), so this is safe on hot paths.
        /// </summary>
        private string ResolveMediaBase(string tenantId)
        {
            var tenant = _tenantStore.GetAllEnabledAsync().GetAwaiter().GetResult()
                .FirstOrDefault(t => t.TenantId == tenantId);
            var root = tenant != null
                ? DXSuite.Shared.Multitenancy.TenantMediaPaths.GetRoot(tenant, _env, _config)
                : _env.WebRootPath;
            return Path.Combine(root, "MediaAssets");
        }

        public string[] GetMediaFiles(string tenantId, int locationId)
        {
            var cacheKey = $"mediafiles:{tenantId}:{locationId}";

            if (_cache.TryGetValue(cacheKey, out string[]? files) && files != null)
            {
                Interlocked.Increment(ref _hits);
                return files;
            }

            Interlocked.Increment(ref _misses);

            var tenantPath = Path.Combine(ResolveMediaBase(tenantId), locationId.ToString());
            var legacyPath = Path.Combine(_env.WebRootPath, "MediaAssets", locationId.ToString());

            // In deployments being migrated to tenant-scoped media roots, media may still
            // reside at the global flat path. With tenant media isolation disabled the two
            // paths are identical, so the fallback only matters mid-migration.
            var folderPath = (_legacyFallbackEnabled && !Directory.Exists(tenantPath))
                ? legacyPath
                : tenantPath;

            if (!Directory.Exists(folderPath))
            {
                _cache.Set(cacheKey, Array.Empty<string>(), TimeSpan.FromSeconds(30));
                return Array.Empty<string>();
            }

            files = Directory
                .GetFiles(folderPath, "*.*")
                .Where(f => _mediaExtensions.Contains(Path.GetExtension(f)))
                .ToArray();

            _cache.Set(cacheKey, files, new MemoryCacheEntryOptions().SetAbsoluteExpiration(_cacheDuration));

            EnsureWatcher(tenantId, locationId, folderPath);

            _logger.LogDebug("MediaFolder cache populated: Tenant {TenantId}, Location {LocationId}, {Count} files",
                tenantId, locationId, files.Length);

            return files;
        }

        public void InvalidateLocation(string tenantId, int locationId)
        {
            _cache.Remove($"mediafiles:{tenantId}:{locationId}");
            // Sidecars are read through their own cache while the publication rebuilds, so they drop
            // first or the rebuild would publish stale play order.
            _metadataCache.InvalidateLocation(tenantId, locationId);
            _manifestCache.InvalidateLocation(tenantId, locationId);
            _logger.LogInformation("MediaFolder cache invalidated: Tenant {TenantId}, Location {LocationId}", tenantId, locationId);
        }

        public MediaFolderCacheStatistics GetStatistics() => new()
        {
            Hits = _hits,
            Misses = _misses,
            WatchedFolders = _watchers.Count,
            CachedEntries = _watchers.Count
        };

        private void EnsureWatcher(string tenantId, int locationId, string folderPath)
        {
            var watcherKey = $"{tenantId}:{locationId}";
            if (_watchers.ContainsKey(watcherKey))
                return;

            lock (_lockObject)
            {
                if (_watchers.ContainsKey(watcherKey))
                    return;

                try
                {
                    var watcher = new FileSystemWatcher(folderPath)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += (s, e) => OnFileChanged(tenantId, locationId, e.FullPath, "Created");
                    watcher.Deleted += (s, e) => OnFileChanged(tenantId, locationId, e.FullPath, "Deleted");
                    watcher.Changed += (s, e) => OnFileChanged(tenantId, locationId, e.FullPath, "Changed");
                    watcher.Renamed += (s, e) => OnFileChanged(tenantId, locationId, e.FullPath, "Renamed");

                    _watchers[watcherKey] = watcher;

                    _logger.LogDebug("FileSystemWatcher created for tenant {TenantId}, location {LocationId}: {Path}",
                        tenantId, locationId, folderPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create FileSystemWatcher for tenant {TenantId}, location {LocationId}",
                        tenantId, locationId);
                }
            }
        }

        private void OnFileChanged(string tenantId, int locationId, string filePath, string changeType)
        {
            var extension = Path.GetExtension(filePath);
            if (_mediaExtensions.Contains(extension) || extension.Equals(".metadata", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Media file {ChangeType}: {File}, invalidating tenant {TenantId}, location {LocationId}",
                    changeType, Path.GetFileName(filePath), tenantId, locationId);
                InvalidateLocation(tenantId, locationId);
            }
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }
}
