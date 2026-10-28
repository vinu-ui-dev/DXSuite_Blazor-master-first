namespace DXSuite.Director.Entities;

public enum MessageDisplayMode : byte
{
    FullScreen = 1,
    Overlay = 2,
    Ticker = 3
}

public enum MessageStatus : byte
{
    /// <summary>
    /// A new Message its approval chain rejected: it cannot be sent, and its author's next save submits it again. Shown
    /// as Declined (<c>MessageStatusText</c>).
    /// </summary>
    Draft = 0,
    Approved = 1,
    Retired = 2,

    /// <summary>A new canned Message waiting on its type's approval chain; it cannot be sent until Approved (MSG-12).</summary>
    Pending = 3
}

public enum MessageActivationSource : byte
{
    DirectorAdmin = 1,
    Cupps = 2,
    Api = 3
}

/// <summary>Why a shown version exists (AUD-06): the send itself, or the approval of a revision that replaced it.</summary>
public enum ShownVersionReason : byte
{
    FirstSend = 1,
    Revision = 2
}
