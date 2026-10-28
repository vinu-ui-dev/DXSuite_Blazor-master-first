using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Connectors;

/// <summary>
/// Cloud platform connector for Q-SYS Reflect Enterprise Manager.
/// Two-pass inventory: first fetches Q-SYS Core processors via /cores, then fetches
/// peripheral items (cameras, amplifiers, I/O) via /systems/{id}/items.
/// Auth is Bearer token via the Authorization header.
/// </summary>
public class QSysReflectConnector : ICloudPlatformConnector
{
    public string PlatformId => "qsys-reflect";
    public string DisplayName => "Q-SYS Reflect";

    private readonly HttpClient _httpClient;
    private readonly ILogger<QSysReflectConnector> _logger;

    private const string DefaultBaseUrl = "https://reflect.qsc.com/api/public/v0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new LenientStringConverter() }
    };

    /// <summary>
    /// Handles the Reflect API returning numbers or booleans where we expect strings.
    /// Without this, System.Text.Json throws on e.g. "uptime": 12345 when the DTO has string? Uptime.
    /// </summary>
    private class LenientStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.Number when reader.TryGetInt64(out var l): return l.ToString();
                case JsonTokenType.Number when reader.TryGetDouble(out var d): return d.ToString();
                case JsonTokenType.True: return "true";
                case JsonTokenType.False: return "false";
                case JsonTokenType.Null: return null;
                default:
                    using (var doc = JsonDocument.ParseValue(ref reader))
                        return doc.RootElement.GetRawText();
            }
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    public QSysReflectConnector(HttpClient httpClient, ILogger<QSysReflectConnector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IList<PlatformDevice>> SyncDeviceInventoryAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        var devices = new List<PlatformDevice>();
        var seenIds = new HashSet<string>();

        try
        {
            // Pass 1: Fetch Q-SYS Core processors
            var coresRequest = CreateRequest(HttpMethod.Get, config, "cores");
            var coresResponse = await _httpClient.SendAsync(coresRequest, cancellationToken);
            coresResponse.EnsureSuccessStatusCode();

            var coresJson = await coresResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Q-SYS Reflect cores response ({Length} chars): {Preview}",
                coresJson.Length, coresJson.Length > 3000 ? coresJson[..3000] : coresJson);

            var cores = JsonSerializer.Deserialize<List<ReflectCore>>(coresJson, JsonOptions) ?? new();

            foreach (var core in cores)
            {
                var pd = MapCoreToPlatformDevice(core);
                if (seenIds.Add(pd.PlatformDeviceId))
                    devices.Add(pd);
            }

            // Pass 2: Fetch systems -> items (peripherals attached to each system)
            var systemsRequest = CreateRequest(HttpMethod.Get, config, "systems");
            var systemsResponse = await _httpClient.SendAsync(systemsRequest, cancellationToken);
            systemsResponse.EnsureSuccessStatusCode();

            var systemsJson = await systemsResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Q-SYS Reflect systems response ({Length} chars): {Preview}",
                systemsJson.Length, systemsJson.Length > 3000 ? systemsJson[..3000] : systemsJson);

            var systems = JsonSerializer.Deserialize<List<ReflectSystem>>(systemsJson, JsonOptions) ?? new();

            foreach (var system in systems)
            {
                if (system.Id == null) continue;

                try
                {
                    var itemsRequest = CreateRequest(HttpMethod.Get, config, $"systems/{system.Id}/items");
                    var itemsResponse = await _httpClient.SendAsync(itemsRequest, cancellationToken);
                    itemsResponse.EnsureSuccessStatusCode();

                    var itemsJson = await itemsResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogDebug("Q-SYS Reflect items for system {SystemId} ({Length} chars): {Preview}",
                        system.Id, itemsJson.Length, itemsJson.Length > 3000 ? itemsJson[..3000] : itemsJson);

                    var items = JsonSerializer.Deserialize<List<ReflectItem>>(itemsJson, JsonOptions) ?? new();

                    foreach (var item in items)
                    {
                        var pd = MapItemToPlatformDevice(item, system);
                        if (seenIds.Add(pd.PlatformDeviceId))
                            devices.Add(pd);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch items for Q-SYS system {SystemId} ({SystemName})", system.Id, system.Name);
                }
            }

            _logger.LogInformation("Synced {Count} devices from Q-SYS Reflect ({Cores} cores, {Items} items)",
                devices.Count, cores.Count, devices.Count - cores.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync device inventory from Q-SYS Reflect");
            throw;
        }

        return devices;
    }

    public async Task<PlatformDeviceState> GetDeviceStateAsync(PlatformConnectionConfig config, string platformDeviceId, CancellationToken cancellationToken = default)
    {
        var state = new PlatformDeviceState { PlatformDeviceId = platformDeviceId };

        try
        {
            if (platformDeviceId.StartsWith("core-"))
            {
                var coreId = platformDeviceId["core-".Length..];
                var request = CreateRequest(HttpMethod.Get, config, $"cores/{Uri.EscapeDataString(coreId)}");
                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var core = JsonSerializer.Deserialize<ReflectCore>(json, JsonOptions);
                if (core != null)
                {
                    state.IsOnline = IsStatusOnline(core.Status?.Code);
                    state.StateData = new Dictionary<string, object>
                    {
                        ["statusCode"] = core.Status?.Code ?? "unknown",
                        ["firmware"] = core.FirmwareVersion ?? "",
                        ["model"] = core.Model ?? ""
                    };
                }
            }
            // Item state retrieval would require knowing the system ID; return basic state
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get device state for Q-SYS device {DeviceId}", platformDeviceId);
            state.StateData["error"] = ex.Message;
        }

        return state;
    }

    /// <summary>
    /// Q-SYS Reflect does not expose a screenshot/thumbnail endpoint.
    /// </summary>
    public Task<byte[]?> GetScreenshotAsync(PlatformConnectionConfig config, string platformDeviceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<byte[]?>(null);
    }

    public PlatformCapabilities GetCapabilities()
    {
        return new PlatformCapabilities
        {
            CanListDevices = true,
            CanGetDeviceState = true,
            CanGetScreenshot = false,
            CanDeployContent = false,
            CanRebootDevice = false,
            CanPowerControl = false,
            SupportedCommands = new List<string>()
        };
    }

    public Task<CommandResult> ExecuteCommandAsync(PlatformConnectionConfig config, string platformDeviceId, string commandId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CommandResult
        {
            CommandId = commandId,
            RoutedVia = "cloud-platform:qsys-reflect",
            ErrorMessage = "Command execution is not supported by the Q-SYS Reflect connector"
        });
    }

    public Task<IList<PlatformFolder>> SyncFolderInventoryAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default)
        => Task.FromResult<IList<PlatformFolder>>(Array.Empty<PlatformFolder>());

    public async Task<bool> TestConnectionAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = CreateRequest(HttpMethod.Get, config, "cores");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Q-SYS Reflect connection test failed for {BaseUrl}", GetBaseUrl(config));
            return false;
        }
    }

    #region Status Mapping

    internal static bool IsStatusOnline(string? statusMessage) =>
        statusMessage != null && !string.Equals(statusMessage, "Fault", StringComparison.OrdinalIgnoreCase);

    internal static string MapStatusToReadingStatus(string? statusMessage) =>
        statusMessage?.ToLowerInvariant() switch
        {
            "ok" or "normal" or "running" => "healthy",
            "warning" or "compromised redundancy" => "warning",
            "fault" or "error" or "missing required fields." => "critical",
            _ => "unknown"
        };

    #endregion

    #region Helpers

    private static HttpRequestMessage CreateRequest(HttpMethod method, PlatformConnectionConfig config, string relativePath)
    {
        var baseUrl = GetBaseUrl(config);
        var request = new HttpRequestMessage(method, $"{baseUrl}/{relativePath}");

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string GetBaseUrl(PlatformConnectionConfig config) =>
        !string.IsNullOrEmpty(config.BaseUrl) ? config.BaseUrl.TrimEnd('/') : DefaultBaseUrl;

    private PlatformDevice MapCoreToPlatformDevice(ReflectCore core)
    {
        var device = new PlatformDevice
        {
            PlatformDeviceId = $"core-{core.Id}",
            DeviceName = core.Name ?? $"Core {core.Id}",
            Manufacturer = "QSC",
            DeviceModel = core.Model,
            SerialNumber = core.SerialNumber,
            PlatformMetadata = new Dictionary<string, object>
            {
                ["type"] = "Core",
                ["statusCode"] = core.Status?.Message ?? "unknown",
                ["firmware"] = core.FirmwareVersion ?? "",
                ["siteName"] = core.Site?.Name ?? "",
                ["designName"] = core.Design?.Name ?? ""
            }
        };

        if (core.NetworkConfig?.Interfaces != null)
        {
            var firstIf = core.NetworkConfig.Interfaces.FirstOrDefault();
            if (firstIf != null)
            {
                device.IpAddress = firstIf.IpAddress;
                device.MacAddress = firstIf.MacAddress;
            }
        }

        if (core.Uptime != null)
            device.PlatformMetadata["uptime"] = core.Uptime;
        if (core.Redundancy != null)
            device.PlatformMetadata["redundancy"] = core.Redundancy;

        return device;
    }

    private PlatformDevice MapItemToPlatformDevice(ReflectItem item, ReflectSystem system)
    {
        var device = new PlatformDevice
        {
            PlatformDeviceId = $"item-{item.Id}",
            DeviceName = item.Name ?? $"Item {item.Id}",
            Manufacturer = item.Manufacturer,
            DeviceModel = item.Model,
            SerialNumber = item.SerialNumber,
            PlatformMetadata = new Dictionary<string, object>
            {
                ["type"] = item.Type ?? "Unknown",
                ["statusCode"] = item.Status?.Message ?? "unknown",
                ["isOnline"] = item.IsOnline ?? false,
                ["systemName"] = system.Name ?? "",
                ["designName"] = system.Design?.Name ?? "",
                ["siteName"] = system.Site?.Name ?? "",
                ["location"] = item.Location ?? ""
            }
        };

        if (item.NetworkConfig?.Interfaces != null)
        {
            var firstIf = item.NetworkConfig.Interfaces.FirstOrDefault();
            if (firstIf != null)
            {
                device.IpAddress = firstIf.IpAddress;
                device.MacAddress = firstIf.MacAddress;
            }
        }

        if (!string.IsNullOrEmpty(system.CoreName))
            device.PlatformMetadata["coreName"] = system.CoreName;

        return device;
    }

    #endregion

    #region Internal DTOs

    private class ReflectCore
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("serialNumber")]
        public string? SerialNumber { get; set; }

        [JsonPropertyName("firmwareVersion")]
        public string? FirmwareVersion { get; set; }

        [JsonPropertyName("status")]
        public ReflectStatus? Status { get; set; }

        [JsonPropertyName("networkConfig")]
        public ReflectNetworkConfig? NetworkConfig { get; set; }

        [JsonPropertyName("site")]
        public ReflectSite? Site { get; set; }

        [JsonPropertyName("design")]
        public ReflectDesign? Design { get; set; }

        [JsonPropertyName("uptime")]
        public string? Uptime { get; set; }

        [JsonPropertyName("redundancy")]
        public string? Redundancy { get; set; }
    }

    private class ReflectSystem
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("coreName")]
        public string? CoreName { get; set; }

        [JsonPropertyName("site")]
        public ReflectSite? Site { get; set; }

        [JsonPropertyName("design")]
        public ReflectDesign? Design { get; set; }
    }

    private class ReflectItem
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("manufacturer")]
        public string? Manufacturer { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("serialNumber")]
        public string? SerialNumber { get; set; }

        [JsonPropertyName("isOnline")]
        public bool? IsOnline { get; set; }

        [JsonPropertyName("status")]
        public ReflectStatus? Status { get; set; }

        [JsonPropertyName("networkConfig")]
        public ReflectNetworkConfig? NetworkConfig { get; set; }
    }

    private class ReflectStatus
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private class ReflectNetworkConfig
    {
        [JsonPropertyName("interfaces")]
        public List<ReflectNetworkInterface>? Interfaces { get; set; }
    }

    private class ReflectNetworkInterface
    {
        [JsonPropertyName("ipAddress")]
        public string? IpAddress { get; set; }

        [JsonPropertyName("macAddress")]
        public string? MacAddress { get; set; }
    }

    private class ReflectSite
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class ReflectDesign
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    #endregion
}
