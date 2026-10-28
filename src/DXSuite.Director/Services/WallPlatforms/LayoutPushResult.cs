namespace DXSuite.Director.Services.WallPlatforms;

public enum LayoutPushStatus
{
    Success,
    NotImplemented,
    Error
}

public class LayoutPushResult
{
    public LayoutPushStatus Status { get; init; }
    public string? ErrorMessage { get; init; }

    public static LayoutPushResult Success() => new() { Status = LayoutPushStatus.Success };
    public static LayoutPushResult NotImplemented() => new() { Status = LayoutPushStatus.NotImplemented };
    public static LayoutPushResult Error(string message) => new() { Status = LayoutPushStatus.Error, ErrorMessage = message };
}
