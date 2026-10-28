using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public interface IConsoleLoggingService
{
    /// <summary>
    /// Check if console logging is enabled for a device
    /// </summary>
    Task<bool> IsLoggingEnabledAsync(string deviceId);

    /// <summary>
    /// Enable or disable console logging for a device
    /// </summary>
    Task SetLoggingEnabledAsync(string deviceId, bool enabled);

    /// <summary>
    /// Store console log entries from a device
    /// </summary>
    Task StoreLogsAsync(string deviceId, List<ConsoleLogEntry> entries);

    /// <summary>
    /// Get console logging data for a device (settings + entries)
    /// </summary>
    Task<ConsoleLoggingData?> GetLoggingDataAsync(string deviceId);

    /// <summary>
    /// Clear stored logs for a device
    /// </summary>
    Task ClearLogsAsync(string deviceId);
}

public class ConsoleLoggingService : IConsoleLoggingService
{
    private readonly IDbContextFactory<DirectorDbContext> _dbContextFactory;
    private readonly ILogger<ConsoleLoggingService> _logger;

    // Maximum number of log entries to keep per device
    private const int MaxLogEntries = 200;

    public ConsoleLoggingService(
        IDbContextFactory<DirectorDbContext> dbContextFactory,
        ILogger<ConsoleLoggingService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<bool> IsLoggingEnabledAsync(string deviceId)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var device = await dbContext.TblDevices.FindAsync(deviceId);

            if (device == null) return false;

            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(device.ExtendedData);
            return extData?.ConsoleLogging?.Enabled ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check logging status for device {DeviceId}", deviceId);
            return false;
        }
    }

    public async Task SetLoggingEnabledAsync(string deviceId, bool enabled)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var device = await dbContext.TblDevices.FindAsync(deviceId);

            if (device == null)
            {
                _logger.LogWarning("Device {DeviceId} not found when setting logging status", deviceId);
                return;
            }

            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(device.ExtendedData) ?? new DeviceExtendedData();

            extData.ConsoleLogging ??= new ConsoleLoggingData();
            extData.ConsoleLogging.Enabled = enabled;
            extData.ConsoleLogging.ToggledAt = DateTime.UtcNow;

            // Clear logs when disabling to free up space
            if (!enabled)
            {
                extData.ConsoleLogging.Entries = null;
            }

            device.ExtendedData = JsonSerializer.Serialize(extData);
            await dbContext.SaveChangesAsync();

            _logger.LogInformation("Console logging {Status} for device {DeviceId}",
                enabled ? "enabled" : "disabled", deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set logging status for device {DeviceId}", deviceId);
        }
    }

    public async Task StoreLogsAsync(string deviceId, List<ConsoleLogEntry> entries)
    {
        if (entries == null || entries.Count == 0) return;

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var device = await dbContext.TblDevices.FindAsync(deviceId);

            if (device == null) return;

            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(device.ExtendedData) ?? new DeviceExtendedData();

            // Only store if logging is enabled
            if (extData.ConsoleLogging?.Enabled != true) return;

            extData.ConsoleLogging.Entries ??= new List<ConsoleLogEntry>();

            // Add new entries
            extData.ConsoleLogging.Entries.AddRange(entries);

            // Prune to keep only the most recent entries
            if (extData.ConsoleLogging.Entries.Count > MaxLogEntries)
            {
                extData.ConsoleLogging.Entries = extData.ConsoleLogging.Entries
                    .OrderByDescending(e => e.Timestamp)
                    .Take(MaxLogEntries)
                    .ToList();
            }

            device.ExtendedData = JsonSerializer.Serialize(extData);
            await dbContext.SaveChangesAsync();

            _logger.LogDebug("Stored {Count} log entries for device {DeviceId}", entries.Count, deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store logs for device {DeviceId}", deviceId);
        }
    }

    public async Task<ConsoleLoggingData?> GetLoggingDataAsync(string deviceId)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var device = await dbContext.TblDevices.FindAsync(deviceId);

            if (device == null) return null;

            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(device.ExtendedData);
            return extData?.ConsoleLogging;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get logging data for device {DeviceId}", deviceId);
            return null;
        }
    }

    public async Task ClearLogsAsync(string deviceId)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var device = await dbContext.TblDevices.FindAsync(deviceId);

            if (device == null) return;

            var extData = ExtendedDataHelper.Parse<DeviceExtendedData>(device.ExtendedData);

            if (extData?.ConsoleLogging != null)
            {
                extData.ConsoleLogging.Entries = null;
                device.ExtendedData = JsonSerializer.Serialize(extData);
                await dbContext.SaveChangesAsync();

                _logger.LogInformation("Cleared console logs for device {DeviceId}", deviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear logs for device {DeviceId}", deviceId);
        }
    }
}
