using DXSuite.Director.Entities;
using DXSuite.Director.Services.Messaging;

namespace DXSuite.Director.Tests;

// Clause ids refer to docs/spec/gtaa-fsd-1.1.md.

/// <summary>The one label each Message status shows with; a rejected new Message is stored as Draft (MSG-13).</summary>
public sealed class MessageStatusTextTests
{
    [Theory]
    [InlineData(MessageStatus.Draft, "Declined", "dx-badge-red")]
    [InlineData(MessageStatus.Pending, "Pending approval", "dx-badge-yellow")]
    [InlineData(MessageStatus.Approved, "Approved", "dx-badge-green")]
    [InlineData(MessageStatus.Retired, "Retired", "dx-badge-gray")]
    public void Each_status_has_one_label_and_badge(MessageStatus status, string label, string badge)
    {
        Assert.Equal((label, badge), (MessageStatusText.Label(status), MessageStatusText.Badge(status)));
    }
}
