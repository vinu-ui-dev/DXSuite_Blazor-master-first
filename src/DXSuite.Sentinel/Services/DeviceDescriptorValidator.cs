namespace DXSuite.Sentinel.Services;

/// <summary>
/// Validates device descriptors against the required schema rules.
/// Every descriptor must have at least one monitor point with severity,
/// every command must have a risk level, and communication must reference a registered protocol.
/// </summary>
public class DeviceDescriptorValidator
{
    private static readonly HashSet<string> ValidProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "tcp-rs232", "qrc", "http-heartbeat", "snmp", "icmp", "mdc", "http-api"
    };

    private static readonly HashSet<string> ValidRiskLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "low", "medium", "high"
    };

    private static readonly HashSet<string> ValidSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "warning", "critical"
    };

    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "commercial-display", "media-player", "av-processor", "generic-ip"
    };

    public ValidationResult Validate(DeviceDescriptorSchema descriptor)
    {
        var errors = new List<string>();

        ValidateRequiredFields(descriptor, errors);
        ValidateCommunication(descriptor, errors);
        ValidateMonitorPoints(descriptor, errors);
        ValidateCommands(descriptor, errors);

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private void ValidateRequiredFields(DeviceDescriptorSchema descriptor, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(descriptor.DeviceType))
            errors.Add("deviceType is required");
        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
            errors.Add("displayName is required");
        if (string.IsNullOrWhiteSpace(descriptor.Category))
            errors.Add("category is required");
        if (!string.IsNullOrWhiteSpace(descriptor.Category) && !ValidCategories.Contains(descriptor.Category))
            errors.Add($"category must be one of: {string.Join(", ", ValidCategories)}");
    }

    private void ValidateCommunication(DeviceDescriptorSchema descriptor, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Communication.Protocol))
            errors.Add("communication.protocol is required");
        else if (!ValidProtocols.Contains(descriptor.Communication.Protocol))
            errors.Add($"communication.protocol '{descriptor.Communication.Protocol}' is not a registered protocol driver. Valid protocols: {string.Join(", ", ValidProtocols)}");

        if (descriptor.Communication.HeartbeatMethod == null)
            errors.Add("communication.heartbeatMethod is required");
    }

    private void ValidateMonitorPoints(DeviceDescriptorSchema descriptor, List<string> errors)
    {
        if (descriptor.MonitorPoints.Count == 0)
        {
            errors.Add("At least one monitorPoint is required");
            return;
        }

        var hasMonitorWithSeverity = false;
        for (int i = 0; i < descriptor.MonitorPoints.Count; i++)
        {
            var mp = descriptor.MonitorPoints[i];
            if (string.IsNullOrWhiteSpace(mp.Id))
                errors.Add($"monitorPoints[{i}].id is required");
            if (string.IsNullOrWhiteSpace(mp.Name))
                errors.Add($"monitorPoints[{i}].name is required");

            if (!string.IsNullOrWhiteSpace(mp.Severity))
            {
                if (!ValidSeverities.Contains(mp.Severity))
                    errors.Add($"monitorPoints[{i}].severity must be one of: {string.Join(", ", ValidSeverities)}");
                else
                    hasMonitorWithSeverity = true;
            }

            if (mp.Thresholds?.Warning != null || mp.Thresholds?.Critical != null)
                hasMonitorWithSeverity = true;
        }

        if (!hasMonitorWithSeverity)
            errors.Add("At least one monitorPoint must define a severity or thresholds");
    }

    private void ValidateCommands(DeviceDescriptorSchema descriptor, List<string> errors)
    {
        for (int i = 0; i < descriptor.Commands.Count; i++)
        {
            var cmd = descriptor.Commands[i];
            if (string.IsNullOrWhiteSpace(cmd.Id))
                errors.Add($"commands[{i}].id is required");
            if (string.IsNullOrWhiteSpace(cmd.RiskLevel))
                errors.Add($"commands[{i}].riskLevel is required");
            else if (!ValidRiskLevels.Contains(cmd.RiskLevel))
                errors.Add($"commands[{i}].riskLevel must be one of: {string.Join(", ", ValidRiskLevels)}");
        }
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
