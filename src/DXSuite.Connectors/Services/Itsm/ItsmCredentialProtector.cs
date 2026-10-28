using Microsoft.AspNetCore.DataProtection;

namespace DXSuite.Connectors.Services.Itsm;

public class ItsmCredentialProtector
{
    private readonly IDataProtector _protector;
    private const string EncryptedPrefix = "ENC:";

    public ItsmCredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ItsmCredential");
    }

    public string? Encrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith(EncryptedPrefix)) return value;
        return EncryptedPrefix + _protector.Protect(value);
    }

    public string? Decrypt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.StartsWith(EncryptedPrefix)) return value;
        try { return _protector.Unprotect(value[EncryptedPrefix.Length..]); }
        catch { return null; }
    }
}
