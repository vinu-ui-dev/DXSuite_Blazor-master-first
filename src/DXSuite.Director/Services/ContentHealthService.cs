using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Models.BrightSign;

namespace DXSuite.Director.Services;

public class ContentHealthService : IContentHealthService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContentHealthService> _logger;

    public ContentHealthService(
        IServiceScopeFactory scopeFactory,
        ILogger<ContentHealthService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RecordEventAsync(ContentHealthEvent evt, string deviceId)
    {
        if (evt == null || string.IsNullOrEmpty(evt.Status))
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var eventRecord = new TblContentHealthEvent
            {
                DeviceId = deviceId,
                ContentUrl = evt.ContentUrl,
                PreviousStatus = evt.PreviousStatus,
                Status = evt.Status,
                Reason = evt.Reason,
                EventTime = evt.Timestamp.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(evt.Timestamp.Value).DateTime
                    : DateTime.UtcNow,
                ReceivedTime = DateTime.UtcNow
            };

            dbContext.TblContentHealthEvents.Add(eventRecord);
            await dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Content health event recorded: Device {DeviceId}, Status {Status} -> {NewStatus}, Reason: {Reason}",
                deviceId, evt.PreviousStatus, evt.Status, evt.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record content health event for device {DeviceId}", deviceId);
        }
    }

    public async Task UpdateHealthStatusAsync(string deviceId, ContentHealthStatus status)
    {
        if (status == null)
            return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var device = await dbContext.TblDevices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
            if (device != null)
            {
                device.ExtendedData = ExtendedDataHelper.UpdateProperty<DeviceExtendedData>(
                    device.ExtendedData,
                    ext =>
                    {
                        ext.ContentHealth ??= new ContentHealthData();
                        ext.ContentHealth.Status = status.Status;
                        ext.ContentHealth.Url = status.Url;
                        ext.ContentHealth.JsErrors = status.JsErrors;

                        if (status.LastProofOfLife.HasValue)
                        {
                            ext.ContentHealth.LastProof = DateTimeOffset.FromUnixTimeMilliseconds(status.LastProofOfLife.Value).DateTime;
                        }
                    });

                await dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update content health status for device {DeviceId}", deviceId);
        }
    }

    public async Task<List<TblContentHealthEvent>> GetRecentEventsAsync(int count = 100)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await dbContext.TblContentHealthEvents
            .OrderByDescending(e => e.EventTime)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<TblDevice>> GetUnhealthyDevicesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        // Load devices with ExtendedData and filter in memory for content health status
        var devices = await dbContext.TblDevices
            .Where(d => d.ExtendedData != null)
            .ToListAsync();

        return devices
            .Where(d =>
            {
                var ext = ExtendedDataHelper.Parse<DeviceExtendedData>(d.ExtendedData);
                var status = ext?.ContentHealth?.Status;
                return !string.IsNullOrEmpty(status) &&
                       status != "healthy" &&
                       status != "loading";
            })
            .ToList();
    }
}
