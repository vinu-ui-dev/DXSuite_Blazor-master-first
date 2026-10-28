using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DXSuite.Connectors.Services.Connectors;
using DXSuite.Director.Services;
using DXSuite.Sentinel.Entities;
using DXSuite.Shared.Services;
using DXSuite.Sentinel.Services.Drivers;
using DXSuite.Sentinel.Services.Gateway;
using System.Diagnostics;
using System.Text.Json;

namespace DXSuite.Sentinel.Services.Routing;

/// <summary>
/// Routes device commands through the optimal communication path.
/// Priority: 1) Site Gateway (LAN direct) 2) Heartbeat piggyback 3) Cloud Platform API
/// </summary>
public class CommandRouter : ICommandRouter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeviceDriverRegistry _driverRegistry;
    private readonly IGatewayConnectionManager _gatewayManager;
    private readonly ICloudPlatformConnectorRegistry _platformRegistry;
    private readonly ICecCommandService _cecCommandService;
    private readonly IDeviceDwsCredentialService _dwsCredentials;
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(
        IServiceScopeFactory scopeFactory,
        DeviceDriverRegistry driverRegistry,
        IGatewayConnectionManager gatewayManager,
        ICloudPlatformConnectorRegistry platformRegistry,
        ICecCommandService cecCommandService,
        IDeviceDwsCredentialService dwsCredentials,
        ILogger<CommandRouter> logger)
    {
        _scopeFactory = scopeFactory;
        _driverRegistry = driverRegistry;
        _gatewayManager = gatewayManager;
        _platformRegistry = platformRegistry;
        _cecCommandService = cecCommandService;
        _dwsCredentials = dwsCredentials;
        _logger = logger;
    }

    public Task<CommandResult> RouteCommandAsync(int deviceInstanceId, string commandId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
    {
        return RouteCommandAsync(deviceInstanceId, commandId, parameters, RoutePreference.Auto, cancellationToken);
    }

    public async Task<CommandResult> RouteCommandAsync(int deviceInstanceId, string commandId, Dictionary<string, object>? parameters, RoutePreference preference, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new CommandResult { CommandId = commandId };

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var deviceInfo = await LoadDeviceInfoAsync(scope, deviceInstanceId, cancellationToken);
            if (deviceInfo == null)
            {
                result.ErrorMessage = $"Device instance {deviceInstanceId} not found or not enabled";
                return result;
            }

            var descriptor = await LoadDescriptorAsync(scope, deviceInfo.DescriptorId, deviceInfo.DescriptorVersionId, cancellationToken);
            if (descriptor == null)
            {
                result.ErrorMessage = $"Device descriptor not found for device instance {deviceInstanceId}";
                return result;
            }

            _logger.LogDebug(
                "Routing command {CommandId} to device {DeviceInstanceId} ({DisplayName}), preference={Preference}",
                commandId, deviceInstanceId, deviceInfo.DisplayName, preference);

            // Try routes in priority order based on preference
            if (preference == RoutePreference.Auto || preference == RoutePreference.PreferGateway)
            {
                var gatewayResult = await TryRouteViaGatewayAsync(scope, deviceInfo, commandId, parameters, descriptor, cancellationToken);
                if (gatewayResult != null)
                {
                    gatewayResult.ElapsedMs = sw.ElapsedMilliseconds;
                    return gatewayResult;
                }
            }

            if (preference == RoutePreference.Auto || preference == RoutePreference.PreferHeartbeat)
            {
                if (!string.IsNullOrEmpty(deviceInfo.DirectorDeviceId) && descriptor.Communication.Protocol == "http-heartbeat")
                {
                    var heartbeatResult = TryRouteViaHeartbeatPiggyback(deviceInfo, commandId, parameters, descriptor);
                    if (heartbeatResult != null)
                    {
                        heartbeatResult.ElapsedMs = sw.ElapsedMilliseconds;
                        return heartbeatResult;
                    }
                }
            }

            if (preference == RoutePreference.Auto || preference == RoutePreference.PreferCloudPlatform)
            {
                var platformResult = await TryRouteViaCloudPlatformAsync(scope, deviceInstanceId, commandId, parameters, cancellationToken);
                if (platformResult != null)
                {
                    platformResult.ElapsedMs = sw.ElapsedMilliseconds;
                    return platformResult;
                }
            }

            // Last resort: direct driver execution (device on same network as Director)
            var driver = _driverRegistry.GetDriver(descriptor.Communication.Protocol);
            if (driver != null)
            {
                var instanceInfo = await MapToDriverInfoAsync(deviceInfo);
                result = await driver.ExecuteCommandAsync(instanceInfo, commandId, parameters, descriptor, cancellationToken);
                result.RoutedVia = "direct";
                result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            result.ErrorMessage = $"No available route to device {deviceInstanceId}. " +
                                  $"Gateway={deviceInfo.SiteGatewayId.HasValue}, " +
                                  $"Heartbeat={!string.IsNullOrEmpty(deviceInfo.DirectorDeviceId)}, " +
                                  $"Protocol={descriptor.Communication.Protocol}";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error routing command {CommandId} to device {DeviceInstanceId}", commandId, deviceInstanceId);
        }
        finally
        {
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    private async Task<DeviceRoutingInfo?> LoadDeviceInfoAsync(IServiceScope scope, int deviceInstanceId, CancellationToken ct)
    {
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        var instance = await sentinelDb.DeviceInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceInstanceId == deviceInstanceId && d.IsEnabled, ct);

        if (instance == null)
            return null;

        return new DeviceRoutingInfo
        {
            DeviceInstanceId = instance.DeviceInstanceId,
            DescriptorId = instance.DescriptorId,
            DescriptorVersionId = instance.DescriptorVersionId,
            DirectorDeviceId = instance.DirectorDeviceId,
            SiteGatewayId = instance.SiteGatewayId,
            IpAddress = instance.IpAddress,
            Port = instance.Port,
            MacAddress = instance.MacAddress,
            DisplayName = instance.DisplayName,
            CustomerId = instance.CustomerId,
            CredentialsJson = instance.CredentialsJson,
            ConfigJson = instance.ConfigJson
        };
    }

    private async Task<DeviceDescriptorSchema?> LoadDescriptorAsync(IServiceScope scope, int descriptorId, int? descriptorVersionId, CancellationToken ct)
    {
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        string? descriptorJson = null;

        // Prefer the pinned version if specified, otherwise use the active version
        if (descriptorVersionId.HasValue)
        {
            var version = await sentinelDb.DeviceDescriptorVersions
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.VersionId == descriptorVersionId.Value, ct);

            descriptorJson = version?.DescriptorJson;
        }

        if (string.IsNullOrEmpty(descriptorJson))
        {
            // Fall back to the active version for this descriptor
            var activeVersion = await sentinelDb.DeviceDescriptorVersions
                .AsNoTracking()
                .Where(v => v.DescriptorId == descriptorId && v.IsActive)
                .OrderByDescending(v => v.PublishedAt)
                .FirstOrDefaultAsync(ct);

            descriptorJson = activeVersion?.DescriptorJson;
        }

        if (string.IsNullOrEmpty(descriptorJson))
        {
            _logger.LogWarning("No descriptor JSON found for DescriptorId={DescriptorId}, VersionId={VersionId}",
                descriptorId, descriptorVersionId);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeviceDescriptorSchema>(descriptorJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize descriptor JSON for DescriptorId={DescriptorId}", descriptorId);
            return null;
        }
    }

    private async Task<CommandResult?> TryRouteViaGatewayAsync(
        IServiceScope scope,
        DeviceRoutingInfo deviceInfo,
        string commandId,
        Dictionary<string, object>? parameters,
        DeviceDescriptorSchema descriptor,
        CancellationToken ct)
    {
        if (!deviceInfo.SiteGatewayId.HasValue)
            return null;

        var gatewayId = deviceInfo.SiteGatewayId.Value;

        if (!_gatewayManager.IsGatewayOnline(gatewayId))
        {
            _logger.LogDebug("Gateway {GatewayId} is offline, skipping gateway route for device {DeviceInstanceId}",
                gatewayId, deviceInfo.DeviceInstanceId);
            return null;
        }

        // Resolve the LAN IP for this device on the gateway's network
        var (lanIp, lanPort) = await ResolveGatewayLanAddressAsync(scope, gatewayId, deviceInfo, ct);
        if (string.IsNullOrEmpty(lanIp))
        {
            _logger.LogDebug("No LAN address mapped for device {DeviceInstanceId} on gateway {GatewayId}",
                deviceInfo.DeviceInstanceId, gatewayId);
            return null;
        }

        try
        {
            var result = await _gatewayManager.SendCommandViaGatewayAsync(
                gatewayId, lanIp, lanPort, commandId, parameters, ct);
            result.RoutedVia = "gateway";

            _logger.LogInformation(
                "Command {CommandId} routed via gateway {GatewayId} to {LanIp}:{LanPort}, success={Success}",
                commandId, gatewayId, lanIp, lanPort, result.Success);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gateway route failed for command {CommandId} to device {DeviceInstanceId}",
                commandId, deviceInfo.DeviceInstanceId);
            return null;
        }
    }

    private async Task<(string? ip, int? port)> ResolveGatewayLanAddressAsync(
        IServiceScope scope, int gatewayId, DeviceRoutingInfo deviceInfo, CancellationToken ct)
    {
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        // Look for a specific LAN mapping for this device on this gateway
        var mapping = await sentinelDb.SiteGatewayDeviceMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.GatewayId == gatewayId &&
                m.DeviceInstanceId == deviceInfo.DeviceInstanceId &&
                m.IsReachable, ct);

        if (mapping != null && !string.IsNullOrEmpty(mapping.LanIpAddress))
        {
            return (mapping.LanIpAddress, mapping.LanPort);
        }

        // Fall back to the device's own IP address (may be the same network)
        if (!string.IsNullOrEmpty(deviceInfo.IpAddress))
        {
            return (deviceInfo.IpAddress, deviceInfo.Port);
        }

        return (null, null);
    }

    private CommandResult? TryRouteViaHeartbeatPiggyback(
        DeviceRoutingInfo deviceInfo,
        string commandId,
        Dictionary<string, object>? parameters,
        DeviceDescriptorSchema descriptor)
    {
        if (string.IsNullOrEmpty(deviceInfo.DirectorDeviceId))
            return null;

        // Only CEC-compatible commands can be piggybacked on heartbeat responses
        var cmdDef = descriptor.Commands.FirstOrDefault(c =>
            c.Id.Equals(commandId, StringComparison.OrdinalIgnoreCase));

        var isCecCommand = commandId.StartsWith("cec-", StringComparison.OrdinalIgnoreCase) ||
                           cmdDef?.Protocol?.Equals("cec", StringComparison.OrdinalIgnoreCase) == true;

        if (!isCecCommand)
        {
            _logger.LogDebug(
                "Command {CommandId} is not CEC-compatible, skipping heartbeat piggyback for device {DeviceId}",
                commandId, deviceInfo.DirectorDeviceId);
            return null;
        }

        try
        {
            var action = cmdDef?.Command ?? commandId.Replace("cec-", "").Replace("-", "_");
            var output = parameters?.TryGetValue("output", out var outputVal) == true
                ? outputVal?.ToString()
                : null;

            var cecCmd = new CecCommand
            {
                Action = action,
                Output = output
            };

            _cecCommandService.QueueCommand(deviceInfo.DirectorDeviceId, cecCmd);

            _logger.LogInformation(
                "CEC command {Action} queued via heartbeat piggyback for device {DeviceId}",
                action, deviceInfo.DirectorDeviceId);

            return new CommandResult
            {
                CommandId = commandId,
                Success = true,
                Response = $"CEC command '{action}' queued for next heartbeat sync",
                RoutedVia = "heartbeat-piggyback"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue heartbeat piggyback command {CommandId}", commandId);
            return null;
        }
    }

    private async Task<CommandResult?> TryRouteViaCloudPlatformAsync(
        IServiceScope scope,
        int deviceInstanceId,
        string commandId,
        Dictionary<string, object>? parameters,
        CancellationToken ct)
    {
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();

        // Load links from SentinelDB
        var links = await sentinelDb.PlatformDeviceLinks
            .AsNoTracking()
            .Where(l => l.DeviceInstanceId == deviceInstanceId)
            .ToListAsync(ct);

        if (links.Count == 0)
            return null;

        // Load connectors (cross-DB, no navigation property)
        var connectorIds = links.Select(l => l.ConnectorId).Distinct().ToList();

        // Try each linked platform connector
        foreach (var link in links)
        {
            var connector = _platformRegistry.GetConnector(link.ConnectorId.ToString());
            if (connector == null)
            {
                _logger.LogDebug("No registered connector for link ConnectorId {ConnectorId}", link.ConnectorId);
                continue;
            }

            var capabilities = connector.GetCapabilities();
            if (!capabilities.SupportedCommands.Contains(commandId, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Connector {ConnectorId} does not support command {CommandId}",
                    link.ConnectorId, commandId);
                continue;
            }

            try
            {
                var config = new PlatformConnectionConfig(); // TODO: Load from connector config
                var platformResult = await connector.ExecuteCommandAsync(
                    config, link.PlatformDeviceId, commandId, parameters, ct);
                var result = new CommandResult
                {
                    Success = platformResult.Success,
                    CommandId = platformResult.CommandId,
                    ErrorMessage = platformResult.ErrorMessage,
                    RoutedVia = $"cloud-platform:{link.ConnectorId}",
                    ResponseData = platformResult.ResultData
                };

                _logger.LogInformation(
                    "Command {CommandId} routed via cloud platform connector {ConnectorId} for device {DeviceInstanceId}, success={Success}",
                    commandId, link.ConnectorId, deviceInstanceId, result.Success);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Cloud platform route failed via connector {ConnectorId} for command {CommandId} on device {DeviceInstanceId}",
                    link.ConnectorId, commandId, deviceInstanceId);
            }
        }

        return null;
    }

    private async Task<DeviceInstanceInfo> MapToDriverInfoAsync(DeviceRoutingInfo routing)
    {
        Dictionary<string, string>? credentials = null;
        if (!string.IsNullOrEmpty(routing.CredentialsJson))
        {
            try
            {
                credentials = JsonSerializer.Deserialize<Dictionary<string, string>>(routing.CredentialsJson);
            }
            catch { /* Credentials are optional; log only if needed */ }
        }

        // Director's device row is authoritative for a BrightSign player's DWS password, and
        // overrides whatever CredentialsJson holds. That column has no write path anywhere in the
        // repo and no data protector, so it could never have carried the per-device secret
        // enrollment rotates to; the secret is stored where it is generated instead.
        //
        // DirectorDeviceId is a soft cross-database string keyed on the serial, not a foreign key,
        // so a miss here is ordinary rather than an error: it means this instance is not a paired
        // Engage player, and CredentialsJson stands for every other device type.
        if (!string.IsNullOrEmpty(routing.DirectorDeviceId))
        {
            var rotated = await _dwsCredentials.GetPasswordAsync(routing.DirectorDeviceId);
            if (!string.IsNullOrEmpty(rotated))
            {
                credentials ??= new Dictionary<string, string>();
                credentials["password"] = rotated;
            }
        }

        Dictionary<string, object>? config = null;
        if (!string.IsNullOrEmpty(routing.ConfigJson))
        {
            try
            {
                config = JsonSerializer.Deserialize<Dictionary<string, object>>(routing.ConfigJson);
            }
            catch { /* Config is optional */ }
        }

        return new DeviceInstanceInfo
        {
            DeviceInstanceId = routing.DeviceInstanceId,
            DisplayName = routing.DisplayName,
            IpAddress = routing.IpAddress,
            Port = routing.Port,
            MacAddress = routing.MacAddress,
            Credentials = credentials,
            Config = config
        };
    }

    /// <summary>
    /// Internal model holding the routing-relevant fields loaded from a DeviceInstance entity
    /// </summary>
    private class DeviceRoutingInfo
    {
        public int DeviceInstanceId { get; set; }
        public int DescriptorId { get; set; }
        public int? DescriptorVersionId { get; set; }
        public string? DirectorDeviceId { get; set; }
        public int? SiteGatewayId { get; set; }
        public string? IpAddress { get; set; }
        public int? Port { get; set; }
        public string? MacAddress { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string? CredentialsJson { get; set; }
        public string? ConfigJson { get; set; }
    }
}
