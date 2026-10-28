using DXSuite.Shared.Services;

namespace DXSuite.Director.Services.Pixera;

/// <summary>
/// Cloud connector for on-prem Pixera AV servers, connected via direct IP (JSON-RPC port 1400).
/// Supports resource folder discovery, content deployment, and device inventory/health:
/// Pixera.LiveSystems enumerates every physical machine in the project (director plus
/// multi-user client servers), which sync into the Director device list and back the
/// monitoring service's health probes.
/// </summary>
public class PixeraConnector : ICloudPlatformConnector
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PixeraConnector> _logger;

    public string PlatformId => "pixera";
    public string DisplayName => "Pixera";

    public PixeraConnector(IHttpClientFactory httpClientFactory, ILogger<PixeraConnector> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public PlatformCapabilities GetCapabilities() => new()
    {
        CanListDevices = true,
        CanGetDeviceState = true,
        CanGetScreenshot = false,
        CanDeployContent = true,
        CanRebootDevice = false,
        CanPowerControl = false,
        ImportableResources = [new("folders", "Resource Folders"), new("layers", "Composition Layers"), new("timelines", "Timelines")]
    };

    public async Task<bool> TestConnectionAsync(
        PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            // Short timeout so the UI button doesn't hang if the server is unreachable
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            var client = BuildApiClient(config);
            return await client.GetApiVersionAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pixera connection test failed for {BaseUrl}", config.BaseUrl);
            return false;
        }
    }

    public async Task<IList<PlatformFolder>> SyncFolderInventoryAsync(
        PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        // Cap folder inventory at 30s so the UI sync button doesn't hang indefinitely
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var client = BuildApiClient(config);
        return await client.ListResourceFoldersAsync(cts.Token);
    }

    public async Task<IList<PlatformDevice>> SyncDeviceInventoryAsync(
        PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var systems = await BuildApiClient(config).GetLiveSystemsAsync(cts.Token);
        return systems.Select(s => new PlatformDevice
        {
            // The system name is the stable identity within a Pixera project;
            // IP and MAC can change with network configuration.
            PlatformDeviceId = s.Name,
            DeviceName = s.Name,
            IpAddress = s.Ip,
            MacAddress = s.MacAddress,
            Manufacturer = "AV Stumpfl",
            DeviceModel = s.IsMultiUserMember ? "Pixera Multi-User Member" : "Pixera Live System",
            PlatformMetadata = new Dictionary<string, object>
            {
                ["state"] = s.State ?? "",
                ["isMultiUserMember"] = s.IsMultiUserMember,
            },
        }).ToList();
    }

    public async Task<PlatformDeviceState> GetDeviceStateAsync(
        PlatformConnectionConfig config, string platformDeviceId, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var systems = await BuildApiClient(config).GetLiveSystemsAsync(cts.Token);
        var match = systems.FirstOrDefault(s =>
            string.Equals(s.Name, platformDeviceId, StringComparison.OrdinalIgnoreCase));

        return new PlatformDeviceState
        {
            PlatformDeviceId = platformDeviceId,
            // The state vocabulary is undocumented; presence with a readable state
            // counts as online, and the raw string rides along for diagnostics.
            IsOnline = match != null && !string.IsNullOrEmpty(match.State),
            LastSeen = match != null ? DateTime.UtcNow : null,
            StateData = new Dictionary<string, object> { ["state"] = match?.State ?? "unreachable" },
        };
    }

    public Task<byte[]?> GetScreenshotAsync(
        PlatformConnectionConfig config, string platformDeviceId, CancellationToken cancellationToken = default)
        => Task.FromResult<byte[]?>(null);

    public Task<CommandResult> ExecuteCommandAsync(
        PlatformConnectionConfig config, string platformDeviceId, string commandId,
        Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new CommandResult
        {
            Success = false,
            CommandId = commandId,
            ErrorMessage = "Pixera does not support remote commands."
        });

    /// <summary>
    /// Enumerates all timelines and their layers from Pixera, returning them as PlatformFolder records.
    /// Used by CloudConnectorsTab to populate the layer dropdown in the channel Settings modal.
    /// Not on ICloudPlatformConnector — Pixera-specific capability.
    /// </summary>
    public async Task<IList<PlatformFolder>> SyncLayerInventoryAsync(
        PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        return await BuildApiClient(config).ListLayersAsync(cts.Token);
    }

    /// <summary>
    /// Enumerates timeline names from Pixera. Used by CloudConnectorsTab to import
    /// timelines as content channel rows for the timeline watchdog.
    /// Not on ICloudPlatformConnector — Pixera-specific capability.
    /// </summary>
    public async Task<IList<string>> SyncTimelineInventoryAsync(
        PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        return await BuildApiClient(config).ListTimelineNamesAsync(cts.Token);
    }

    internal PixeraApiClient BuildApiClient(PlatformConnectionConfig config)
    {
        // BaseUrl is the full address including protocol and port (e.g. http://192.168.1.100:1400)
        var rpcUrl = config.BaseUrl.TrimEnd('/') + "/jsonrpc";
        return new PixeraApiClient(rpcUrl, _httpClientFactory.CreateClient(), _logger);
    }
}
