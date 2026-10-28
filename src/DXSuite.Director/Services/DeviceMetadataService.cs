using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services
{
    public interface IDeviceMetadataService
    {
        /// <summary>
        /// Gets all metadata key/value pairs for a device as a dictionary
        /// </summary>
        Task<Dictionary<string, string>> GetMetadataAsync(string deviceId);

        /// <summary>
        /// Gets all metadata records for a device (for UI editing)
        /// </summary>
        Task<List<TblDeviceMetadata>> GetMetadataRecordsAsync(string deviceId);

        /// <summary>
        /// Sets a device's single value for a key, trimmed. A device holds one value per key; a blank
        /// value removes the key.
        /// </summary>
        Task SetMetadataAsync(string deviceId, string key, string value);

        /// <summary>
        /// Deletes a metadata key for a device
        /// </summary>
        Task DeleteMetadataAsync(string deviceId, string key);

        /// <summary>
        /// Deletes a metadata record by ID
        /// </summary>
        Task DeleteMetadataByIdAsync(int deviceMetadataId);

        /// <summary>
        /// Gets a list of known/common metadata keys across all devices
        /// </summary>
        Task<List<string>> GetKnownKeysAsync();
    }

    public class DeviceMetadataService : IDeviceMetadataService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DeviceMetadataService> _logger;

        // Common metadata keys that are suggested in the UI.
        //
        // zipCode and propertyName are gone: they were a shadow, per-device, free-text version of
        // the site data tblSite now owns. Existing rows are untouched and still delivered, because
        // this list is only an affordance for authoring new ones.
        //
        // "timezone" stays, and stays a decoy. Devices carry one today, it reaches the player, and
        // displayManager appends it to web content URLs as a query parameter. Nothing interprets it
        // as a time zone, it is not tblDevice.TimeZone, and no part of the scheduling work reads it.
        private static readonly List<string> CommonKeys = new()
        {
            "locale",
            "timezone",
            "region"
        };

        public DeviceMetadataService(IServiceScopeFactory scopeFactory, ILogger<DeviceMetadataService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<Dictionary<string, string>> GetMetadataAsync(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return new Dictionary<string, string>();
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var metadata = await db.TblDeviceMetadata
                .Where(m => m.DeviceId == deviceId)
                .ToDictionaryAsync(m => m.MetadataKey, m => m.MetadataValue);

            return metadata;
        }

        public async Task<List<TblDeviceMetadata>> GetMetadataRecordsAsync(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return new List<TblDeviceMetadata>();
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            return await db.TblDeviceMetadata
                .Where(m => m.DeviceId == deviceId)
                .OrderBy(m => m.MetadataKey)
                .ToListAsync();
        }

        public async Task SetMetadataAsync(string deviceId, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("Device ID is required", nameof(deviceId));
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Metadata key is required", nameof(key));
            }

            value = value?.Trim() ?? "";
            if (value.Length == 0)
            {
                await DeleteMetadataAsync(deviceId, key);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Check if metadata already exists (upsert)
            var existing = await db.TblDeviceMetadata
                .FirstOrDefaultAsync(m => m.DeviceId == deviceId && m.MetadataKey == key);

            if (existing != null)
            {
                existing.MetadataValue = value;
                _logger.LogDebug("Updated metadata for device {DeviceId}: {Key}={Value}", deviceId, key, value);
            }
            else
            {
                var metadata = new TblDeviceMetadata
                {
                    DeviceId = deviceId,
                    MetadataKey = key,
                    MetadataValue = value,
                    CreateDate = DateTime.UtcNow
                };
                db.TblDeviceMetadata.Add(metadata);
                _logger.LogDebug("Added metadata for device {DeviceId}: {Key}={Value}", deviceId, key, value);
            }

            await db.SaveChangesAsync();
        }

        public async Task DeleteMetadataAsync(string deviceId, string key)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblDeviceMetadata
                .FirstOrDefaultAsync(m => m.DeviceId == deviceId && m.MetadataKey == key);

            if (existing != null)
            {
                db.TblDeviceMetadata.Remove(existing);
                await db.SaveChangesAsync();
                _logger.LogDebug("Deleted metadata for device {DeviceId}: {Key}", deviceId, key);
            }
        }

        public async Task DeleteMetadataByIdAsync(int deviceMetadataId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var existing = await db.TblDeviceMetadata.FindAsync(deviceMetadataId);

            if (existing != null)
            {
                db.TblDeviceMetadata.Remove(existing);
                await db.SaveChangesAsync();
                _logger.LogDebug("Deleted metadata ID {MetadataId} for device {DeviceId}", deviceMetadataId, existing.DeviceId);
            }
        }

        public async Task<List<string>> GetKnownKeysAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            // Get all unique keys from the database
            var dbKeys = await db.TblDeviceMetadata
                .Select(m => m.MetadataKey)
                .Distinct()
                .ToListAsync();

            // Merge with common keys, remove duplicates, sort
            var allKeys = CommonKeys
                .Union(dbKeys)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            return allKeys;
        }

        /// <summary>
        /// Appends device metadata as query parameters to a URL.
        /// Used to pass device-specific values (like zipCode) to web content.
        /// This is a static helper that can be used without instantiating the service.
        /// </summary>
        /// <param name="url">The base URL to append parameters to</param>
        /// <param name="metadata">Dictionary of metadata key/value pairs</param>
        /// <param name="deviceId">Optional device ID to include as a parameter</param>
        /// <returns>URL with metadata appended as query parameters</returns>
        public static string AppendMetadataToUrl(string url, Dictionary<string, string>? metadata, string? deviceId = null)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            var hasMetadata = metadata != null && metadata.Count > 0;
            var hasDeviceId = !string.IsNullOrEmpty(deviceId);

            if (!hasMetadata && !hasDeviceId)
            {
                return url;
            }

            var separator = url.Contains('?') ? "&" : "?";
            var parameters = new List<string>();

            // Add metadata parameters
            if (hasMetadata)
            {
                parameters.AddRange(metadata!.Select(m =>
                    $"{Uri.EscapeDataString(m.Key)}={Uri.EscapeDataString(m.Value)}"));
            }

            // Add deviceId if provided and not already in metadata
            if (hasDeviceId && (metadata == null || !metadata.ContainsKey("deviceId")))
            {
                parameters.Add($"deviceId={Uri.EscapeDataString(deviceId!)}");
            }

            return url + separator + string.Join("&", parameters);
        }
    }
}
