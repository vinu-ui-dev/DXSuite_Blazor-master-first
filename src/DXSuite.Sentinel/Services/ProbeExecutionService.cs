using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Gateway;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Resolves probe strategy profiles and executes ordered probe cascades against device IPs.
/// Every probe method handles its own errors — no exceptions propagate from probe execution.
/// </summary>
public class ProbeExecutionService
{
    private readonly ILogger<ProbeExecutionService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProbeExecutionService(ILogger<ProbeExecutionService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Resolves the effective probe strategy for a Sentinel (cloud) device.
    /// Resolution order: device override → make/model profile → environment default.
    /// Returns a fallback single-ICMP strategy if no profiles exist.
    /// </summary>
    public async Task<ResolvedProbeStrategy> ResolveStrategyAsync(
        int deviceInstanceId, string? manufacturer, string? model, string customerId, ISentinelDBContext db)
    {
        var profiles = await db.ProbeProfiles
            .Where(p => p.CustomerId == customerId && p.IsActive)
            .OrderBy(p => p.Priority)
            .AsNoTracking()
            .ToListAsync();

        // Tier 1: Device-specific override
        var deviceProfile = profiles.FirstOrDefault(p =>
            p.Scope == "device" && p.DeviceInstanceId == deviceInstanceId);
        if (deviceProfile != null)
            return ParseStrategy(deviceProfile);

        // Tier 2: Make/Model match (most specific first: manufacturer+model, then manufacturer-only)
        if (!string.IsNullOrEmpty(manufacturer))
        {
            var makeModelProfile = profiles.FirstOrDefault(p =>
                p.Scope == "make-model"
                && string.Equals(p.MatchManufacturer, manufacturer, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(p.MatchModel)
                && string.Equals(p.MatchModel, model, StringComparison.OrdinalIgnoreCase));

            if (makeModelProfile != null)
                return ParseStrategy(makeModelProfile);

            var makeOnlyProfile = profiles.FirstOrDefault(p =>
                p.Scope == "make-model"
                && string.Equals(p.MatchManufacturer, manufacturer, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(p.MatchModel));

            if (makeOnlyProfile != null)
                return ParseStrategy(makeOnlyProfile);
        }

        // Tier 3: Environment default
        var envProfile = profiles.FirstOrDefault(p => p.Scope == "environment");
        if (envProfile != null)
            return ParseStrategy(envProfile);

        // Fallback: no profiles configured — basic ICMP-only strategy
        return new ResolvedProbeStrategy
        {
            ProfileName = "(built-in fallback)",
            Steps = new List<ProbeStepConfig>
            {
                new() { Method = "icmp", Label = "ICMP Ping", TimeoutMs = 3000, Expected = true }
            }
        };
    }

    /// <summary>
    /// Executes a probe cascade against a device IP.
    /// Walks steps top-to-bottom, stops at first success. Every step is individually
    /// try-caught so a single failure never crashes the cascade.
    /// </summary>
    public async Task<ProbeResult> ExecuteProbeAsync(
        string ipAddress, ResolvedProbeStrategy strategy, CancellationToken ct)
    {
        var result = new ProbeResult
        {
            ProfileName = strategy.ProfileName,
            Timestamp = DateTime.UtcNow,
            Steps = new List<ProbeStepResult>()
        };

        foreach (var step in strategy.Steps)
        {
            if (ct.IsCancellationRequested)
                break;

            var stepResult = step.Method.ToLowerInvariant() switch
            {
                "tcp" => await ExecuteTcpProbeAsync(ipAddress, step.Port ?? 80, step.TimeoutMs, ct),
                "icmp" => await ExecuteIcmpProbeAsync(ipAddress, step.TimeoutMs, ct),
                "http" => await ExecuteHttpProbeAsync(ipAddress, step.Port ?? 80, step.TimeoutMs, ct),
                _ => new ProbeStepResult
                {
                    Result = "unsupported",
                    ErrorDetail = $"Unknown probe method: {step.Method}"
                }
            };

            stepResult.Method = step.Method;
            stepResult.Port = step.Port;
            stepResult.Label = step.Label;
            stepResult.Expected = step.Expected;
            result.Steps.Add(stepResult);

            if (stepResult.Result == "passed")
            {
                result.StoppedAt = step.Method;
                break;
            }
        }

        result.Color = DetermineStatusColor(result);
        result.Overall = result.Steps.Any(s => s.Result == "passed") ? "online" : "offline";

        return result;
    }

    /// <summary>
    /// Determines status color from probe results and expected flags.
    /// error/unsupported steps are excused from expectation failure.
    /// </summary>
    public string DetermineStatusColor(ProbeResult result)
    {
        var passedSteps = result.Steps.Where(s => s.Result == "passed").ToList();
        if (passedSteps.Count == 0)
            return "red";

        // Only count expected failures on steps that actually ran (not error/unsupported)
        var failedExpected = result.Steps
            .Where(s => s.Expected && s.Result is "failed" or "timeout")
            .ToList();

        if (failedExpected.Count == 0)
            return "green";

        // Some expected steps failed but we got at least one pass — degraded
        return "yellow";
    }

    /// <summary>
    /// Determines combined status for Director devices using heartbeat + probe data.
    /// </summary>
    public static string DetermineDirectorStatusColor(DateTime? lastHeartbeat, ProbeResult? probeResult)
    {
        var staleThreshold = TimeSpan.FromMinutes(2);

        if (lastHeartbeat.HasValue && (DateTime.UtcNow - lastHeartbeat.Value) < staleThreshold)
            return "green";

        // Heartbeat stale — check probe
        if (probeResult == null)
            return "red";

        if (probeResult.Steps.Any(s => s.Result == "passed"))
            return "yellow"; // Network OK, client issue

        return "red"; // Unreachable
    }

    private async Task<ProbeStepResult> ExecuteTcpProbeAsync(
        string ip, int port, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await client.ConnectAsync(ip, port, cts.Token);
            sw.Stop();
            return new ProbeStepResult { Result = "passed", ElapsedMs = (int)sw.ElapsedMilliseconds };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new ProbeStepResult
            {
                Result = "timeout",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = $"TCP connect to :{port} timed out after {timeoutMs}ms"
            };
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return new ProbeStepResult
            {
                Result = "failed",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = ex.SocketErrorCode.ToString()
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogDebug(ex, "TCP probe error for {Ip}:{Port}", ip, port);
            return new ProbeStepResult
            {
                Result = "error",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = ex.GetType().Name
            };
        }
    }

    private async Task<ProbeStepResult> ExecuteIcmpProbeAsync(
        string ip, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, timeoutMs);
            sw.Stop();
            if (reply.Status == IPStatus.Success)
            {
                return new ProbeStepResult { Result = "passed", ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
            return new ProbeStepResult
            {
                Result = "failed",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = reply.Status.ToString()
            };
        }
        catch (PlatformNotSupportedException)
        {
            sw.Stop();
            return new ProbeStepResult
            {
                Result = "unsupported",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = "ICMP not supported on this platform"
            };
        }
        catch (UnauthorizedAccessException)
        {
            sw.Stop();
            return new ProbeStepResult
            {
                Result = "unsupported",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = "ICMP requires elevated privileges"
            };
        }
        catch (PingException ex)
        {
            sw.Stop();
            return new ProbeStepResult
            {
                Result = "failed",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = ex.InnerException?.Message ?? ex.Message
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogDebug(ex, "ICMP probe error for {Ip}", ip);
            return new ProbeStepResult
            {
                Result = "error",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = ex.GetType().Name
            };
        }
    }

    private async Task<ProbeStepResult> ExecuteHttpProbeAsync(
        string ip, int port, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var httpClient = _httpClientFactory.CreateClient("ProbeHttp");
            httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
            var scheme = port == 443 ? "https" : "http";
            var url = $"{scheme}://{ip}:{port}/";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var response = await httpClient.GetAsync(url, cts.Token);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new ProbeStepResult { Result = "passed", ElapsedMs = (int)sw.ElapsedMilliseconds };
            }
            return new ProbeStepResult
            {
                Result = "failed",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            return new ProbeStepResult
            {
                Result = "timeout",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = $"HTTP request timed out after {timeoutMs}ms"
            };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return new ProbeStepResult
            {
                Result = "failed",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = ex.Message
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogDebug(ex, "HTTP probe error for {Ip}:{Port}", ip, port);
            return new ProbeStepResult
            {
                Result = "error",
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                ErrorDetail = ex.GetType().Name
            };
        }
    }

    /// <summary>
    /// Executes a probe cascade against a device via a remote gateway.
    /// Sends an execute-probe command through the gateway's WebSocket connection and
    /// deserializes the result. Returns null if the gateway is offline or the command fails.
    /// </summary>
    public async Task<ProbeResult?> ExecuteProbeViaGatewayAsync(
        int gatewayId, string ipAddress, ResolvedProbeStrategy strategy,
        IGatewayConnectionManager gatewayManager, CancellationToken ct)
    {
        if (!gatewayManager.IsGatewayOnline(gatewayId))
        {
            _logger.LogDebug("Gateway {GatewayId} is offline, cannot execute remote probe for {Ip}", gatewayId, ipAddress);
            return null;
        }

        try
        {
            var parameters = new Dictionary<string, object>
            {
                ["ipAddress"] = ipAddress,
                ["strategy"] = JsonSerializer.Serialize(strategy, JsonOptions)
            };

            var commandResult = await gatewayManager.SendCommandViaGatewayAsync(
                gatewayId, ipAddress, null, "execute-probe", parameters, ct);

            if (commandResult.Success && !string.IsNullOrEmpty(commandResult.Response))
            {
                var probeResult = JsonSerializer.Deserialize<ProbeResult>(commandResult.Response, JsonOptions);
                return probeResult;
            }

            _logger.LogDebug("Remote probe via gateway {GatewayId} for {Ip} failed: {Error}",
                gatewayId, ipAddress, commandResult.ErrorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error executing probe via gateway {GatewayId} for {Ip}", gatewayId, ipAddress);
        }

        return null;
    }

    private ResolvedProbeStrategy ParseStrategy(ProbeProfile profile)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ProbeStrategyConfig>(profile.StrategyJson, JsonOptions);
            return new ResolvedProbeStrategy
            {
                ProfileName = profile.ProfileName,
                Steps = config?.Steps ?? new List<ProbeStepConfig>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse StrategyJson for probe profile '{ProfileName}' (ID {ProfileId}), using ICMP fallback",
                profile.ProfileName, profile.ProfileId);
            return new ResolvedProbeStrategy
            {
                ProfileName = profile.ProfileName + " (parse error)",
                Steps = new List<ProbeStepConfig>
                {
                    new() { Method = "icmp", Label = "ICMP Ping (fallback)", TimeoutMs = 3000, Expected = true }
                }
            };
        }
    }

    /// <summary>
    /// Serializes a ProbeResult to JSON for storage in MonitorReadings or ProbeHistory.
    /// </summary>
    public static string SerializeResult(ProbeResult result)
    {
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// Deserializes a ProbeResult from JSON stored in MonitorReadings.
    /// Returns null if the value is not a valid ProbeResult JSON.
    /// </summary>
    public static ProbeResult? DeserializeResult(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ProbeResult>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

#region DTOs

/// <summary>
/// JSON structure stored in ProbeProfile.StrategyJson
/// </summary>
public class ProbeStrategyConfig
{
    [JsonPropertyName("steps")]
    public List<ProbeStepConfig> Steps { get; set; } = new();
}

public class ProbeStepConfig
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "tcp";

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; set; } = 3000;

    [JsonPropertyName("expected")]
    public bool Expected { get; set; }
}

/// <summary>
/// Resolved strategy ready for execution (profile name + parsed steps)
/// </summary>
public class ResolvedProbeStrategy
{
    public string ProfileName { get; set; } = string.Empty;
    public List<ProbeStepConfig> Steps { get; set; } = new();
}

/// <summary>
/// Full result of a probe cascade execution.
/// Stored in MonitorReading.Value and ProbeHistory.ResultJson.
/// </summary>
public class ProbeResult
{
    [JsonPropertyName("overall")]
    public string Overall { get; set; } = "offline";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "red";

    [JsonPropertyName("stoppedAt")]
    public string? StoppedAt { get; set; }

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "scheduled";

    [JsonPropertyName("steps")]
    public List<ProbeStepResult> Steps { get; set; } = new();

    [JsonPropertyName("profileName")]
    public string? ProfileName { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}

public class ProbeStepResult
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// "passed" | "failed" | "timeout" | "error" | "unsupported"
    /// </summary>
    [JsonPropertyName("result")]
    public string Result { get; set; } = "error";

    [JsonPropertyName("elapsedMs")]
    public int ElapsedMs { get; set; }

    [JsonPropertyName("expected")]
    public bool Expected { get; set; }

    [JsonPropertyName("errorDetail")]
    public string? ErrorDetail { get; set; }
}

#endregion
