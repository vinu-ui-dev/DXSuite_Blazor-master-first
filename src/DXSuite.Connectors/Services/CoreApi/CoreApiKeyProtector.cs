using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace DXSuite.Connectors.Services.CoreApi;

/// <summary>
/// Protects the tenant's Director key for CoreAPI at rest with ASP.NET Core Data Protection (HOST-04). The
/// column was created protected, so a stored value without the "ENC:" prefix is corruption, never legacy
/// plaintext, and is not handed back as a key.
/// </summary>
public class CoreApiKeyProtector
{
    public const string UnreadableMessage = "The CoreAPI key was saved on another server and cannot be read here. Enter it again.";

    private const string EncryptedPrefix = "ENC:";

    private readonly IDataProtector _protector;

    public CoreApiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("CoreApiDirectorKey");
    }

    public string Protect(string directorKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directorKey);
        return EncryptedPrefix + _protector.Protect(directorKey);
    }

    /// <returns>False for a value that is not protected, or that does not unprotect on this host's key ring.</returns>
    public bool TryUnprotect(string? stored, out string? directorKey)
    {
        directorKey = null;
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal)) return false;

        try
        {
            directorKey = _protector.Unprotect(stored[EncryptedPrefix.Length..]);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
