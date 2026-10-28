namespace DXSuite.Shared.Services;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Title { get; set; } = null!;
    public string? Message { get; set; }
    public string NotificationType { get; set; } = null!;
    public int? ReferenceId { get; set; }
    public string? ReferenceUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedDate { get; set; }
}
