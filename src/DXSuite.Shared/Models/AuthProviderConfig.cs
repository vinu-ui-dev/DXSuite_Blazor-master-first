namespace DXSuite.Shared.Models;

/// <summary>
/// Top-level authentication configuration. Supports multiple OIDC providers,
/// local email/password accounts, and a developer demo mode.
/// </summary>
public class AuthenticationConfig
{
    /// <summary>
    /// OIDC providers keyed by name (e.g. "Microsoft", "Google", "Okta").
    /// </summary>
    public Dictionary<string, OidcProviderConfig> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Local email/password account settings.
    /// </summary>
    public LocalAccountConfig LocalAccounts { get; set; } = new();

    /// <summary>
    /// When true, bypasses all auth and logs in as DemoUsername.
    /// </summary>
    public bool DemoMode { get; set; }

    /// <summary>
    /// Username for demo mode.
    /// </summary>
    public string DemoUsername { get; set; } = "dev@local";

    /// <summary>
    /// Legacy compatibility: maps old "AuthenticationType" to the new model.
    /// </summary>
    public string? AuthenticationType { get; set; }

    public LdapProviderConfig? Ldap { get; set; }
}

public class OidcProviderConfig
{
    public bool Enabled { get; set; }
    public string DisplayName { get; set; } = "";
    public string Icon { get; set; } = "bi-box-arrow-in-right";

    // Microsoft-specific (Instance + TenantId)
    public string? Instance { get; set; }
    public string? TenantId { get; set; }
    public string? Domain { get; set; }

    // Generic OIDC (Authority URL)
    public string? Authority { get; set; }

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>
    /// OIDC response_mode. Null keeps the handler default of "form_post". Set to "query" for
    /// providers that reject form_post — Duo SSO advertises only query and fragment in its
    /// discovery document.
    /// </summary>
    public string? ResponseMode { get; set; }
}

public class LocalAccountConfig
{
    public bool Enabled { get; set; }
    public bool RequireEmailConfirmation { get; set; }
    public bool AllowSelfRegistration { get; set; }
    public int MinPasswordLength { get; set; } = 12;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireDigit { get; set; } = true;
    public bool RequireSpecialChar { get; set; } = true;
    public int LockoutMaxAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public class LdapProviderConfig
{
    public bool Enabled { get; set; }
    public string DisplayName { get; set; } = "Sign in with Directory";

    public string Server { get; set; } = "";
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public int TimeoutSeconds { get; set; } = 10;

    public string BaseDn { get; set; } = "";

    /// <summary>
    /// Bind mechanism: "Negotiate" (Kerberos/NTLM) or "Basic" (LDAP simple bind).
    /// Negotiate is the default because a directory may authenticate a simple bind and still reject
    /// every operation on that connection. Kerberos/NTLM identify principals by UPN or DOMAIN\user,
    /// so <see cref="ServiceAccountDn"/> cannot be a distinguished name unless this is "Basic".
    /// </summary>
    public string AuthMethod { get; set; } = "Negotiate";

    /// <summary>Filter for sAMAccountName lookups. {0} is replaced with the sanitized username.</summary>
    public string SamAccountNameFilter { get; set; } = "(&(objectClass=user)(sAMAccountName={0}))";

    /// <summary>Filter for UPN lookups (input contains @). {0} is replaced with the sanitized UPN.</summary>
    public string UpnFilter { get; set; } = "(&(objectClass=user)(userPrincipalName={0}))";

    public string ServiceAccountDn { get; set; } = "";
    public string ServiceAccountPassword { get; set; } = "";

    /// <summary>
    /// Appended to sAMAccountName when the LDAP 'mail' attribute is absent.
    /// e.g. "corp.example.com" produces "jsmith@corp.example.com".
    /// Leave empty to deny login for users without a mail attribute.
    /// </summary>
    public string EmailDomainSuffix { get; set; } = "";
}
