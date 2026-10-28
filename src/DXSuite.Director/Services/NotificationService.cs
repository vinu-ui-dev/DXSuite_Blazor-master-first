using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Services;

namespace DXSuite.Director.Services;

public class AppNotificationService : IDirectorNotificationService
{
    private readonly IDirectorDBContext _db;
    private readonly NotificationBroadcastService _broadcast;
    private readonly ILogger<AppNotificationService> _logger;

    public AppNotificationService(
        IDirectorDBContext db,
        NotificationBroadcastService broadcast,
        ILogger<AppNotificationService> logger)
    {
        _db = db;
        _broadcast = broadcast;
        _logger = logger;
    }

    public async Task CreateNotificationAsync(int userId, string title, string message, string type,
        int? referenceId = null, string? referenceUrl = null)
    {
        _db.TblNotifications.Add(new TblNotification
        {
            UserId = userId,
            Title = title,
            Message = message,
            NotificationType = type,
            ReferenceId = referenceId,
            ReferenceUrl = referenceUrl,
            IsRead = false,
            EmailSent = false,
            CreatedDate = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        _broadcast.NotifyUser(userId);
    }

    public async Task CreateBulkNotificationsAsync(List<int> userIds, string title, string message, string type,
        int? referenceId = null, string? referenceUrl = null)
    {
        if (userIds.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var userId in userIds.Distinct())
        {
            _db.TblNotifications.Add(new TblNotification
            {
                UserId = userId,
                Title = title,
                Message = message,
                NotificationType = type,
                ReferenceId = referenceId,
                ReferenceUrl = referenceUrl,
                IsRead = false,
                EmailSent = false,
                CreatedDate = now
            });
        }
        await _db.SaveChangesAsync();

        _broadcast.NotifyUsers(userIds.Distinct());
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        return await _db.TblNotifications
            .CountAsync(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<List<NotificationDto>> GetRecentNotificationsAsync(int userId, int count = 20)
    {
        return await _db.TblNotifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedDate)
            .Take(count)
            .Select(n => new NotificationDto
            {
                NotificationId = n.NotificationId,
                Title = n.Title,
                Message = n.Message,
                NotificationType = n.NotificationType,
                ReferenceId = n.ReferenceId,
                ReferenceUrl = n.ReferenceUrl,
                IsRead = n.IsRead,
                CreatedDate = n.CreatedDate
            })
            .ToListAsync();
    }

    public async Task MarkAsReadAsync(int notificationId, int userId)
    {
        var notification = await _db.TblNotifications
            .FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.UserId == userId);

        if (notification != null && !notification.IsRead)
        {
            notification.IsRead = true;
            await _db.SaveChangesAsync();
            _broadcast.NotifyUser(userId);
        }
    }

    public async Task MarkAllAsReadAsync(int userId)
    {
        var unread = await _db.TblNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        if (unread.Count > 0)
        {
            foreach (var n in unread)
                n.IsRead = true;

            await _db.SaveChangesAsync();
            _broadcast.NotifyUser(userId);
        }
    }
}
