using Microsoft.AspNetCore.DataProtection;

namespace DXSuite.Connectors.Services.Llm;

/// <summary>
/// Encrypts and decrypts LLM provider API keys using ASP.NET Core Data Protection.
/// Legacy plaintext keys are auto-detected via an "ENC:" prefix on encrypted values.
/// </summary>
public class LlmApiKeyProtector
{
    private readonly IDataProtector _protector;
    private const string EncryptedPrefix = "ENC:";

    public LlmApiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("LlmApiKey");
    }

    public string? Encrypt(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return apiKey;
        return EncryptedPrefix + _protector.Protect(apiKey);
    }

    public string? Decrypt(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return apiKey;

        if (!apiKey.StartsWith(EncryptedPrefix))
            return apiKey;

        try
        {
            return _protector.Unprotect(apiKey[EncryptedPrefix.Length..]);
        }
        catch
        {
            return null;
        }
    }
}
