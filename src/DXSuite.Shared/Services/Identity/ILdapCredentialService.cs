using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Models;

namespace DXSuite.Shared.Services.Identity;

public interface ILdapCredentialService
{
    /// <summary>
    /// Validates credentials against the LDAP/Active Directory server configured in
    /// <paramref name="config"/>. On success, finds or auto-creates a TblUser and returns it.
    /// Returns null for invalid credentials or when the user does not exist in the directory.
    /// Throws <see cref="InvalidOperationException"/> for configuration problems (bad service
    /// account, empty Server/BaseDn) so ops can distinguish misconfig from wrong password.
    /// </summary>
    Task<TblUser?> ValidateAsync(string username, string password, LdapProviderConfig config);
}
