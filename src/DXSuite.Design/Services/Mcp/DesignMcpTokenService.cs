using System.Security.Cryptography;
using System.Text;
using DXSuite.Design.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Design.Services.Mcp;

/// <summary>
/// Issues and validates dxd_-prefixed personal access tokens for the Design MCP endpoint.
/// Tokens are tenant- and user-bound; only their SHA-256 hash is persisted.
/// </summary>
public sealed class DesignMcpTokenService(IDbContextFactory<DesignDbContext> dbFactory, ITenantContext tenant)
{
    public const string TokenPrefix = "dxd_";
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(90);

    /// <summary>LastUsedDate is refreshed at most this often to avoid a write per MCP request.</summary>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    /// <summary>Creates a token for the user and returns the raw value — the only time it is available.</summary>
    public async Task<string> CreateAsync(string userId, string name)
    {
        var raw = TokenPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        await using var db = await dbFactory.CreateDbContextAsync();
        db.TblDesignMcpTokens.Add(new TblDesignMcpToken
        {
            CustomerId = tenant.CustomerCode,
            UserId = userId,
            Name = name,
            TokenHash = Hash(raw),
            CreateDate = DateTime.UtcNow,
            ExpiresDate = DateTime.UtcNow.Add(DefaultLifetime),
        });
        await db.SaveChangesAsync();
        return raw;
    }

    public async Task<List<TblDesignMcpToken>> ListForUserAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TblDesignMcpTokens.AsNoTracking()
            .Where(t => t.CustomerId == tenant.CustomerCode && t.UserId == userId)
            .OrderByDescending(t => t.CreateDate)
            .ToListAsync();
    }

    public async Task RevokeAsync(int tokenId, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var token = await db.TblDesignMcpTokens
            .FirstOrDefaultAsync(t => t.TokenId == tokenId && t.CustomerId == tenant.CustomerCode && t.UserId == userId);
        if (token is not null)
        {
            token.IsRevoked = true;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Returns the token row when the raw value is valid for this tenant, else null.</summary>
    public async Task<TblDesignMcpToken?> ValidateAsync(string rawToken)
    {
        if (!rawToken.StartsWith(TokenPrefix, StringComparison.Ordinal)) return null;

        var hash = Hash(rawToken);
        await using var db = await dbFactory.CreateDbContextAsync();
        var token = await db.TblDesignMcpTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash
                                      && t.CustomerId == tenant.CustomerCode
                                      && !t.IsRevoked
                                      && t.ExpiresDate > DateTime.UtcNow);
        if (token is null) return null;

        if (token.LastUsedDate is null || DateTime.UtcNow - token.LastUsedDate > LastUsedResolution)
        {
            token.LastUsedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return token;
    }

    private static string Hash(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
