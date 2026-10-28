using DXSuite.Director.Entities;
using DXSuite.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// Delivers monitoring alerts to admins' notification bells. One notification
/// per transition — dedup is upstream in the monitoring state machine.
/// </summary>
public class NotificationAlertSink : IDirectorAlertSink
{
    private readonly IServiceScopeFactory _scopeFactory;

    public NotificationAlertSink(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task PublishAsync(DirectorAlert alert, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var adminUserIds = await db.TblUsers
            .AsNoTracking()
            .Where(u => u.Role.RoleName == "Admin")
            .Select(u => u.UserId)
            .ToListAsync(ct);
        if (adminUserIds.Count == 0) return;

        var title = alert.Kind switch
        {
            DirectorAlertKind.UrlMissing           => $"Monitored URL '{alert.Source}' stopped polling",
            DirectorAlertKind.UrlRecovered         => $"Monitored URL '{alert.Source}' recovered",
            DirectorAlertKind.DeviceProbeFailed    => $"Device '{alert.DeviceName ?? alert.Source}' is unreachable",
            DirectorAlertKind.DeviceProbeRecovered => $"Device '{alert.DeviceName ?? alert.Source}' recovered",
            _ => alert.Message,
        };

        await notifications.CreateBulkNotificationsAsync(
            adminUserIds, title, alert.Message, "monitoring-alert",
            referenceUrl: "/configuration");
    }
}
