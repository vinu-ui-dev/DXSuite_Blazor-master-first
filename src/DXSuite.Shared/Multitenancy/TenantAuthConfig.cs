using DXSuite.Shared.Models;

namespace DXSuite.Shared.Multitenancy;

public class TenantAuthConfig
{
    /// <summary>
    /// "microsoft", "google", "okta", "local", or "multi" (all configured providers).
    /// "multi" is the default for backward-compatible dev/on-prem setups where per-tenant
    /// providers are absent and the top-level Authentication:Providers config is used.
    /// </summary>
    public string Provider { get; init; } = "multi";

    // Legacy single-provider fields kept for backward compatibility.
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Azure AD tenant ID for Microsoft provider, or OIDC authority URL for others.
    /// </summary>
    public string? TenantIdOrAuthority { get; init; }

    /// <summary>
    /// Per-tenant OIDC provider configurations keyed by provider name (e.g. "Microsoft", "Google").
    /// When non-empty, these take precedence over the top-level Authentication:Providers config.
    /// Each entry gets a named OIDC scheme registered at startup as "{subdomain}_{providerKey}".
    /// </summary>
    public Dictionary<string, OidcProviderConfig> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-tenant local account settings. Null means inherit the top-level
    /// Authentication:LocalAccounts config. When set, this takes full precedence —
    /// a tenant can disable local accounts even if the global config enables them.
    /// </summary>
    public LocalAccountConfig? LocalAccounts { get; init; }

    /// <summary>
    /// Per-tenant LDAP / Active Directory config. Null means inherit the top-level
    /// Authentication:Ldap config. When set, completely replaces the global LDAP config
    /// for this tenant. Stored in TblTenant.AuthConfigJson and reloaded every 5 minutes
    /// by SqlTenantStore — no restart required to enable or reconfigure LDAP.
    /// </summary>
    public LdapProviderConfig? Ldap { get; init; }
}
