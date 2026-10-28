using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Shared.Services;

namespace DXSuite.Director.Services;

/// <summary>
/// Sends email via SMTP using configuration stored in TblSystemSetting.
/// Uses System.Net.Mail -- no external packages required.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly SmtpCredentialProtector _smtpProtector;

    public SmtpEmailService(IServiceScopeFactory scopeFactory, ILogger<SmtpEmailService> logger,
        SmtpCredentialProtector smtpProtector)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _smtpProtector = smtpProtector;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var settings = await LoadSettingsAsync();
        return settings.TryGetValue("smtp:enabled", out var enabled)
               && enabled?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
               && settings.ContainsKey("smtp:host")
               && settings.ContainsKey("smtp:from-address");
    }

    public async Task SendAsync(string toAddress, string subject, string htmlBody)
    {
        var settings = await LoadSettingsAsync();

        if (!settings.TryGetValue("smtp:enabled", out var enabled)
            || !enabled.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var host = settings.GetValueOrDefault("smtp:host", "");
        var portStr = settings.GetValueOrDefault("smtp:port", "587");
        var useSslStr = settings.GetValueOrDefault("smtp:use-ssl", "true");
        var username = settings.GetValueOrDefault("smtp:username", "");
        var password = _smtpProtector.Decrypt(settings.GetValueOrDefault("smtp:password", "")) ?? "";
        var fromAddress = settings.GetValueOrDefault("smtp:from-address", "");
        var fromName = settings.GetValueOrDefault("smtp:from-name", "Director");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(fromAddress))
        {
            _logger.LogWarning("SMTP not fully configured -- skipping email to {To}", toAddress);
            return;
        }

        int.TryParse(portStr, out var port);
        if (port == 0) port = 587;

        bool.TryParse(useSslStr, out var useSsl);

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = useSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrEmpty(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            var message = new MailMessage
            {
                From = new MailAddress(fromAddress, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toAddress);

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent to {To}: {Subject}", toAddress, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}: {Subject}", toAddress, subject);
        }
    }

    private async Task<Dictionary<string, string>> LoadSettingsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblSystemSettings
            .AsNoTracking()
            .Where(s => s.SettingKey.StartsWith("smtp:"))
            .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue ?? "");
    }
}
