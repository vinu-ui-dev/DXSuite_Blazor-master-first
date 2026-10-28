using System.Security.Cryptography;
using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services;

/// <summary>
/// The tenant-scoped path token that gates <c>/MediaAssets</c>.
///
/// Stable per tenant, and that is a cost accepted rather than an oversight: a player caches assets
/// by URL, so a rotating token would re-download the whole library every time it turned over. A
/// leaked manifest therefore leaks that tenant's content library, which is strictly better than
/// today, where the whole tree is anonymous to anyone who can reach the host.
///
/// A path token rather than a header, because there is no per-flavour switch to throw:
/// <c>/MediaAssets</c> is one static-file pipeline serving one path to every flavour. Header auth
/// would break every applet player until they shipped a release, spending exactly the
/// zero-applet-releases the flavour work bought. The player fetches whatever URL the manifest
/// hands it, so the server can authenticate both flavours with no client change at all.
/// </summary>
public interface IMediaAccessTokenService
{
    /// <summary>The current token for this tenant, minted on first use.</summary>
    Task<string> GetOrCreateAsync();

    /// <summary>True when the presented token is this tenant's. False for anything else.</summary>
    Task<bool> IsValidAsync(string? token);
}

public sealed class MediaAccessTokenService : IMediaAccessTokenService
{
    /// <summary>
    /// The marker segment that separates a tokenised URL from a legacy one. A literal rather than
    /// a bare leading segment, because a location id is also a bare leading segment.
    /// </summary>
    public const string PathMarker = "t";

    private const string SettingKey = "engage:mediaAccessToken";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaAccessTokenService> _logger;

    public MediaAccessTokenService(IServiceScopeFactory scopeFactory, ILogger<MediaAccessTokenService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string> GetOrCreateAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var setting = await db.TblSystemSettings.FindAsync(SettingKey);
        if (!string.IsNullOrWhiteSpace(setting?.SettingValue))
            return setting.SettingValue;

        var token = Generate();

        if (setting == null)
        {
            db.TblSystemSettings.Add(new TblSystemSetting
            {
                SettingKey = SettingKey,
                SettingValue = token,
                LastEditDate = DateTime.UtcNow,
                LastEditUser = "system"
            });
        }
        else
        {
            setting.SettingValue = token;
            setting.LastEditDate = DateTime.UtcNow;
            setting.LastEditUser = "system";
        }

        await db.SaveChangesAsync();

        // Stored rather than derived from a server key, so rotating it is deleting one row and
        // does not depend on a key whose rotation would silently invalidate every tenant at once.
        _logger.LogInformation("Minted a media access token for this tenant");
        return token;
    }

    public async Task<bool> IsValidAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var stored = await db.TblSystemSettings
            .AsNoTracking()
            .Where(s => s.SettingKey == SettingKey)
            .Select(s => s.SettingValue)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(stored))
            return false;

        // Fixed-time, because this is a secret compared on every asset request.
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(stored),
            System.Text.Encoding.UTF8.GetBytes(token));
    }

    private static string Generate()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
}
