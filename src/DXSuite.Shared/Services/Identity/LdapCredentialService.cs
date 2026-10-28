#pragma warning disable CA1416 // System.DirectoryServices.Protocols is unsupported on browser — this service only runs server-side
using System.DirectoryServices.Protocols;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Entities;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Shared.Models;

namespace DXSuite.Shared.Services.Identity;

public class LdapCredentialService : ILdapCredentialService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LdapCredentialService> _logger;

    public LdapCredentialService(IServiceScopeFactory scopeFactory, ILogger<LdapCredentialService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<TblUser?> ValidateAsync(string username, string password, LdapProviderConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Server))
            throw new InvalidOperationException("LDAP Server is not configured.");
        if (string.IsNullOrWhiteSpace(config.BaseDn))
            throw new InvalidOperationException("LDAP BaseDn is not configured.");

        AuthType authType = ResolveAuthType(config);
        bool useServiceAccount = !string.IsNullOrWhiteSpace(config.ServiceAccountDn);

        // A simple bind carrying a name but an empty password is an "unauthenticated bind"
        // (RFC 4513 §5.1.2): the directory reports success but leaves the session anonymous, and every
        // later operation fails with a misleading "a successful bind must be completed" error.
        if (useServiceAccount && string.IsNullOrEmpty(config.ServiceAccountPassword))
            throw new InvalidOperationException(
                "LDAP ServiceAccountDn is set but ServiceAccountPassword is empty. Supply the password, " +
                "or clear ServiceAccountDn to bind anonymously on purpose.");

        if (useServiceAccount && authType != AuthType.Basic && LooksLikeDn(config.ServiceAccountDn))
            throw new InvalidOperationException(
                $"LDAP AuthMethod '{config.AuthMethod}' cannot bind with the distinguished name " +
                $"'{config.ServiceAccountDn}'. Use a UPN (user@domain.com) or DOMAIN\\user, " +
                "or set AuthMethod to 'Basic'.");

        username = username.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        // Reject LDAP filter injection characters (RFC 4515 §3 special chars).
        if (ContainsLdapSpecialChars(username))
        {
            _logger.LogWarning("LDAP login rejected: username contains illegal filter characters");
            return null;
        }

        bool isUpn = username.Contains('@');
        string filter = isUpn
            ? string.Format(config.UpnFilter, username)
            : string.Format(config.SamAccountNameFilter, username);

        // Phase 1: service-account bind + user attribute search
        string? userDn, mailAttr, displayNameAttr, givenNameAttr, snAttr, samAttr, upnAttr;
        try
        {
            var identifier = new LdapDirectoryIdentifier(config.Server, config.Port, true, false);
            var serviceCredential = useServiceAccount
                ? new NetworkCredential(config.ServiceAccountDn, config.ServiceAccountPassword)
                : null;

            using var searchConn = new LdapConnection(identifier, serviceCredential)
            {
                AuthType = useServiceAccount ? authType : AuthType.Anonymous,
                Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
            };
            searchConn.SessionOptions.ProtocolVersion = 3;
            if (config.UseSsl)
            {
                searchConn.SessionOptions.SecureSocketLayer = true;
                // Certificate validation intentionally permissive here; production deployments
                // should install the AD CA into the OS trust store instead of relying on this callback.
                searchConn.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }

            searchConn.Bind();

            var searchRequest = new SearchRequest(
                config.BaseDn,
                filter,
                SearchScope.Subtree,
                "distinguishedName", "mail", "displayName", "givenName", "sn", "sAMAccountName", "userPrincipalName");

            var response = (SearchResponse)searchConn.SendRequest(searchRequest);
            if (response.Entries.Count == 0)
            {
                _logger.LogInformation("LDAP: no directory entry for username '{Username}'", username);
                return null;
            }

            var entry = response.Entries[0];
            userDn          = entry.DistinguishedName;
            mailAttr        = GetAttr(entry, "mail");
            displayNameAttr = GetAttr(entry, "displayName");
            givenNameAttr   = GetAttr(entry, "givenName");
            snAttr          = GetAttr(entry, "sn");
            samAttr         = GetAttr(entry, "sAMAccountName");
            upnAttr         = GetAttr(entry, "userPrincipalName");
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            // Code 49 on the service bind means the service account credentials are wrong — misconfig.
            throw new InvalidOperationException(
                "LDAP service account credentials are invalid. Check LdapProviderConfig.ServiceAccountDn.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LDAP search failed for server {Server}:{Port}", config.Server, config.Port);
            return null;
        }

        // Phase 2: validate the user's password by binding as them.
        // Only a simple bind accepts the distinguished name; Kerberos/NTLM need the UPN, falling back
        // to sAMAccountName qualified with the BaseDn's DNS domain.
        string? bindIdentity = authType == AuthType.Basic
            ? userDn
            : ResolveNegotiateIdentity(upnAttr, samAttr, config.BaseDn);

        if (string.IsNullOrWhiteSpace(bindIdentity))
        {
            _logger.LogWarning(
                "LDAP: user '{UserDn}' exposes no userPrincipalName or sAMAccountName usable for an " +
                "AuthMethod '{AuthMethod}' bind; login denied", userDn, config.AuthMethod);
            return null;
        }

        try
        {
            var identifier = new LdapDirectoryIdentifier(config.Server, config.Port, true, false);
            using var userConn = new LdapConnection(identifier, new NetworkCredential(bindIdentity, password))
            {
                AuthType = authType,
                Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
            };
            userConn.SessionOptions.ProtocolVersion = 3;
            if (config.UseSsl)
            {
                userConn.SessionOptions.SecureSocketLayer = true;
                userConn.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }

            userConn.Bind();
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            _logger.LogInformation("LDAP: invalid credentials for DN '{UserDn}'", userDn);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LDAP user bind failed for DN '{UserDn}'", userDn);
            return null;
        }

        // Phase 3: resolve a usable email address for TblUser.UserPrincipalName
        string? email = mailAttr;

        if (string.IsNullOrWhiteSpace(email))
        {
            if (!string.IsNullOrWhiteSpace(config.EmailDomainSuffix) && !string.IsNullOrWhiteSpace(samAttr))
                email = $"{samAttr.Trim()}@{config.EmailDomainSuffix.TrimStart('@')}";
            else if (isUpn)
                email = username; // caller supplied a UPN; use it directly
            else
            {
                _logger.LogWarning(
                    "LDAP: user '{UserDn}' has no mail attribute and EmailDomainSuffix is not configured; login denied",
                    userDn);
                return null;
            }
        }

        // Phase 4: find or create TblUser, matching the pattern in SessionState.LoadUserAsync
        string normalizedEmail = email.Trim().ToLowerInvariant();
        string givenName   = !string.IsNullOrWhiteSpace(givenNameAttr) ? givenNameAttr.Trim() : DeriveGivenName(displayNameAttr, normalizedEmail);
        string surname     = !string.IsNullOrWhiteSpace(snAttr)        ? snAttr.Trim()         : DeriveSurname(displayNameAttr, normalizedEmail);
        string friendlyName = !string.IsNullOrWhiteSpace(displayNameAttr) ? displayNameAttr.Trim() : $"{givenName} {surname}".Trim();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDbContext>();

        var user = await db.TblUsers.FirstOrDefaultAsync(u => u.UserPrincipalName == normalizedEmail);
        if (user == null)
        {
            user = new TblUser
            {
                UserPrincipalName = normalizedEmail,
                GivenName         = givenName,
                Surname           = surname,
                FriendlyName      = friendlyName,
                RoleId            = (int)Roles.Role.NewUser,
            };
            db.TblUsers.Add(user);
            try
            {
                await db.SaveChangesAsync();
                _logger.LogInformation("LDAP: auto-created TblUser for {Email} (UserId={UserId})", normalizedEmail, user.UserId);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation())
            {
                // A concurrent login created the row between our existence check and the insert.
                // Removing the Added entity detaches it so a later save doesn't retry the insert.
                db.TblUsers.Remove(user);
                user = await db.TblUsers.FirstAsync(u => u.UserPrincipalName == normalizedEmail);
            }
        }
        else
        {
            bool changed = false;
            if (string.IsNullOrEmpty(user.GivenName)    && !string.IsNullOrEmpty(givenName))    { user.GivenName    = givenName;    changed = true; }
            if (string.IsNullOrEmpty(user.Surname)       && !string.IsNullOrEmpty(surname))      { user.Surname      = surname;      changed = true; }
            if (string.IsNullOrEmpty(user.FriendlyName)  && !string.IsNullOrEmpty(friendlyName)) { user.FriendlyName = friendlyName; changed = true; }
            if (changed)
            {
                db.TblUsers.Update(user);
                await db.SaveChangesAsync();
            }
        }

        return user;
    }

    private static AuthType ResolveAuthType(LdapProviderConfig config)
        => config.AuthMethod?.Trim().ToLowerInvariant() switch
        {
            null or "" or "negotiate" => AuthType.Negotiate,
            "basic" or "simple"       => AuthType.Basic,
            _ => throw new InvalidOperationException(
                $"LDAP AuthMethod '{config.AuthMethod}' is not supported. Use 'Negotiate' or 'Basic'."),
        };

    private static bool LooksLikeDn(string value)
        => value.Contains("CN=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("OU=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("DC=", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Identity for a Kerberos/NTLM bind: the directory's UPN when present, otherwise the
    /// sAMAccountName qualified with the BaseDn's DNS domain. Null when neither is available.
    /// </summary>
    private static string? ResolveNegotiateIdentity(string? upn, string? sam, string baseDn)
    {
        if (!string.IsNullOrWhiteSpace(upn)) return upn.Trim();
        if (string.IsNullOrWhiteSpace(sam)) return null;

        string domain = DeriveDnsDomain(baseDn);
        return string.IsNullOrEmpty(domain) ? null : $"{sam.Trim()}@{domain}";
    }

    /// <summary>Builds "SLCIA.COM" from "DC=SLCIA,DC=COM".</summary>
    private static string DeriveDnsDomain(string baseDn)
        => string.Join('.', baseDn
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]));

    private static string? GetAttr(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name)) return null;
        var vals = entry.Attributes[name].GetValues(typeof(string));
        return vals.Length > 0 ? vals[0] as string : null;
    }

    private static bool ContainsLdapSpecialChars(string input)
        => input.IndexOfAny(['\\', '*', '(', ')', '\0']) >= 0;

    private static string DeriveGivenName(string? displayName, string email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim().Split(' ', 2)[0];
        var local = email.Split('@')[0].Split('.');
        return Capitalize(local[0]);
    }

    private static string DeriveSurname(string? displayName, string email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var parts = displayName.Trim().Split(' ', 2);
            return parts.Length >= 2 ? parts[1] : "";
        }
        var local = email.Split('@')[0].Split('.');
        return local.Length >= 2 ? Capitalize(local[1]) : "";
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}
