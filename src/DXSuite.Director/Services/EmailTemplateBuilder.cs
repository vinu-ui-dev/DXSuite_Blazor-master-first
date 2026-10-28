using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace DXSuite.Director.Services;

/// <summary>
/// Builds branded HTML email templates with CID-embedded header image.
/// Table-based layout with inline styles for cross-client compatibility.
/// </summary>
public static class EmailTemplateBuilder
{
    /// <summary>
    /// Hardcoded path to the email header image relative to wwwroot.
    /// This is a static copy of the website header — changing the website
    /// header will NOT change the email header.
    /// </summary>
    private const string EmailHeaderImageRelativePath = "images/email-header.png";

    private static readonly Dictionary<string, string> ButtonTextMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["approval-pending"]  = "REVIEW APPROVAL",
        ["approval-approved"] = "VIEW APPROVALS",
        ["approval-declined"] = "VIEW APPROVALS",
        ["new-user"]          = "MANAGE USERS",
        ["schedule-expiring"] = "VIEW SCHEDULE",
        ["api-key-expiring"]       = "MANAGE API KEYS",
        ["credential-expiring"]     = "MANAGE CREDENTIALS",
        ["credential-auth-failure"] = "MANAGE CREDENTIALS",
    };

    /// <summary>
    /// Resolves the absolute path to the hardcoded email header image.
    /// </summary>
    public static string? ResolveHeaderImagePath(string webRootPath)
    {
        if (string.IsNullOrEmpty(webRootPath)) return null;
        var path = Path.Combine(webRootPath, EmailHeaderImageRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Builds a branded HTML email body with CID-embedded header image.
    /// </summary>
    /// <param name="title">Email heading text</param>
    /// <param name="message">Body message (plain text or simple HTML)</param>
    /// <param name="notificationType">Notification type key for button text mapping</param>
    /// <param name="actionUrl">Absolute URL for the action button; omitted if null</param>
    /// <param name="headerImagePath">Absolute path to the email header image on disk</param>
    /// <param name="settingsUrl">Absolute URL to settings page for footer link</param>
    public static (string HtmlBody, LinkedResource? HeaderImage) Build(
        string title, string message, string notificationType,
        string? actionUrl, string? headerImagePath, string? settingsUrl)
    {
        LinkedResource? headerImage = null;
        string headerHtml;

        if (!string.IsNullOrEmpty(headerImagePath) && File.Exists(headerImagePath))
        {
            headerImage = new LinkedResource(headerImagePath, MediaTypeNames.Image.Png)
            {
                ContentId = "header-logo",
                TransferEncoding = TransferEncoding.Base64
            };
            headerHtml = @"<tr>
    <td align=""center"" style=""padding: 24px 0 16px 0;"">
        <img src=""cid:header-logo"" alt=""Radiant Director"" style=""max-width: 280px; height: auto;"" />
    </td>
</tr>";
        }
        else
        {
            // Text fallback when the hardcoded header image is missing from wwwroot
            headerHtml = @"<tr>
    <td align=""center"" style=""padding: 24px 0 16px 0;"">
        <span style=""font-family: 'Segoe UI', Arial, sans-serif; font-size: 22px; font-weight: 700; color: #FD8204; letter-spacing: 2px;"">RADIANT DIRECTOR</span>
    </td>
</tr>";
        }

        ButtonTextMap.TryGetValue(notificationType, out var buttonText);

        var buttonHtml = "";
        if (!string.IsNullOrEmpty(actionUrl) && !string.IsNullOrEmpty(buttonText))
        {
            buttonHtml = $@"<tr>
    <td align=""center"" style=""padding: 24px 0 8px 0;"">
        <!--[if mso]>
        <v:roundrect xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:w=""urn:schemas-microsoft-com:office:word"" href=""{EscapeHtml(actionUrl)}"" style=""height:44px;v-text-anchor:middle;width:220px;"" arcsize=""10%"" strokecolor=""#0071BC"" fillcolor=""#0071BC"">
        <w:anchorlock/>
        <center style=""color:#ffffff;font-family:'Segoe UI',Arial,sans-serif;font-size:14px;font-weight:bold;"">{EscapeHtml(buttonText)}</center>
        </v:roundrect>
        <![endif]-->
        <!--[if !mso]><!-->
        <a href=""{EscapeHtml(actionUrl)}"" target=""_blank"" style=""display: inline-block; background-color: #0071BC; color: #ffffff; font-family: 'Segoe UI', Arial, sans-serif; font-size: 14px; font-weight: bold; text-decoration: none; padding: 12px 32px; border-radius: 4px; mso-hide: all;"">{EscapeHtml(buttonText)}</a>
        <!--<![endif]-->
    </td>
</tr>";
        }

        var settingsLinkHtml = !string.IsNullOrEmpty(settingsUrl)
            ? $@"<a href=""{EscapeHtml(settingsUrl)}"" target=""_blank"" style=""color: #999999; text-decoration: underline;"">Settings</a>"
            : "Settings";

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang=""en"" xmlns=""http://www.w3.org/1999/xhtml"" xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:o=""urn:schemas-microsoft-com:office:office"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"" />
    <title>{EscapeHtml(title)}</title>
    <!--[if mso]>
    <noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript>
    <![endif]-->
</head>
<body style=""margin: 0; padding: 0; background-color: #f5f5f5; font-family: 'Segoe UI', Arial, sans-serif;"">
    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background-color: #f5f5f5;"">
        <tr>
            <td align=""center"" style=""padding: 32px 16px;"">
                <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""max-width: 600px; width: 100%; background-color: #ffffff; border-radius: 8px; overflow: hidden;"">
                    <!-- Orange accent bar -->
                    <tr>
                        <td style=""height: 4px; background-color: #FD8204; font-size: 1px; line-height: 1px;"">&nbsp;</td>
                    </tr>
                    <!-- Header -->
                    {headerHtml}
                    <!-- Divider -->
                    <tr>
                        <td style=""padding: 0 32px;"">
                            <hr style=""border: none; border-top: 1px solid #eeeeee; margin: 0;"" />
                        </td>
                    </tr>
                    <!-- Title -->
                    <tr>
                        <td style=""padding: 24px 32px 8px 32px;"">
                            <h2 style=""margin: 0; font-family: 'Segoe UI', Arial, sans-serif; font-size: 20px; font-weight: 600; color: #333333;"">{EscapeHtml(title)}</h2>
                        </td>
                    </tr>
                    <!-- Message -->
                    <tr>
                        <td style=""padding: 8px 32px 16px 32px; font-family: 'Segoe UI', Arial, sans-serif; font-size: 15px; line-height: 1.6; color: #555555;"">
                            {message}
                        </td>
                    </tr>
                    <!-- Action button -->
                    {buttonHtml}
                    <!-- Footer -->
                    <tr>
                        <td style=""padding: 24px 32px;"">
                            <hr style=""border: none; border-top: 1px solid #eeeeee; margin: 0 0 16px 0;"" />
                            <p style=""margin: 0; font-family: 'Segoe UI', Arial, sans-serif; font-size: 12px; color: #999999; text-align: center;"">
                                Automated notification from Radiant Director. Manage preferences in {settingsLinkHtml}.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>");

        return (sb.ToString(), headerImage);
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
