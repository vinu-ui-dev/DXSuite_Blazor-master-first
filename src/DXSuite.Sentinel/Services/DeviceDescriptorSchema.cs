using System.Text.Json.Serialization;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Root schema for a device descriptor. Declares what a device type can do -
/// its communication protocol, monitoring points, available commands, and health rules.
/// </summary>
public class DeviceDescriptorSchema
{
    [JsonPropertyName("descriptorVersion")]
    public string DescriptorVersion { get; set; } = "1.0";

    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("communication")]
    public CommunicationConfig Communication { get; set; } = new();

    [JsonPropertyName("monitorPoints")]
    public List<MonitorPointDefinition> MonitorPoints { get; set; } = new();

    [JsonPropertyName("commands")]
    public List<CommandDefinition> Commands { get; set; } = new();

    [JsonPropertyName("healthRules")]
    public List<HealthRuleDefinition> HealthRules { get; set; } = new();
}

public class CommunicationConfig
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [JsonPropertyName("defaultPort")]
    public int? DefaultPort { get; set; }

    [JsonPropertyName("authentication")]
    public AuthenticationConfig? Authentication { get; set; }

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("heartbeatMethod")]
    public HeartbeatMethodConfig HeartbeatMethod { get; set; } = new();
}

public class AuthenticationConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "none";

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public class HeartbeatMethodConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "poll";

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("intervalSeconds")]
    public int IntervalSeconds { get; set; } = 30;
}

public class MonitorPointDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("dataType")]
    public string DataType { get; set; } = "string";

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("retrieveCommand")]
    public string? RetrieveCommand { get; set; }

    [JsonPropertyName("retrieveParams")]
    public Dictionary<string, object>? RetrieveParams { get; set; }

    [JsonPropertyName("responsePath")]
    public string? ResponsePath { get; set; }

    [JsonPropertyName("expectedState")]
    public ConditionExpression? ExpectedState { get; set; }

    [JsonPropertyName("thresholds")]
    public ThresholdConfig? Thresholds { get; set; }

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class ConditionExpression
{
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public class ThresholdConfig
{
    [JsonPropertyName("warning")]
    public ConditionExpression? Warning { get; set; }

    [JsonPropertyName("critical")]
    public ConditionExpression? Critical { get; set; }
}

public class CommandDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("riskLevel")]
    public string RiskLevel { get; set; } = "low";

    [JsonPropertyName("requiresApproval")]
    public bool RequiresApproval { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<CommandParameterDefinition>? Parameters { get; set; }

    [JsonPropertyName("timeoutMs")]
    public int? TimeoutMs { get; set; }
}

public class CommandParameterDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("default")]
    public object? Default { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class HealthRuleDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("condition")]
    public HealthRuleCondition Condition { get; set; } = new();

    [JsonPropertyName("suggestedActions")]
    public List<string> SuggestedActions { get; set; } = new();

    [JsonPropertyName("escalationTier")]
    public int EscalationTier { get; set; } = 3;

    [JsonPropertyName("cooldownMinutes")]
    public int? CooldownMinutes { get; set; }
}

public class HealthRuleCondition
{
    [JsonPropertyName("monitorPoint")]
    public string MonitorPoint { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("durationSeconds")]
    public int? DurationSeconds { get; set; }
}
