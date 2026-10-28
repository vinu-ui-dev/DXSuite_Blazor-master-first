namespace DXSuite.Director.Services;

/// <summary>
/// Defines API key permission scopes for granular endpoint access control.
/// Stored as space-delimited strings in TblApiKey.Permissions.
/// The wildcard "*" grants access to all scopes for backward compatibility with existing keys.
/// SignageHeartbeat and SignageUpload are device-only scopes assigned automatically
/// via the pairing flow and are not available for manual API key creation.
/// </summary>
public static class ApiKeyScopes
{
    public const string SignageHeartbeat = "signage:heartbeat";
    public const string SignageRead = "signage:read";
    public const string SignageUpload = "signage:upload";
    public const string OverrideRead = "override:read";
    public const string OverrideTrigger = "override:trigger";
    public const string TokenValidate = "token:validate";
    public const string ControlClient = "controlclient:access";

    // Publishing introduces new code to the fleet; deploying chooses who receives code that
    // already exists. A customer staging their own rollout should not need the right to introduce
    // a binary, and that is the common case.
    public const string UpdatePublish = "update:publish";
    public const string UpdateDeploy = "update:deploy";

    // Messaging (GTAA FSD 1.1 MSG-19, INK-03)
    public const string MessagingSend = "messaging:send";
    public const string MessagingRead = "messaging:read";

    public const string Wildcard = "*";

    /// <summary>
    /// Scopes available for manual API key creation in the admin UI.
    /// Device-only scopes (signage:heartbeat, signage:upload) are excluded
    /// because devices now obtain credentials via the pairing flow.
    /// </summary>
    public static readonly string[] UserSelectableScopes =
    {
        SignageRead,
        OverrideRead,
        OverrideTrigger,
        TokenValidate,
        UpdatePublish,
        UpdateDeploy,
        MessagingSend,
        MessagingRead
    };

    /// <summary>
    /// Checks whether the permissions string contains the required scope.
    /// Returns true if permissions is "*" (wildcard) or contains the exact scope.
    /// </summary>
    public static bool HasScope(string? permissions, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(permissions)) return false;
        if (permissions.Trim() == Wildcard) return true;
        return permissions.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
    }
}
