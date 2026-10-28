namespace DXSuite.Shared.Services;

/// <summary>
/// Email service interface for cross-module email delivery.
/// Full implementation lives in DXSuite.Director.
/// </summary>
public interface IEmailService
{
    Task SendAsync(string toAddress, string subject, string htmlBody);
    Task<bool> IsConfiguredAsync();
}
