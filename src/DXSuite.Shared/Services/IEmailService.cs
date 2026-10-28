namespace DXSuite.Shared.Services;

/// <summary>
/// Email service interface for cross-module email delivery.
/// Full implementation lives in DXSuite.Director.
/// </summary>
public interface IEmailService
{
    Task SendAsync(string toAddress, string subject, string htmlBody);

    /// <summary>Multi-recipient send with a plain-text alternative, reporting whether it actually went out rather
    /// than only logging a failure (Messaging's approval emails and reminder digests need to tell the two apart).</summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);

    Task<bool> IsConfiguredAsync();
}

public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string Html, string Text);

public sealed record EmailSendResult(bool Succeeded, bool NotConfigured, string? Error)
{
    public static EmailSendResult Ok { get; } = new(true, false, null);
    public static EmailSendResult NotConfiguredResult { get; } = new(false, true, null);
    public static EmailSendResult Failed(string error) => new(false, false, error);
}
