using DXSuite.Director.Entities;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The one label and badge each Message status shows with. A new Message its approval chain rejected is stored as
/// <see cref="MessageStatus.Draft"/>, so Draft reads "Declined" (MSG-13).
/// </summary>
public static class MessageStatusText
{
    public static string Label(MessageStatus status) => status switch
    {
        MessageStatus.Draft => "Declined",
        MessageStatus.Approved => "Approved",
        MessageStatus.Retired => "Retired",
        MessageStatus.Pending => "Pending approval",
        _ => status.ToString(),
    };

    /// <summary>The StyleGuide badge class a list shows the status in.</summary>
    public static string Badge(MessageStatus status) => status switch
    {
        MessageStatus.Draft => "dx-badge-red",
        MessageStatus.Pending => "dx-badge-yellow",
        MessageStatus.Approved => "dx-badge-green",
        _ => "dx-badge-gray",
    };
}
