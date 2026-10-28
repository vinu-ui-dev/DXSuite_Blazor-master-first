using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

public record ControlClientTokenCreationResult(TblControlClientToken Token, string PlaintextToken);

public class ControlClientTokenValidationResult
{
    public bool IsValid { get; set; }
    public int? ControlClientTokenId { get; set; }
    public int? ControlClientId { get; set; }
    public int? LocationId { get; set; }
    public string? Permissions { get; set; }
    public string? InvalidReason { get; set; }
}

public interface IControlClientTokenService
{
    Task<ControlClientTokenValidationResult> ValidateTokenAsync(string token);

    Task<ControlClientTokenCreationResult> CreateTokenAsync(int controlClientId);

    Task<List<TblControlClientToken>> GetTokensForClientAsync(int controlClientId);

    Task RevokeTokenAsync(int tokenId);

    Task DeleteTokenAsync(int tokenId);

    Task RecordUsageAsync(int tokenId);
}
