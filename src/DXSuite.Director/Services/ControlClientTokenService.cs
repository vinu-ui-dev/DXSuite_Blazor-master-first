using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.Security;
using System.Security.Cryptography;

namespace DXSuite.Director.Services;

public class ControlClientTokenService : IControlClientTokenService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ControlClientTokenService> _logger;

    private const string ControlClientPermissions = "controlclient:access";

    public ControlClientTokenService(IServiceScopeFactory scopeFactory, ILogger<ControlClientTokenService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ControlClientTokenValidationResult> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ControlClientTokenValidationResult
            {
                IsValid = false,
                InvalidReason = "Control client token is empty"
            };
        }

        var rawToken = token.StartsWith("cc_") ? token[3..] : token;
        var hash = GatewayApiKeyHelper.ComputeApiKeyHash(rawToken);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var ccToken = await db.TblControlClientTokens
            .Include(t => t.ControlClient)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (ccToken == null)
        {
            return new ControlClientTokenValidationResult
            {
                IsValid = false,
                InvalidReason = "Control client token not found"
            };
        }

        if (!ccToken.IsActive)
        {
            return new ControlClientTokenValidationResult
            {
                IsValid = false,
                InvalidReason = "Control client token has been revoked"
            };
        }

        if (!ccToken.ControlClient.IsActive)
        {
            return new ControlClientTokenValidationResult
            {
                IsValid = false,
                InvalidReason = "Control client profile is disabled"
            };
        }

        _ = Task.Run(() => RecordUsageAsync(ccToken.ControlClientTokenId));

        return new ControlClientTokenValidationResult
        {
            IsValid = true,
            ControlClientTokenId = ccToken.ControlClientTokenId,
            ControlClientId = ccToken.ControlClientId,
            LocationId = ccToken.ControlClient.LocationId,
            Permissions = ControlClientPermissions
        };
    }

    public async Task<ControlClientTokenCreationResult> CreateTokenAsync(int controlClientId)
    {
        var plaintextRaw = GenerateSecureToken();
        var plaintextToken = $"cc_{plaintextRaw}";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var token = new TblControlClientToken
        {
            ControlClientId = controlClientId,
            TokenHash = GatewayApiKeyHelper.ComputeApiKeyHash(plaintextRaw),
            TokenPrefix = plaintextToken[..Math.Min(11, plaintextToken.Length)],
            IsActive = true,
            CreateDate = DateTime.UtcNow
        };

        db.TblControlClientTokens.Add(token);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created control client token (ID: {TokenId}) for client {ClientId}",
            token.ControlClientTokenId, controlClientId);

        return new ControlClientTokenCreationResult(token, plaintextToken);
    }

    public async Task<List<TblControlClientToken>> GetTokensForClientAsync(int controlClientId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        return await db.TblControlClientTokens
            .Where(t => t.ControlClientId == controlClientId)
            .OrderByDescending(t => t.CreateDate)
            .ToListAsync();
    }

    public async Task RevokeTokenAsync(int tokenId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var token = await db.TblControlClientTokens.FindAsync(tokenId);
        if (token == null)
            throw new ArgumentException($"Control client token {tokenId} not found");

        token.IsActive = false;
        await db.SaveChangesAsync();

        _logger.LogInformation("Revoked control client token (ID: {TokenId})", tokenId);
    }

    public async Task DeleteTokenAsync(int tokenId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var token = await db.TblControlClientTokens.FindAsync(tokenId);
        if (token == null)
            throw new ArgumentException($"Control client token {tokenId} not found");

        db.TblControlClientTokens.Remove(token);
        await db.SaveChangesAsync();

        _logger.LogInformation("Deleted control client token (ID: {TokenId})", tokenId);
    }

    public async Task RecordUsageAsync(int tokenId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

            var token = await db.TblControlClientTokens.FindAsync(tokenId);
            if (token != null)
            {
                token.LastUsedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record control client token usage for ID {TokenId}", tokenId);
        }
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
