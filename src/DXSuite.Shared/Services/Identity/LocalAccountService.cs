using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Models;

namespace DXSuite.Shared.Services.Identity;

public class LocalAccountService : ILocalAccountService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalAccountConfig _config;
    private readonly ILogger<LocalAccountService> _logger;

    // PBKDF2 parameters matching ASP.NET Core Identity defaults
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 600_000;
    private static readonly KeyDerivationPrf Prf = KeyDerivationPrf.HMACSHA256;

    public LocalAccountService(
        IServiceScopeFactory scopeFactory,
        IOptions<AuthenticationConfig> authConfig,
        ILogger<LocalAccountService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = authConfig.Value.LocalAccounts;
        _logger = logger;
    }

    public async Task<TblUser> CreateAccountAsync(string email, string password)
    {
        ValidatePassword(password);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var normalizedEmail = email.Trim().ToUpperInvariant();

        if (await db.TblLocalAccounts.AnyAsync(a => a.NormalizedEmail == normalizedEmail))
            throw new InvalidOperationException("An account with this email already exists.");

        // Create the TblUser record
        var nameParts = email.Split('@')[0].Split('.');
        var user = new TblUser
        {
            UserPrincipalName = email.Trim(),
            FriendlyName = nameParts.Length >= 2
                ? $"{Capitalize(nameParts[0])} {Capitalize(nameParts[1])}"
                : Capitalize(nameParts[0]),
            GivenName = Capitalize(nameParts[0]),
            Surname = nameParts.Length >= 2 ? Capitalize(nameParts[1]) : null,
            RoleId = (int)DXSuite.Shared.Authorization.Roles.Role.NewUser,
            CreateDate = DateTime.UtcNow,
        };
        db.TblUsers.Add(user);
        await db.SaveChangesAsync();

        // Create the local account linked to the user
        var account = new TblLocalAccount
        {
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = HashPassword(password),
            EmailConfirmed = !_config.RequireEmailConfirmation,
            UserId = user.UserId,
            CreateDate = DateTime.UtcNow,
        };
        db.TblLocalAccounts.Add(account);
        await db.SaveChangesAsync();

        _logger.LogInformation("Local account created for {Email} (UserId={UserId})", email, user.UserId);
        return user;
    }

    public async Task<TblUser?> ValidateCredentialsAsync(string email, string password)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var account = await db.TblLocalAccounts
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail);

        if (account == null)
            return null;

        // Check lockout
        if (account.LockoutEnd.HasValue && account.LockoutEnd > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Login attempt for locked account {Email}", email);
            return null;
        }

        // Check email confirmation
        if (_config.RequireEmailConfirmation && !account.EmailConfirmed)
        {
            _logger.LogWarning("Login attempt for unconfirmed account {Email}", email);
            throw new InvalidOperationException("email-not-confirmed");
        }

        if (!VerifyPassword(password, account.PasswordHash))
        {
            // Record failed attempt
            account.AccessFailedCount++;
            if (account.AccessFailedCount >= _config.LockoutMaxAttempts)
            {
                account.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(_config.LockoutMinutes);
                _logger.LogWarning("Account {Email} locked out after {Count} failed attempts", email, account.AccessFailedCount);
            }
            await db.SaveChangesAsync();
            return null;
        }

        // Successful login — reset lockout
        account.AccessFailedCount = 0;
        account.LockoutEnd = null;
        account.LastLoginDate = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return account.User;
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var account = await db.TblLocalAccounts.FirstOrDefaultAsync(a => a.UserId == userId)
            ?? throw new InvalidOperationException("No local account found for this user.");

        if (!VerifyPassword(currentPassword, account.PasswordHash))
            throw new InvalidOperationException("Current password is incorrect.");

        ValidatePassword(newPassword);
        account.PasswordHash = HashPassword(newPassword);
        await db.SaveChangesAsync();
    }

    public async Task ResetPasswordAsync(int userId, string newPassword)
    {
        ValidatePassword(newPassword);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var account = await db.TblLocalAccounts.FirstOrDefaultAsync(a => a.UserId == userId)
            ?? throw new InvalidOperationException("No local account found for this user.");

        account.PasswordHash = HashPassword(newPassword);
        account.AccessFailedCount = 0;
        account.LockoutEnd = null;
        await db.SaveChangesAsync();
    }

    public async Task<bool> IsLockedOutAsync(string email)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var account = await db.TblLocalAccounts.FirstOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail);

        return account?.LockoutEnd > DateTimeOffset.UtcNow;
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var normalizedEmail = email.Trim().ToUpperInvariant();
        return await db.TblLocalAccounts.AnyAsync(a => a.NormalizedEmail == normalizedEmail);
    }

    public async Task<(string token, TblUser user)> CreateAccountWithConfirmationAsync(string email, string password)
    {
        ValidatePassword(password);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var normalizedEmail = email.Trim().ToUpperInvariant();

        if (await db.TblLocalAccounts.AnyAsync(a => a.NormalizedEmail == normalizedEmail))
            throw new InvalidOperationException("An account with this email already exists.");

        var nameParts = email.Split('@')[0].Split('.');
        var user = new TblUser
        {
            UserPrincipalName = email.Trim(),
            FriendlyName = nameParts.Length >= 2
                ? $"{Capitalize(nameParts[0])} {Capitalize(nameParts[1])}"
                : Capitalize(nameParts[0]),
            GivenName = Capitalize(nameParts[0]),
            Surname = nameParts.Length >= 2 ? Capitalize(nameParts[1]) : null,
            RoleId = (int)DXSuite.Shared.Authorization.Roles.Role.NewUser,
            CreateDate = DateTime.UtcNow,
        };
        db.TblUsers.Add(user);
        await db.SaveChangesAsync();

        var token = Guid.NewGuid().ToString("N");
        var account = new TblLocalAccount
        {
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            PasswordHash = HashPassword(password),
            EmailConfirmed = false,
            UserId = user.UserId,
            CreateDate = DateTime.UtcNow,
            ConfirmationToken = token,
            ConfirmationTokenExpiry = DateTime.UtcNow.AddHours(24),
        };
        db.TblLocalAccounts.Add(account);
        await db.SaveChangesAsync();

        _logger.LogInformation("Local account created with confirmation pending for {Email} (UserId={UserId})", email, user.UserId);
        return (token, user);
    }

    public async Task<bool> ConfirmEmailAsync(string email, string token)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var account = await db.TblLocalAccounts.FirstOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail);

        if (account == null) return false;
        if (account.EmailConfirmed) return true;
        if (account.ConfirmationToken != token) return false;
        if (account.ConfirmationTokenExpiry < DateTime.UtcNow) return false;

        account.EmailConfirmed = true;
        account.ConfirmationToken = null;
        account.ConfirmationTokenExpiry = null;
        await db.SaveChangesAsync();

        _logger.LogInformation("Email confirmed for {Email}", email);
        return true;
    }

    public async Task SetEmailConfirmedAsync(int userId, bool confirmed)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var account = await db.TblLocalAccounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (account == null) return;

        account.EmailConfirmed = confirmed;
        if (confirmed)
        {
            account.ConfirmationToken = null;
            account.ConfirmationTokenExpiry = null;
        }
        await db.SaveChangesAsync();
    }

    private void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < _config.MinPasswordLength)
            throw new ArgumentException($"Password must be at least {_config.MinPasswordLength} characters.");
        if (_config.RequireUppercase && !password.Any(char.IsUpper))
            throw new ArgumentException("Password must contain at least one uppercase letter.");
        if (_config.RequireDigit && !password.Any(char.IsDigit))
            throw new ArgumentException("Password must contain at least one digit.");
        if (_config.RequireSpecialChar && password.All(c => char.IsLetterOrDigit(c)))
            throw new ArgumentException("Password must contain at least one special character.");
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = KeyDerivation.Pbkdf2(password, salt, Prf, Iterations, HashSize);

        // Store as: iterations.salt.hash (all base64)
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 3) return false;

        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);

        var actualHash = KeyDerivation.Pbkdf2(password, salt, Prf, iterations, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
