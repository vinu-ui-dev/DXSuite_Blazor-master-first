namespace DXSuite.Shared.Services;

/// <summary>
/// Notification service interface for cross-module notification delivery.
/// Full implementation lives in DXSuite.Director.
/// </summary>
public interface INotificationService
{
    Task CreateNotificationAsync(int userId, string title, string message, string type, int? referenceId = null, string? referenceUrl = null);
    Task CreateBulkNotificationsAsync(List<int> userIds, string title, string message, string type, int? referenceId = null, string? referenceUrl = null);
    Task<int> GetUnreadCountAsync(int userId);
    Task MarkAsReadAsync(int notificationId, int userId);
    Task MarkAllAsReadAsync(int userId);
    Task<List<NotificationDto>> GetRecentNotificationsAsync(int userId, int count = 20);
}
