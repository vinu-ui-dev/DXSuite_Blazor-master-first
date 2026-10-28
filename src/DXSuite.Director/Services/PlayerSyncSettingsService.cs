using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DXSuite.Director.Entities;
using DXSuite.Shared.Multitenancy;

namespace DXSuite.Director.Services;

public class PlayerSyncSettings
{
    public const int MinPollSeconds = 10;
    public const int MaxPollSeconds = 600;
    public const int DefaultPollSeconds = 30;

    /// <summary>Fleet-wide player poll cadence, clamped to [Min, Max] on read.</summary>
    public int PollIntervalSeconds { get; set; } = DefaultPollSeconds;

    /// <summary>Minutes between full device-inventory heartbeats; 0 sends the full payload every beat.</summary>
    public int FullInventoryMinutes { get; set; } = 15;

    /// <summary>
    /// Device offline threshold derived from the poll cadence (4 missed polls plus grace),
    /// floored at the historical 5 minutes so the default cadence keeps today's behavior.
    /// A slower fleet cadence must never mark healthy devices offline or trigger auto-heal.
    /// </summary>
    public TimeSpan OnlineThreshold => TimeSpan.FromSeconds(Math.Max(300, PollIntervalSeconds * 4 + 60));
}

public interface IPlayerSyncSettingsService
{
    Task<PlayerSyncSettings> GetAsync();
    Task SaveAsync(PlayerSyncSettings settings, string? editedBy = null);
}

/// <summary>
/// Player sync cadence settings backed by TblSystemSettings, cached briefly per tenant
/// because the poll interval is read on every player heartbeat.
/// </summary>
public class PlayerSyncSettingsService : IPlayerSyncSettingsService
{
    private const string PollIntervalKey = "engage:pollIntervalSeconds";
    private const string FullInventoryKey = "engage:fullInventoryMinutes";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IDirectorDBContext _db;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenantContext;

    public PlayerSyncSettingsService(IDirectorDBContext db, IMemoryCache cache, ITenantContext tenantContext)
    {
        _db = db;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    private string CacheKey => $"playersync:{_tenantContext.TenantId}";

    public async Task<PlayerSyncSettings> GetAsync()
    {
        if (_cache.TryGetValue(CacheKey, out PlayerSyncSettings? cached) && cached != null)
            return cached;

        var rows = await _db.TblSystemSettings
            .AsNoTracking()
            .Where(s => s.SettingKey == PollIntervalKey || s.SettingKey == FullInventoryKey)
            .ToListAsync();

        var settings = new PlayerSyncSettings();
        if (int.TryParse(rows.FirstOrDefault(r => r.SettingKey == PollIntervalKey)?.SettingValue, out var poll))
            settings.PollIntervalSeconds = Math.Clamp(poll, PlayerSyncSettings.MinPollSeconds, PlayerSyncSettings.MaxPollSeconds);
        if (int.TryParse(rows.FirstOrDefault(r => r.SettingKey == FullInventoryKey)?.SettingValue, out var full))
            settings.FullInventoryMinutes = Math.Max(0, full);

        _cache.Set(CacheKey, settings, CacheTtl);
        return settings;
    }

    public async Task SaveAsync(PlayerSyncSettings settings, string? editedBy = null)
    {
        settings.PollIntervalSeconds = Math.Clamp(settings.PollIntervalSeconds,
            PlayerSyncSettings.MinPollSeconds, PlayerSyncSettings.MaxPollSeconds);
        settings.FullInventoryMinutes = Math.Max(0, settings.FullInventoryMinutes);

        await UpsertAsync(PollIntervalKey, settings.PollIntervalSeconds.ToString(), editedBy);
        await UpsertAsync(FullInventoryKey, settings.FullInventoryMinutes.ToString(), editedBy);
        await _db.SaveChangesAsync();

        _cache.Set(CacheKey, settings, CacheTtl);
    }

    private async Task UpsertAsync(string key, string value, string? editedBy)
    {
        var row = await _db.TblSystemSettings.FirstOrDefaultAsync(s => s.SettingKey == key);
        if (row == null)
        {
            row = new TblSystemSetting { SettingKey = key };
            _db.TblSystemSettings.Add(row);
        }
        row.SettingValue = value;
        row.LastEditDate = DateTime.UtcNow;
        row.LastEditUser = editedBy;
    }
}
