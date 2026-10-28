using Microsoft.AspNetCore.DataProtection;

namespace DXSuite.Director.Services;

/// <summary>
/// Encrypts and decrypts the secrets the Engage for BrightSign provisioning surfaces hold: a
/// package's bootstrap DWS password and Wi-Fi PSK, a device's rotated per-device DWS password, and
/// a pairing request's persisted device token.
///
/// A separate protector from <see cref="SmtpCredentialProtector"/> rather than a shared one, so
/// that a purpose string scopes the ciphertext: a value protected for one purpose cannot be
/// unprotected by the other, and revoking one does not reach the other.
/// </summary>
public class ProvisioningSecretProtector
{
    private readonly IDataProtector _protector;
    private const string EncryptedPrefix = "ENC:";

    public ProvisioningSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("EngageBrightSign.ProvisioningSecret");
    }

    public string? Encrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return EncryptedPrefix + _protector.Protect(value);
    }

    /// <summary>
    /// Returns null for a value that will not unprotect. Unlike the SMTP protector there is no
    /// legacy plaintext to tolerate: every column this guards was created protected, so an
    /// unprefixed value is corruption rather than history and must not be handed back as a secret.
    /// </summary>
    public string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.StartsWith(EncryptedPrefix)) return null;

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
