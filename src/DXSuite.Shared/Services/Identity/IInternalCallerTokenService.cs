using Microsoft.AspNetCore.DataProtection;

namespace DXSuite.Shared.Services.Identity;

/// <summary>
/// Proves a Blazor Server circuit's already-authenticated identity to an internal, same-process
/// server-to-server HTTP call (e.g. CuppsLiveDataProvider calling back into this app's own Director
/// API) without relying on the browser's auth cookie, which isn't reliably available to
/// IHttpContextAccessor once an interactive circuit is past its initial render.
///
/// The token is minted fresh per outbound call from the circuit's own ISessionState — already proven
/// authentic by the original sign-in — and is short-lived and signed with this app's Data Protection
/// key ring (the same key material backing auth cookies and antiforgery tokens), so it can't be forged
/// or replayed meaningfully by an external caller who only has network access to the loopback endpoint.
/// </summary>
public interface IInternalCallerTokenService
{
    /// <summary>Mints a short-lived token asserting <paramref name="username"/> (a TblUser.UserPrincipalName)
    /// is the caller. Callers must have already verified this is the real signed-in user (e.g. via
    /// ISessionState), since this service does no identity verification itself — it only signs the claim.</summary>
    string IssueToken(string username);

    /// <summary>Validates a token minted by <see cref="IssueToken"/>. Returns the asserted username, or null
    /// if the token is missing, malformed, tampered with, or expired.</summary>
    string? ValidateToken(string? token);
}

internal sealed class InternalCallerTokenService : IInternalCallerTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);
    private const string Purpose = "DXSuite.InternalCallerToken.v1";

    private readonly IDataProtector _protector;

    public InternalCallerTokenService(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string IssueToken(string username)
    {
        var expiresUtc = DateTime.UtcNow.Add(TokenLifetime);
        var payload = $"{username}|{expiresUtc.Ticks}";
        return _protector.Protect(payload);
    }

    public string? ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        string payload;
        try
        {
            payload = _protector.Unprotect(token);
        }
        catch
        {
            // Tampered, wrong purpose, or signed by a since-rotated key — never trust it.
            return null;
        }

        var parts = payload.Split('|', 2);
        if (parts.Length != 2) return null;
        if (!long.TryParse(parts[1], out var expiresTicks)) return null;
        if (DateTime.UtcNow.Ticks > expiresTicks) return null;

        var username = parts[0];
        return string.IsNullOrWhiteSpace(username) ? null : username;
    }
}
