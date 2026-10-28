namespace DXSuite.Shared.Services;

/// <summary>
/// Singleton event bus for real-time notification delivery across modules.
/// Blazor Server components subscribe to OnNotificationsChanged and call InvokeAsync(StateHasChanged).
/// </summary>
public class NotificationBroadcastService
{
    public event Action<int>? OnNotificationsChanged;

    public void NotifyUser(int userId)
    {
        OnNotificationsChanged?.Invoke(userId);
    }

    public void NotifyUsers(IEnumerable<int> userIds)
    {
        foreach (var userId in userIds)
        {
            OnNotificationsChanged?.Invoke(userId);
        }
    }
}
