namespace DXSuite.Sentinel.Services;

/// <summary>
/// Provides built-in base descriptor templates for common device categories.
/// These templates serve as starting points when creating new device type descriptors,
/// pre-populated with realistic monitor points, commands, and health rules.
/// </summary>
public static class DescriptorTemplates
{
    /// <summary>
    /// Commercial display template (Samsung MDC, LG, etc.) with RS-232/MDC protocol.
    /// Includes power state, input source, volume, temperature, and lamp hours monitoring.
    /// </summary>
    public static DeviceDescriptorSchema GetCommercialDisplayTemplate()
    {
        return new DeviceDescriptorSchema
        {
            DescriptorVersion = "1.0",
            DeviceType = "commercial-display-base",
            DisplayName = "Commercial Display (Base Template)",
            Category = "commercial-display",
            Communication = new CommunicationConfig
            {
                Protocol = "mdc",
                DefaultPort = 1515,
                ConnectionType = "persistent-tcp",
                HeartbeatMethod = new HeartbeatMethodConfig
                {
                    Type = "poll",
                    Command = "GET_STATUS",
                    IntervalSeconds = 30
                }
            },
            MonitorPoints = new List<MonitorPointDefinition>
            {
                new()
                {
                    Id = "power-state",
                    Name = "Power State",
                    Category = "status",
                    DataType = "string",
                    RetrieveCommand = "GET_POWER",
                    ExpectedState = new ConditionExpression { Operator = "==", Value = "on" },
                    Severity = "critical",
                    Description = "Current power state of the display panel"
                },
                new()
                {
                    Id = "input-source",
                    Name = "Input Source",
                    Category = "status",
                    DataType = "string",
                    RetrieveCommand = "GET_INPUT",
                    Severity = "warning",
                    Description = "Active input source (HDMI1, HDMI2, DP1, etc.)"
                },
                new()
                {
                    Id = "volume-level",
                    Name = "Volume Level",
                    Category = "status",
                    DataType = "integer",
                    Unit = "%",
                    RetrieveCommand = "GET_VOLUME",
                    Description = "Current volume level as a percentage"
                },
                new()
                {
                    Id = "panel-temperature",
                    Name = "Panel Temperature",
                    Category = "health",
                    DataType = "float",
                    Unit = "C",
                    RetrieveCommand = "GET_TEMPERATURE",
                    Thresholds = new ThresholdConfig
                    {
                        Warning = new ConditionExpression { Operator = ">", Value = 55.0 },
                        Critical = new ConditionExpression { Operator = ">", Value = 70.0 }
                    },
                    Severity = "warning",
                    Description = "Internal panel temperature in Celsius"
                },
                new()
                {
                    Id = "lamp-hours",
                    Name = "Lamp / Backlight Hours",
                    Category = "health",
                    DataType = "integer",
                    Unit = "hours",
                    RetrieveCommand = "GET_LAMP_HOURS",
                    Thresholds = new ThresholdConfig
                    {
                        Warning = new ConditionExpression { Operator = ">", Value = 40000 },
                        Critical = new ConditionExpression { Operator = ">", Value = 50000 }
                    },
                    Description = "Total lamp or backlight operating hours"
                },
                new()
                {
                    Id = "signal-present",
                    Name = "Signal Present",
                    Category = "health",
                    DataType = "boolean",
                    RetrieveCommand = "GET_SIGNAL_STATUS",
                    ExpectedState = new ConditionExpression { Operator = "==", Value = true },
                    Severity = "warning",
                    Description = "Whether a valid video signal is detected on the active input"
                }
            },
            Commands = new List<CommandDefinition>
            {
                new()
                {
                    Id = "power-on",
                    Name = "Power On",
                    Category = "control",
                    RiskLevel = "low",
                    Command = "SET_POWER ON",
                    TimeoutMs = 5000
                },
                new()
                {
                    Id = "power-off",
                    Name = "Power Off",
                    Category = "control",
                    RiskLevel = "medium",
                    RequiresApproval = true,
                    Command = "SET_POWER OFF",
                    TimeoutMs = 5000
                },
                new()
                {
                    Id = "set-input",
                    Name = "Set Input Source",
                    Category = "control",
                    RiskLevel = "low",
                    Command = "SET_INPUT",
                    Parameters = new List<CommandParameterDefinition>
                    {
                        new()
                        {
                            Name = "source",
                            Type = "string",
                            Required = true,
                            Description = "Input source identifier (HDMI1, HDMI2, DP1, DVI)"
                        }
                    },
                    TimeoutMs = 3000
                },
                new()
                {
                    Id = "set-volume",
                    Name = "Set Volume",
                    Category = "control",
                    RiskLevel = "low",
                    Command = "SET_VOLUME",
                    Parameters = new List<CommandParameterDefinition>
                    {
                        new()
                        {
                            Name = "level",
                            Type = "integer",
                            Required = true,
                            Description = "Volume level (0-100)"
                        }
                    },
                    TimeoutMs = 2000
                },
                new()
                {
                    Id = "mute-toggle",
                    Name = "Toggle Mute",
                    Category = "control",
                    RiskLevel = "low",
                    Command = "SET_MUTE TOGGLE",
                    TimeoutMs = 2000
                },
                new()
                {
                    Id = "panel-restart",
                    Name = "Restart Panel",
                    Category = "maintenance",
                    RiskLevel = "high",
                    RequiresApproval = true,
                    Command = "RESTART",
                    TimeoutMs = 30000
                }
            },
            HealthRules = new List<HealthRuleDefinition>
            {
                new()
                {
                    Name = "Display powered off unexpectedly",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "power-state",
                        State = "critical",
                        DurationSeconds = 60
                    },
                    SuggestedActions = new List<string>
                    {
                        "Send power-on command",
                        "Check physical power connection",
                        "Verify schedule - display may be in scheduled off period"
                    },
                    EscalationTier = 2,
                    CooldownMinutes = 10
                },
                new()
                {
                    Name = "Panel overheating",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "panel-temperature",
                        State = "critical",
                        DurationSeconds = 120
                    },
                    SuggestedActions = new List<string>
                    {
                        "Reduce backlight brightness",
                        "Check HVAC in display location",
                        "Power off display to prevent damage"
                    },
                    EscalationTier = 1,
                    CooldownMinutes = 30
                },
                new()
                {
                    Name = "No signal on active input",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "signal-present",
                        State = "warning",
                        DurationSeconds = 90
                    },
                    SuggestedActions = new List<string>
                    {
                        "Check media player or source device",
                        "Verify HDMI/DP cable connection",
                        "Try switching to alternate input"
                    },
                    EscalationTier = 2,
                    CooldownMinutes = 15
                }
            }
        };
    }

    /// <summary>
    /// Media player template (BrightSign, ChromeOS, custom Linux) with HTTP heartbeat protocol.
    /// Includes system resource monitoring, content health, and screenshot capture.
    /// </summary>
    public static DeviceDescriptorSchema GetMediaPlayerTemplate()
    {
        return new DeviceDescriptorSchema
        {
            DescriptorVersion = "1.0",
            DeviceType = "media-player-base",
            DisplayName = "Media Player (Base Template)",
            Category = "media-player",
            Communication = new CommunicationConfig
            {
                Protocol = "http-heartbeat",
                DefaultPort = 8080,
                Authentication = new AuthenticationConfig
                {
                    Type = "api-key",
                    Required = true
                },
                ConnectionType = "http-poll",
                HeartbeatMethod = new HeartbeatMethodConfig
                {
                    Type = "push",
                    IntervalSeconds = 60
                }
            },
            MonitorPoints = new List<MonitorPointDefinition>
            {
                new()
                {
                    Id = "cpu-usage",
                    Name = "CPU Usage",
                    Category = "performance",
                    DataType = "float",
                    Unit = "%",
                    ResponsePath = "$.system.cpu.usagePercent",
                    Thresholds = new ThresholdConfig
                    {
                        Warning = new ConditionExpression { Operator = ">", Value = 80.0 },
                        Critical = new ConditionExpression { Operator = ">", Value = 95.0 }
                    },
                    Severity = "warning",
                    Description = "CPU utilization percentage across all cores"
                },
                new()
                {
                    Id = "memory-usage",
                    Name = "Memory Usage",
                    Category = "performance",
                    DataType = "float",
                    Unit = "%",
                    ResponsePath = "$.system.memory.usagePercent",
                    Thresholds = new ThresholdConfig
                    {
                        Warning = new ConditionExpression { Operator = ">", Value = 85.0 },
                        Critical = new ConditionExpression { Operator = ">", Value = 95.0 }
                    },
                    Severity = "warning",
                    Description = "RAM utilization as percentage of total available"
                },
                new()
                {
                    Id = "disk-usage",
                    Name = "Disk Usage",
                    Category = "performance",
                    DataType = "float",
                    Unit = "%",
                    ResponsePath = "$.system.disk.usagePercent",
                    Thresholds = new ThresholdConfig
                    {
                        Warning = new ConditionExpression { Operator = ">", Value = 80.0 },
                        Critical = new ConditionExpression { Operator = ">", Value = 90.0 }
                    },
                    Severity = "warning",
                    Description = "Primary storage utilization percentage"
                },
                new()
                {
                    Id = "content-playing",
                    Name = "Content Playing",
                    Category = "status",
                    DataType = "boolean",
                    ResponsePath = "$.playback.isPlaying",
                    ExpectedState = new ConditionExpression { Operator = "==", Value = true },
                    Severity = "critical",
                    Description = "Whether the player is actively rendering content"
                },
                new()
                {
                    Id = "current-content-id",
                    Name = "Current Content ID",
                    Category = "status",
                    DataType = "string",
                    ResponsePath = "$.playback.contentId",
                    ExpectedState = new ConditionExpression { Operator = "notEmpty" },
                    Severity = "info",
                    Description = "Identifier of the currently scheduled content"
                },
                new()
                {
                    Id = "last-content-sync",
                    Name = "Last Content Sync",
                    Category = "health",
                    DataType = "string",
                    ResponsePath = "$.content.lastSyncUtc",
                    ExpectedState = new ConditionExpression { Operator = "notEmpty" },
                    Severity = "warning",
                    Description = "Timestamp of the last successful content download"
                },
                new()
                {
                    Id = "uptime-seconds",
                    Name = "System Uptime",
                    Category = "health",
                    DataType = "integer",
                    Unit = "seconds",
                    ResponsePath = "$.system.uptimeSeconds",
                    Description = "Time since last system boot in seconds"
                },
                new()
                {
                    Id = "screenshot-available",
                    Name = "Screenshot Available",
                    Category = "status",
                    DataType = "boolean",
                    ResponsePath = "$.playback.screenshotAvailable",
                    Description = "Whether a recent screenshot capture is available for retrieval"
                }
            },
            Commands = new List<CommandDefinition>
            {
                new()
                {
                    Id = "reboot",
                    Name = "Reboot Player",
                    Category = "maintenance",
                    RiskLevel = "high",
                    RequiresApproval = true,
                    Command = "REBOOT",
                    TimeoutMs = 60000
                },
                new()
                {
                    Id = "restart-playback",
                    Name = "Restart Playback Engine",
                    Category = "maintenance",
                    RiskLevel = "medium",
                    Command = "RESTART_PLAYBACK",
                    TimeoutMs = 15000
                },
                new()
                {
                    Id = "force-content-sync",
                    Name = "Force Content Sync",
                    Category = "maintenance",
                    RiskLevel = "low",
                    Command = "SYNC_CONTENT",
                    TimeoutMs = 30000
                },
                new()
                {
                    Id = "capture-screenshot",
                    Name = "Capture Screenshot",
                    Category = "diagnostic",
                    RiskLevel = "low",
                    Command = "CAPTURE_SCREENSHOT",
                    TimeoutMs = 10000
                },
                new()
                {
                    Id = "update-firmware",
                    Name = "Update Firmware",
                    Category = "maintenance",
                    RiskLevel = "high",
                    RequiresApproval = true,
                    Command = "UPDATE_FIRMWARE",
                    Parameters = new List<CommandParameterDefinition>
                    {
                        new()
                        {
                            Name = "version",
                            Type = "string",
                            Required = true,
                            Description = "Target firmware version to install"
                        },
                        new()
                        {
                            Name = "forceDowngrade",
                            Type = "boolean",
                            Required = false,
                            Default = false,
                            Description = "Allow downgrading to an older firmware version"
                        }
                    },
                    TimeoutMs = 300000
                },
                new()
                {
                    Id = "clear-cache",
                    Name = "Clear Content Cache",
                    Category = "maintenance",
                    RiskLevel = "medium",
                    Command = "CLEAR_CACHE",
                    TimeoutMs = 15000
                }
            },
            HealthRules = new List<HealthRuleDefinition>
            {
                new()
                {
                    Name = "Content playback stopped",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "content-playing",
                        State = "critical",
                        DurationSeconds = 120
                    },
                    SuggestedActions = new List<string>
                    {
                        "Restart playback engine",
                        "Verify content files are present on device",
                        "Check disk space",
                        "Reboot player as last resort"
                    },
                    EscalationTier = 1,
                    CooldownMinutes = 15
                },
                new()
                {
                    Name = "High CPU usage sustained",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "cpu-usage",
                        State = "critical",
                        DurationSeconds = 300
                    },
                    SuggestedActions = new List<string>
                    {
                        "Capture diagnostic screenshot",
                        "Restart playback engine to release resources",
                        "Check for runaway processes",
                        "Reboot player if issue persists"
                    },
                    EscalationTier = 2,
                    CooldownMinutes = 30
                },
                new()
                {
                    Name = "Disk space critically low",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "disk-usage",
                        State = "critical",
                        DurationSeconds = 60
                    },
                    SuggestedActions = new List<string>
                    {
                        "Clear content cache",
                        "Remove old firmware packages",
                        "Check for log file accumulation"
                    },
                    EscalationTier = 2,
                    CooldownMinutes = 60
                }
            }
        };
    }

    /// <summary>
    /// AV processor template (QSC Q-SYS, Biamp, DSP) with QRC protocol.
    /// Includes design/program status, audio levels, and routing commands.
    /// </summary>
    public static DeviceDescriptorSchema GetAvProcessorTemplate()
    {
        return new DeviceDescriptorSchema
        {
            DescriptorVersion = "1.0",
            DeviceType = "av-processor-base",
            DisplayName = "AV Processor / DSP (Base Template)",
            Category = "av-processor",
            Communication = new CommunicationConfig
            {
                Protocol = "qrc",
                DefaultPort = 1710,
                Authentication = new AuthenticationConfig
                {
                    Type = "pin",
                    Required = false
                },
                ConnectionType = "persistent-tcp",
                HeartbeatMethod = new HeartbeatMethodConfig
                {
                    Type = "poll",
                    Command = "StatusGet",
                    IntervalSeconds = 15
                }
            },
            MonitorPoints = new List<MonitorPointDefinition>
            {
                new()
                {
                    Id = "design-running",
                    Name = "Design Running",
                    Category = "status",
                    DataType = "boolean",
                    RetrieveCommand = "StatusGet",
                    ResponsePath = "$.DesignName",
                    ExpectedState = new ConditionExpression { Operator = "notEmpty" },
                    Severity = "critical",
                    Description = "Whether a DSP design/program is loaded and running"
                },
                new()
                {
                    Id = "design-name",
                    Name = "Active Design Name",
                    Category = "status",
                    DataType = "string",
                    RetrieveCommand = "StatusGet",
                    ResponsePath = "$.DesignName",
                    Description = "Name of the currently loaded DSP design file"
                },
                new()
                {
                    Id = "audio-output-level",
                    Name = "Main Output Level",
                    Category = "performance",
                    DataType = "float",
                    Unit = "dB",
                    RetrieveCommand = "Component.Get",
                    RetrieveParams = new Dictionary<string, object>
                    {
                        { "Name", "MainOutput" },
                        { "Controls", new[] { "gain" } }
                    },
                    ResponsePath = "$.Controls[0].Value",
                    Description = "Main audio output gain level in decibels"
                },
                new()
                {
                    Id = "audio-output-mute",
                    Name = "Main Output Mute",
                    Category = "status",
                    DataType = "boolean",
                    RetrieveCommand = "Component.Get",
                    RetrieveParams = new Dictionary<string, object>
                    {
                        { "Name", "MainOutput" },
                        { "Controls", new[] { "mute" } }
                    },
                    ResponsePath = "$.Controls[0].Value",
                    Severity = "info",
                    Description = "Whether the main audio output is muted"
                },
                new()
                {
                    Id = "processor-temperature",
                    Name = "Processor Temperature",
                    Category = "health",
                    DataType = "float",
                    Unit = "C",
                    RetrieveCommand = "StatusGet",
                    ResponsePath = "$.Status.Temperature",
                    Thresholds = new ThresholdConfig
                    {
                        Warning = new ConditionExpression { Operator = ">", Value = 60.0 },
                        Critical = new ConditionExpression { Operator = ">", Value = 75.0 }
                    },
                    Severity = "warning",
                    Description = "Internal processor temperature in Celsius"
                },
                new()
                {
                    Id = "fan-status",
                    Name = "Fan Status",
                    Category = "health",
                    DataType = "string",
                    RetrieveCommand = "StatusGet",
                    ResponsePath = "$.Status.FanStatus",
                    ExpectedState = new ConditionExpression { Operator = "==", Value = "OK" },
                    Severity = "warning",
                    Description = "Cooling fan operational status"
                },
                new()
                {
                    Id = "dante-status",
                    Name = "Dante/AES67 Network Status",
                    Category = "health",
                    DataType = "string",
                    RetrieveCommand = "StatusGet",
                    ResponsePath = "$.Status.DanteStatus",
                    ExpectedState = new ConditionExpression { Operator = "==", Value = "Connected" },
                    Severity = "critical",
                    Description = "Audio-over-IP network interface status"
                }
            },
            Commands = new List<CommandDefinition>
            {
                new()
                {
                    Id = "set-gain",
                    Name = "Set Output Gain",
                    Category = "control",
                    RiskLevel = "low",
                    Command = "Component.Set",
                    Parameters = new List<CommandParameterDefinition>
                    {
                        new()
                        {
                            Name = "componentName",
                            Type = "string",
                            Required = true,
                            Default = "MainOutput",
                            Description = "Name of the audio component"
                        },
                        new()
                        {
                            Name = "gain",
                            Type = "float",
                            Required = true,
                            Description = "Gain level in dB (-100 to +20)"
                        }
                    },
                    TimeoutMs = 3000
                },
                new()
                {
                    Id = "set-mute",
                    Name = "Set Mute State",
                    Category = "control",
                    RiskLevel = "low",
                    Command = "Component.Set",
                    Parameters = new List<CommandParameterDefinition>
                    {
                        new()
                        {
                            Name = "componentName",
                            Type = "string",
                            Required = true,
                            Default = "MainOutput",
                            Description = "Name of the audio component"
                        },
                        new()
                        {
                            Name = "mute",
                            Type = "boolean",
                            Required = true,
                            Description = "Mute state (true/false)"
                        }
                    },
                    TimeoutMs = 2000
                },
                new()
                {
                    Id = "set-routing",
                    Name = "Set Audio Route",
                    Category = "control",
                    RiskLevel = "medium",
                    Command = "Component.Set",
                    Parameters = new List<CommandParameterDefinition>
                    {
                        new()
                        {
                            Name = "routerName",
                            Type = "string",
                            Required = true,
                            Description = "Name of the router component in the design"
                        },
                        new()
                        {
                            Name = "input",
                            Type = "integer",
                            Required = true,
                            Description = "Input channel number"
                        },
                        new()
                        {
                            Name = "output",
                            Type = "integer",
                            Required = true,
                            Description = "Output channel number"
                        }
                    },
                    TimeoutMs = 3000
                },
                new()
                {
                    Id = "load-design",
                    Name = "Load Design",
                    Category = "maintenance",
                    RiskLevel = "high",
                    RequiresApproval = true,
                    Command = "ChangeDesign",
                    Parameters = new List<CommandParameterDefinition>
                    {
                        new()
                        {
                            Name = "designName",
                            Type = "string",
                            Required = true,
                            Description = "Name of the design file to load"
                        }
                    },
                    TimeoutMs = 30000
                },
                new()
                {
                    Id = "reboot",
                    Name = "Reboot Processor",
                    Category = "maintenance",
                    RiskLevel = "high",
                    RequiresApproval = true,
                    Command = "Reboot",
                    TimeoutMs = 60000
                }
            },
            HealthRules = new List<HealthRuleDefinition>
            {
                new()
                {
                    Name = "No design running",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "design-running",
                        State = "critical",
                        DurationSeconds = 30
                    },
                    SuggestedActions = new List<string>
                    {
                        "Attempt to reload the last known design",
                        "Check processor logs for design load errors",
                        "Reboot processor if design fails to load"
                    },
                    EscalationTier = 1,
                    CooldownMinutes = 10
                },
                new()
                {
                    Name = "Audio network disconnected",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "dante-status",
                        State = "critical",
                        DurationSeconds = 15
                    },
                    SuggestedActions = new List<string>
                    {
                        "Check network switch port and cable",
                        "Verify Dante/AES67 network configuration",
                        "Check for IP address conflicts on audio network"
                    },
                    EscalationTier = 1,
                    CooldownMinutes = 5
                },
                new()
                {
                    Name = "Processor overheating",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "processor-temperature",
                        State = "critical",
                        DurationSeconds = 180
                    },
                    SuggestedActions = new List<string>
                    {
                        "Check fan status and airflow",
                        "Verify equipment rack ventilation",
                        "Reduce processing load if possible"
                    },
                    EscalationTier = 1,
                    CooldownMinutes = 30
                }
            }
        };
    }

    /// <summary>
    /// Generic IP device template for simple ICMP/HTTP reachability monitoring.
    /// Suitable for network switches, routers, or any IP-addressable device
    /// that only needs basic online/offline tracking.
    /// </summary>
    public static DeviceDescriptorSchema GetGenericIpTemplate()
    {
        return new DeviceDescriptorSchema
        {
            DescriptorVersion = "1.0",
            DeviceType = "generic-ip-base",
            DisplayName = "Generic IP Device (Base Template)",
            Category = "generic-ip",
            Communication = new CommunicationConfig
            {
                Protocol = "icmp",
                ConnectionType = "on-demand-tcp",
                HeartbeatMethod = new HeartbeatMethodConfig
                {
                    Type = "poll",
                    Command = "PING",
                    IntervalSeconds = 30
                }
            },
            MonitorPoints = new List<MonitorPointDefinition>
            {
                new()
                {
                    Id = "ping-reachable",
                    Name = "Ping Reachable",
                    Category = "health",
                    DataType = "boolean",
                    RetrieveCommand = "PING",
                    ExpectedState = new ConditionExpression { Operator = "==", Value = true },
                    Severity = "critical",
                    Description = "Whether the device responds to ICMP echo requests"
                },
                new()
                {
                    Id = "ping-latency",
                    Name = "Ping Latency",
                    Category = "performance",
                    DataType = "float",
                    Unit = "ms",
                    RetrieveCommand = "PING",
                    ResponsePath = "$.latencyMs",
                    Thresholds = new ThresholdConfig
                    {
                        Warning = new ConditionExpression { Operator = ">", Value = 100.0 },
                        Critical = new ConditionExpression { Operator = ">", Value = 500.0 }
                    },
                    Severity = "warning",
                    Description = "Round-trip ICMP latency in milliseconds"
                },
                new()
                {
                    Id = "http-health",
                    Name = "HTTP Health Check",
                    Category = "health",
                    DataType = "boolean",
                    RetrieveCommand = "HTTP_GET",
                    RetrieveParams = new Dictionary<string, object>
                    {
                        { "path", "/health" },
                        { "expectedStatusCode", 200 }
                    },
                    ExpectedState = new ConditionExpression { Operator = "==", Value = true },
                    Severity = "critical",
                    Description = "Whether the device's HTTP health endpoint returns a successful response"
                },
                new()
                {
                    Id = "http-response-time",
                    Name = "HTTP Response Time",
                    Category = "performance",
                    DataType = "float",
                    Unit = "ms",
                    RetrieveCommand = "HTTP_GET",
                    RetrieveParams = new Dictionary<string, object>
                    {
                        { "path", "/health" }
                    },
                    ResponsePath = "$.responseTimeMs",
                    Thresholds = new ThresholdConfig
                    {
                        Warning = new ConditionExpression { Operator = ">", Value = 2000.0 },
                        Critical = new ConditionExpression { Operator = ">", Value = 5000.0 }
                    },
                    Severity = "warning",
                    Description = "HTTP response time in milliseconds"
                }
            },
            Commands = new List<CommandDefinition>
            {
                new()
                {
                    Id = "ping",
                    Name = "Ping Device",
                    Category = "diagnostic",
                    RiskLevel = "low",
                    Command = "PING",
                    TimeoutMs = 5000
                },
                new()
                {
                    Id = "http-check",
                    Name = "HTTP Health Check",
                    Category = "diagnostic",
                    RiskLevel = "low",
                    Protocol = "http-api",
                    Command = "HTTP_GET",
                    Parameters = new List<CommandParameterDefinition>
                    {
                        new()
                        {
                            Name = "path",
                            Type = "string",
                            Required = false,
                            Default = "/health",
                            Description = "HTTP path to check"
                        }
                    },
                    TimeoutMs = 10000
                },
                new()
                {
                    Id = "traceroute",
                    Name = "Trace Route",
                    Category = "diagnostic",
                    RiskLevel = "low",
                    Command = "TRACEROUTE",
                    TimeoutMs = 30000
                }
            },
            HealthRules = new List<HealthRuleDefinition>
            {
                new()
                {
                    Name = "Device unreachable",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "ping-reachable",
                        State = "critical",
                        DurationSeconds = 90
                    },
                    SuggestedActions = new List<string>
                    {
                        "Verify network connectivity to the device subnet",
                        "Check physical power and cabling",
                        "Confirm device IP has not changed (check DHCP leases)",
                        "Run traceroute to identify network path issues"
                    },
                    EscalationTier = 2,
                    CooldownMinutes = 10
                },
                new()
                {
                    Name = "HTTP health endpoint failing",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "http-health",
                        State = "critical",
                        DurationSeconds = 120
                    },
                    SuggestedActions = new List<string>
                    {
                        "Verify device web service is running",
                        "Check if device responds to ping (network vs service issue)",
                        "Review device logs for application errors"
                    },
                    EscalationTier = 2,
                    CooldownMinutes = 15
                },
                new()
                {
                    Name = "High network latency",
                    Condition = new HealthRuleCondition
                    {
                        MonitorPoint = "ping-latency",
                        State = "warning",
                        DurationSeconds = 300
                    },
                    SuggestedActions = new List<string>
                    {
                        "Check network switch for errors or congestion",
                        "Run traceroute to identify slow hops",
                        "Verify no bandwidth-heavy operations are in progress"
                    },
                    EscalationTier = 3,
                    CooldownMinutes = 30
                }
            }
        };
    }
}
