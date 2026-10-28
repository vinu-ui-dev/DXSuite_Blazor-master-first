using Microsoft.AspNetCore.DataProtection;

namespace DXSuite.Director.Services;

/// <summary>
/// Encrypts and decrypts SMTP credentials using ASP.NET Core Data Protection,
/// so passwords are not stored as plaintext in the database.
/// Legacy plaintext values are auto-detected via an "ENC:" prefix on encrypted values.
/// </summary>
public class SmtpCredentialProtector
{
    private readonly IDataProtector _protector;
    private const string EncryptedPrefix = "ENC:";

    public SmtpCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("SmtpCredential");
    }

    /// <summary>
    /// Encrypts a credential for storage. Returns null/empty unchanged.
    /// </summary>
    public string? Encrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return EncryptedPrefix + _protector.Protect(value);
    }

    /// <summary>
    /// Decrypts a credential from storage. Transparently handles legacy plaintext values
    /// (anything not prefixed with "ENC:" is returned as-is).
    /// </summary>
    public string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        if (!value.StartsWith(EncryptedPrefix))
        {
            // Legacy plaintext — return as-is
            return value;
        }

        try
        {
            return _protector.Unprotect(value[EncryptedPrefix.Length..]);
        }
        catch
        {
            return null;
        }
    }
}
