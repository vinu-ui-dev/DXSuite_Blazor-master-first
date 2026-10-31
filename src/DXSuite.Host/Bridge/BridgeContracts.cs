namespace DXSuite.Host.Bridge;

/// <summary>
/// Wire-format mirrors of RadiantCoreWebApi.Applications.EditorAuth's BridgeContracts. DXSuite and
/// Director are separate solutions, so these are duplicated by shape (property names/casing must
/// match) rather than shared via a reference.
/// </summary>
public sealed record RedeemRequest(string Code, string CodeVerifier);
public sealed record SessionRequest(string SessionHandle, bool Activity = false);

public sealed record BridgeIdentity(int Version, string Subject, string UserName, string DisplayName,
    string CustomerId, string TenantId, string RoleName, string[] Permissions, string[] Operations,
    string SessionHandle, DateTimeOffset ExpiresAt, DateTimeOffset IdleExpiresAt);
