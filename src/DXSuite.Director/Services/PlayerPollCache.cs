using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Entities;
using DXSuite.Director.Models.BrightSign;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

/// <summary>
/// Per-device slow-changing poll data: device metadata, the HTTP-fallback setting, and
/// the app-update check. These were separate queries on every heartbeat; a short cache
/// amortizes them to one refresh per device per minute. Changes (metadata edits, update
/// assignments, setting toggles) reach the device within the TTL plus one poll.
/// </summary>
public class PlayerPollFlags
{
    public Dictionary<string, string>? DeviceMetadata { get; set; }
    public bool AllowHttpFallback { get; set; } = true;
    public UpdateInfo? Update { get; set; }
    public UpdateInfo? BootloaderUpdate { get; set; }
}

public interface IPlayerPollFlagCache
{
    Task<PlayerPollFlags> GetAsync(string deviceId, string? clientAppVersion, string? clientBootloaderVersion = null);
}

public class PlayerPollFlagCache : IPlayerPollFlagCache
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IDirectorDBContext _db;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;
    private readonly IDeviceMetadataService _deviceMetadataService;
    private readonly IUpdateService _updateService;
    private readonly IBatchDeploymentService _batchDeploymentService;

    public PlayerPollFlagCache(
        IDirectorDBContext db,
        IMemoryCache cache,
        ITenantContext tenantContext,
        IDeviceMetadataService deviceMetadataService,
        IUpdateService updateService,
        IBatchDeploymentService batchDeploymentService)
    {
        _db = db;
        _cache = cache;
        _tenantContext = tenantContext;
        _deviceMetadataService = deviceMetadataService;
        _updateService = updateService;
        _batchDeploymentService = batchDeploymentService;
    }

    public async Task<PlayerPollFlags> GetAsync(string deviceId, string? clientAppVersion, string? clientBootloaderVersion = null)
    {
        // Both version lines are part of the key so a device that just updated either of them
        // misses the cache and gets a fresh check against what it now runs
        var cacheKey = $"pollflags:{_tenantContext.TenantId}:{deviceId}:{clientAppVersion ?? "none"}:{clientBootloaderVersion ?? "none"}";
        if (_cache.TryGetValue(cacheKey, out PlayerPollFlags? cached) && cached != null)
            return cached;

        var flags = new PlayerPollFlags
        {
            DeviceMetadata = await _deviceMetadataService.GetMetadataAsync(deviceId)
        };

        var httpFallbackSetting = await _db.TblSystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == "engage:allowHttpFallback");
        flags.AllowHttpFallback = httpFallbackSetting?.SettingValue != "false";

        if (clientAppVersion != null)
        {
            var updateResult = await _updateService.CheckForUpdateAsync(deviceId, clientAppVersion, clientBootloaderVersion);
            if (updateResult.UpdateAvailable)
            {
                var shouldTrigger = await _batchDeploymentService.ShouldTriggerUpdateForDeviceAsync(deviceId);

                var offer = new UpdateInfo
                {
                    UpdateAvailable = true,
                    TargetVersion = updateResult.TargetVersion,
                    AppVersionId = updateResult.AppVersionId,
                    DownloadUrl = updateResult.DownloadUrl,
                    FileSize = updateResult.FileSize,
                    FileHash = updateResult.FileHash,
                    ReleaseNotes = updateResult.ReleaseNotes,
                    RequiresReboot = updateResult.RequiresReboot,
                    TriggerUpdateNow = shouldTrigger,
                    ArtifactKind = updateResult.ArtifactKind,
                    MinBootloaderVersion = updateResult.MinBootloaderVersion
                };

                // The kind decides which slot carries the offer. Only one is ever set, because the
                // server sequences the two rather than leaving the player to.
                if (updateResult.ArtifactKind == ArtifactKinds.Bootloader)
                    flags.BootloaderUpdate = offer;
                else
                    flags.Update = offer;

                // Marking is once per TTL rather than once per poll; cached hits redeliver
                // TriggerUpdateNow to a device already updating, which is harmless
                if (shouldTrigger)
                {
                    await _batchDeploymentService.MarkDeviceUpdateTriggeredAsync(deviceId);
                }
            }
        }

        _cache.Set(cacheKey, flags, CacheTtl);
        return flags;
    }
}

/// <summary>
/// Skips the per-device override query for tenants with no manually active and no
/// scheduled overrides (the common case). The predicate includes IsScheduled because
/// schedule-driven overrides become effectively active by wall clock without a write;
/// their time windows are still evaluated in the per-device path. Any override write
/// invalidates the gate so an activation reaches devices on their next poll.
/// </summary>
public interface IActiveOverrideGate
{
    Task<bool> HasOverridesAsync(IDirectorDBContext db, string tenantId);
    void Invalidate();
}

public class ActiveOverrideGate : IActiveOverrideGate
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, (bool HasOverrides, DateTime CachedAt)> _byTenant = new();

    public async Task<bool> HasOverridesAsync(IDirectorDBContext db, string tenantId)
    {
        if (_byTenant.TryGetValue(tenantId, out var entry) && DateTime.UtcNow - entry.CachedAt < CacheTtl)
            return entry.HasOverrides;

        var hasOverrides = await db.TblOverrides
            .AsNoTracking()
            .AnyAsync(o => o.IsActive || o.IsScheduled);

        _byTenant[tenantId] = (hasOverrides, DateTime.UtcNow);
        return hasOverrides;
    }

    // Override writes are rare; clearing every tenant keeps the invalidation hooks
    // simple in the tenant-blind OverrideService
    public void Invalidate() => _byTenant.Clear();
}
