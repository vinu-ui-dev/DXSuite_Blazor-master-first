namespace DXSuite.Sentinel.Contracts;

/// <summary>
/// Canonical MQTT topic schema for the Sentinel control plane. Topics are namespaced
/// by tenant so the broker can enforce per-tenant isolation via ACLs:
///
///   sentinel/{tenantId}/dev/{deviceId}/state   device → cloud  (retained)
///   sentinel/{tenantId}/dev/{deviceId}/health  device → cloud
///   sentinel/{tenantId}/dev/{deviceId}/lwt      broker → cloud on disconnect (retained)
///   sentinel/{tenantId}/dev/{deviceId}/cmd      cloud  → device
///   sentinel/{tenantId}/gw/{gatewayId}/state    gateway → cloud heartbeat (retained)
///   sentinel/{tenantId}/gw/{gatewayId}/probe    gateway → cloud probe results
///   sentinel/{tenantId}/gw/{gatewayId}/cmd      cloud  → gateway command
///   sentinel/{tenantId}/gw/{gatewayId}/resp     gateway → cloud command response
///   sentinel/{tenantId}/gw/{gatewayId}/lwt      broker → cloud on disconnect (retained)
/// </summary>
public static class SentinelTopics
{
    public const string Root = "sentinel";

    // ── Device topics (publishers fill in concrete ids) ──
    public static string DeviceState(string tenantId, string deviceId) => $"{Root}/{tenantId}/dev/{deviceId}/state";
    public static string DeviceHealth(string tenantId, string deviceId) => $"{Root}/{tenantId}/dev/{deviceId}/health";
    public static string DeviceLwt(string tenantId, string deviceId) => $"{Root}/{tenantId}/dev/{deviceId}/lwt";
    public static string DeviceCommand(string tenantId, string deviceId) => $"{Root}/{tenantId}/dev/{deviceId}/cmd";

    // ── Gateway topics ──
    public static string GatewayState(string tenantId, string gatewayId) => $"{Root}/{tenantId}/gw/{gatewayId}/state";
    public static string GatewayProbe(string tenantId, string gatewayId) => $"{Root}/{tenantId}/gw/{gatewayId}/probe";
    public static string GatewayCommand(string tenantId, string gatewayId) => $"{Root}/{tenantId}/gw/{gatewayId}/cmd";
    public static string GatewayResponse(string tenantId, string gatewayId) => $"{Root}/{tenantId}/gw/{gatewayId}/resp";
    public static string GatewayLwt(string tenantId, string gatewayId) => $"{Root}/{tenantId}/gw/{gatewayId}/lwt";

    // ── Cloud-side subscription filters (across all tenants) ──
    public const string AllDeviceState = Root + "/+/dev/+/state";
    public const string AllDeviceHealth = Root + "/+/dev/+/health";
    public const string AllDeviceLwt = Root + "/+/dev/+/lwt";
    public const string AllGatewayState = Root + "/+/gw/+/state";
    public const string AllGatewayProbe = Root + "/+/gw/+/probe";
    public const string AllGatewayResponse = Root + "/+/gw/+/resp";
    public const string AllGatewayLwt = Root + "/+/gw/+/lwt";

    /// <summary>
    /// Extracts the {tenantId} and trailing {entityId} from a concrete Sentinel topic.
    /// Returns false for topics that don't fit the sentinel/{tenant}/{dev|gw}/{id}/... shape.
    /// </summary>
    public static bool TryParse(string topic, out string tenantId, out string entityType, out string entityId, out string leaf)
    {
        tenantId = entityType = entityId = leaf = string.Empty;
        var parts = topic.Split('/');
        if (parts.Length < 5 || parts[0] != Root) return false;
        tenantId = parts[1];
        entityType = parts[2]; // "dev" or "gw"
        entityId = parts[3];
        leaf = parts[4];
        return true;
    }
}
