using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Xml;

namespace DXSuite.Director.Services
{
    /// <summary>
    /// Caches parsed XML metadata from .metadata files.
    /// Eliminates repeated XML parsing on hot paths.
    /// </summary>
    public interface IMetadataCacheService
    {
        /// <summary>
        /// Get parsed metadata for a file. Returns cached version if available.
        /// </summary>
        ParsedMetadata? GetMetadata(string tenantId, string filePath);

        /// <summary>
        /// Get metadata asynchronously (preferred for new code)
        /// </summary>
        Task<ParsedMetadata?> GetMetadataAsync(string tenantId, string filePath);

        /// <summary>
        /// Invalidate cached metadata for a file
        /// </summary>
        void Invalidate(string filePath);

        /// <summary>
        /// Invalidate all metadata for a tenant location
        /// </summary>
        void InvalidateLocation(string tenantId, int locationId);
    }

    /// <summary>
    /// Parsed metadata fields from .metadata XML files
    /// </summary>
    public class ParsedMetadata
    {
        public int? PlayOrder { get; set; }
        public int? Duration { get; set; }
        public int? ApprovalStatus { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public string? Days { get; set; }
        public DateTime ParsedAt { get; set; }
    }

    public class MetadataCacheService : IMetadataCacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<MetadataCacheService> _logger;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        // Key: (tenantId, locationId) for bulk invalidation
        private readonly Dictionary<(string, int), HashSet<string>> _locationKeys = new();
        private readonly object _lockObject = new();

        public MetadataCacheService(IMemoryCache cache, ILogger<MetadataCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public ParsedMetadata? GetMetadata(string tenantId, string filePath)
        {
            var metadataPath = filePath + ".metadata";
            var cacheKey = $"metadata:{metadataPath}";

            if (_cache.TryGetValue(cacheKey, out ParsedMetadata? cached))
                return cached;

            var metadata = ParseMetadataFile(metadataPath);
            CacheMetadata(tenantId, cacheKey, filePath, metadata);
            return metadata;
        }

        public async Task<ParsedMetadata?> GetMetadataAsync(string tenantId, string filePath)
        {
            var metadataPath = filePath + ".metadata";
            var cacheKey = $"metadata:{metadataPath}";

            if (_cache.TryGetValue(cacheKey, out ParsedMetadata? cached))
                return cached;

            var metadata = await ParseMetadataFileAsync(metadataPath);
            CacheMetadata(tenantId, cacheKey, filePath, metadata);
            return metadata;
        }

        public void Invalidate(string filePath)
        {
            _cache.Remove($"metadata:{filePath}.metadata");
        }

        public void InvalidateLocation(string tenantId, int locationId)
        {
            lock (_lockObject)
            {
                var bucket = (tenantId, locationId);
                if (_locationKeys.TryGetValue(bucket, out var keys))
                {
                    foreach (var key in keys)
                        _cache.Remove(key);
                    _locationKeys.Remove(bucket);
                    _logger.LogDebug("Metadata cache invalidated for tenant {TenantId}, location {LocationId}: {Count} entries",
                        tenantId, locationId, keys.Count);
                }
            }
        }

        private void CacheMetadata(string tenantId, string cacheKey, string filePath, ParsedMetadata? metadata)
        {
            _cache.Set(cacheKey, metadata, new MemoryCacheEntryOptions().SetAbsoluteExpiration(_cacheDuration));

            var locationId = ExtractLocationId(filePath);
            if (locationId.HasValue)
            {
                lock (_lockObject)
                {
                    var bucket = (tenantId, locationId.Value);
                    if (!_locationKeys.ContainsKey(bucket))
                        _locationKeys[bucket] = new HashSet<string>();
                    _locationKeys[bucket].Add(cacheKey);
                }
            }
        }

        private static int? ExtractLocationId(string filePath)
        {
            // Path format: .../MediaAssets/{tenantId}/{locationId}/filename.ext
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var folderName = Path.GetFileName(directory);
                if (int.TryParse(folderName, out int locationId))
                    return locationId;
            }
            catch { }
            return null;
        }

        private ParsedMetadata? ParseMetadataFile(string metadataPath)
        {
            if (!File.Exists(metadataPath))
                return null;

            try
            {
                var doc = new XmlDocument();
                doc.Load(metadataPath);
                return ExtractMetadataFromXml(doc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse metadata file: {Path}", metadataPath);
                return null;
            }
        }

        private async Task<ParsedMetadata?> ParseMetadataFileAsync(string metadataPath)
        {
            if (!File.Exists(metadataPath))
                return null;

            try
            {
                // XmlDocument doesn't support async load; read file async then parse synchronously
                var xmlContent = await File.ReadAllTextAsync(metadataPath);
                var doc = new XmlDocument();
                doc.LoadXml(xmlContent);
                return ExtractMetadataFromXml(doc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse metadata file: {Path}", metadataPath);
                return null;
            }
        }

        private ParsedMetadata ExtractMetadataFromXml(XmlDocument doc)
        {
            var metadata = new ParsedMetadata { ParsedAt = DateTime.UtcNow };

            ReadInt(doc, "PlayOrder", v => metadata.PlayOrder = v);
            ReadInt(doc, "Duration", v => metadata.Duration = v);
            ReadInt(doc, "ApprovalStatus", v => metadata.ApprovalStatus = v);
            ReadDateTime(doc, "StartDate", v => metadata.StartDate = v);
            ReadDateTime(doc, "EndDate", v => metadata.EndDate = v);
            ReadTimeSpan(doc, "StartTime", v => metadata.StartTime = v);
            ReadTimeSpan(doc, "EndTime", v => metadata.EndTime = v);
            ReadString(doc, "Days", v => metadata.Days = v);

            return metadata;
        }

        private static void ReadInt(XmlDocument doc, string name, Action<int> set)
        {
            var val = doc.SelectSingleNode($"//field[@name='{name}']")?.Attributes?["value"]?.Value;
            if (val != null && int.TryParse(val, out int v)) set(v);
        }

        private static void ReadString(XmlDocument doc, string name, Action<string> set)
        {
            var val = doc.SelectSingleNode($"//field[@name='{name}']")?.Attributes?["value"]?.Value;
            if (val != null) set(val);
        }

        private static void ReadDateTime(XmlDocument doc, string name, Action<DateTime> set)
        {
            var val = doc.SelectSingleNode($"//field[@name='{name}']")?.Attributes?["value"]?.Value;
            if (val != null && DateTime.TryParse(val, out var v)) set(v);
        }

        private static void ReadTimeSpan(XmlDocument doc, string name, Action<TimeSpan> set)
        {
            var val = doc.SelectSingleNode($"//field[@name='{name}']")?.Attributes?["value"]?.Value;
            if (val != null && TimeSpan.TryParse(val, out var v)) set(v);
        }
    }
}
