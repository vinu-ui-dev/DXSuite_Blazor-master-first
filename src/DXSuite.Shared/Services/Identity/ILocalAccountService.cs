using DXSuite.Shared.Entities.Identity;

namespace DXSuite.Shared.Services.Identity;

public interface ILocalAccountService
{
    Task<TblUser> CreateAccountAsync(string email, string password);
    Task<TblUser?> ValidateCredentialsAsync(string email, string password);
    Task ChangePasswordAsync(int userId, string currentPassword, string newPassword);
    Task ResetPasswordAsync(int userId, string newPassword);
    Task<bool> IsLockedOutAsync(string email);
    Task<bool> EmailExistsAsync(string email);
    Task<(string token, TblUser user)> CreateAccountWithConfirmationAsync(string email, string password);
    Task<bool> ConfirmEmailAsync(string email, string token);
    Task SetEmailConfirmedAsync(int userId, bool confirmed);
}
