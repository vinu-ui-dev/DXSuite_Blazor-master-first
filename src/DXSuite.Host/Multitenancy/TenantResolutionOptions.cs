namespace DXSuite.Host.Multitenancy;

/// <summary>
/// Bound from the "DXSuite:TenantResolution" configuration section.
/// </summary>
internal sealed class TenantResolutionOptions
{
    /// <summary>
    /// Allows an X-API-KEY request header to replace the default tenant on requests that carry
    /// no subdomain. Off unless a deployment opts in, because the header is attacker-supplied on
    /// anonymous routes and the lookup it triggers queries every configured tenant's Director
    /// database in turn (ConfigTenantStore.FindTenantByApiKeyHashAsync).
    ///
    /// Turning this on is only meaningful where a client authenticates with a tenant API key but
    /// cannot reach the tenant by subdomain. Subdomain requests never consult the header.
    /// </summary>
    public bool AllowApiKeyTenantOverride { get; init; }

    /// <summary>
    /// Allows the Editor bridge's own tenant cookie (BridgeAuthController.BridgeTenantCookie) to
    /// replace the default tenant on requests that carry no subdomain. Unlike the API-key override,
    /// this cookie is set only by BridgeAuthController itself after a server-to-server redeem call
    /// to Director already validated the identity and its TenantId — so it is not attacker-supplied
    /// the way an arbitrary request header would be. Still off unless a deployment opts in.
    /// </summary>
    public bool AllowEditorBridgeTenantOverride { get; init; }
}
