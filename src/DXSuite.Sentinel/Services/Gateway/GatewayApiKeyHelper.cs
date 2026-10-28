using System.Security.Cryptography;

namespace DXSuite.Sentinel.Services.Gateway;

/// <summary>
/// Generates and hashes API keys for Site Gateway authentication.
/// Keys are shown once at creation time; only the SHA-256 hash is stored.
/// </summary>
public static class GatewayApiKeyHelper
{
    /// <summary>
    /// Generates a cryptographically random API key (48 bytes -> base64url, ~64 chars).
    /// </summary>
    public static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Computes a SHA-256 hash of the API key for storage.
    /// </summary>
    public static string ComputeApiKeyHash(string apiKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(apiKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
