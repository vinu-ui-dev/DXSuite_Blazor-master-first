using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DXSuite.Director.Entities; // TODO: Refactor to use IDeviceDataProvider instead of Director entities
using DXSuite.Director.Services;
using DXSuite.Director.Services;
using DXSuite.Shared.Entities.Identity;
using DXSuite.Connectors.Services.Connectors;
using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Gateway;
using DXSuite.Shared.Services;

namespace DXSuite.Sentinel.Services;

/// <summary>
/// Background service that periodically syncs cloud platform connectors, runs configurable
/// probe cascades against cloud-managed devices, and probes Director devices with stale heartbeats.
/// </summary>
public class CloudConnectorSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CloudConnectorSyncService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly ICloudPlatformConnectorRegistry _connectorRegistry;
    private readonly ConnectorConfigProtector _configProtector;
    private readonly IGatewayConnectionManager _gatewayManager;
    private readonly ISentinelAvailabilityService _sentinelAvailability;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HeartbeatStaleThreshold = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Probe results for Director devices with stale heartbeats.
    /// Configuration.razor reads this to show probe data for Director devices.
    /// </summary>
    public static ConcurrentDictionary<string, ProbeResult> DirectorProbeResults { get; } = new();

    public CloudConnectorSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<CloudConnectorSyncService> logger,
        IDistributedLockService lockService,
        ICloudPlatformConnectorRegistry connectorRegistry,
        ConnectorConfigProtector configProtector,
        IGatewayConnectionManager gatewayManager,
        ISentinelAvailabilityService sentinelAvailability)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _lockService = lockService;
        _connectorRegistry = connectorRegistry;
        _configProtector = configProtector;
        _gatewayManager = gatewayManager;
        _sentinelAvailability = sentinelAvailability;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CloudConnectorSyncService started");

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var distLock = await _lockService.TryAcquireLockAsync(
                    "cloud-connector-sync-lock",
                    TimeSpan.FromMinutes(5),
                    stoppingToken);

                if (distLock != null)
                {
                    await RunSyncCycleAsync(stoppingToken);
                }
                else
                {
                    _logger.LogDebug("Another instance is handling cloud connector sync, skipping this cycle");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CloudConnectorSyncService sync cycle");
            }

            try
            {
                await Task.Delay(SyncInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("CloudConnectorSyncService stopped");
    }

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var directorDb = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>(); // TODO: Refactor to use IDeviceDataProvider instead of Director entities
        var sentinelDb = scope.ServiceProvider.GetRequiredService<ISentinelDBContext>();
        var probeService = scope.ServiceProvider.GetRequiredService<ProbeExecutionService>();

        // Phase 1: Sync cloud platform connectors (always runs — content-only connectors work without Sentinel)
        await SyncCloudConnectorsAsync(directorDb, sentinelDb, probeService, ct);

        // Phases 2+3 require Sentinel DB
        if (_sentinelAvailability.IsAvailable)
        {
            // Phase 2: Sync Director/Engage devices into Sentinel as DeviceInstances
            await SyncDirectorDevicesAsync(scope, sentinelDb, ct);

            // Phase 3: Probe Director devices with stale heartbeats
            await ProbeStaleDirectorDevicesAsync(scope, sentinelDb, probeService, ct);
        }
    }

    #region Phase 1: Cloud Connector Sync + Probes

    private async Task SyncCloudConnectorsAsync(IDirectorDBContext directorDb, ISentinelDBContext sentinelDb, ProbeExecutionService probeService, CancellationToken ct)
    {
        List<TblCloudPlatformConnector> enabledConnectors;
        try
        {
            enabledConnectors = await directorDb.TblCloudPlatformConnectors
                .Where(c => c.IsEnabled)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Director DB not available for cloud connector sync: {Message}", ex.Message);
            return;
        }

        if (enabledConnectors.Count == 0)
            return;

        foreach (var conn in enabledConnectors)
        {
            try
            {
                await SyncSingleConnectorAsync(directorDb, sentinelDb, probeService, conn, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                                 or System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(ex, "Auth failure syncing connector {ConnectorId} ({DisplayName})", conn.ConnectorId, conn.DisplayName);

                try
                {
                    conn.LastSyncAt = DateTime.UtcNow;
                    conn.LastSyncStatus = "auth-failed";
                    conn.LastSyncMessage = $"Authentication failed ({(int)ex.StatusCode}): {ex.Message}";
                    await directorDb.SaveChangesAsync(ct);

                    await NotifyAuthFailureAsync("connector", conn.ConnectorId, conn.DisplayName, ct);
                }
                catch (Exception inner)
                {
                    _logger.LogDebug(inner, "Failed to record auth failure for connector {ConnectorId}", conn.ConnectorId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing connector {ConnectorId} ({DisplayName})", conn.ConnectorId, conn.DisplayName);

                try
                {
                    conn.LastSyncAt = DateTime.UtcNow;
                    conn.LastSyncStatus = "failed";
                    conn.LastSyncMessage = ex.Message;
                    await directorDb.SaveChangesAsync(ct);
                }
                catch { /* best effort */ }
            }
        }
    }

    private async Task SyncSingleConnectorAsync(
        IDirectorDBContext directorDb, ISentinelDBContext sentinelDb, ProbeExecutionService probeService,
        TblCloudPlatformConnector conn, CancellationToken ct)
    {
        var platform = _connectorRegistry.GetConnector(conn.PlatformId);
        if (platform == null)
        {
            _logger.LogWarning("No connector implementation for platform '{PlatformId}'", conn.PlatformId);
            return;
        }

        var config = DeserializeConfig(conn.ConfigJson) ?? new PlatformConnectionConfig();
        var devices = await platform.SyncDeviceInventoryAsync(config, ct);
        var enabledImports = ParseEnabledImports(conn.EnabledImports);

        int created = 0;
        int updated = 0;

        // Sentinel monitoring path: create/update DeviceInstance + PlatformDeviceLink
        if (conn.HasMonitoringSync && _sentinelAvailability.IsAvailable)
        {
            var descriptor = await sentinelDb.DeviceDescriptors
                .FirstOrDefaultAsync(d => d.DeviceType == conn.PlatformId && d.IsActive, ct);

            if (descriptor == null)
            {
                descriptor = new DeviceDescriptor
                {
                    DeviceType = conn.PlatformId,
                    DisplayName = platform.DisplayName,
                    Category = "cloud-managed",
                    IsActive = true,
                    CreateDate = DateTime.UtcNow
                };
                sentinelDb.DeviceDescriptors.Add(descriptor);
                await sentinelDb.SaveChangesAsync(ct);
            }

            var existingLinks = await sentinelDb.PlatformDeviceLinks
                .Where(l => l.ConnectorId == conn.ConnectorId)
                .ToListAsync(ct);

            var deviceInstancesToProbe = new List<(DeviceInstance Instance, PlatformDevice PlatformDev)>();

            foreach (var device in devices)
            {
                var existingLink = existingLinks.FirstOrDefault(l => l.PlatformDeviceId == device.PlatformDeviceId);
                DeviceInstance? deviceInstance;

                if (existingLink != null)
                {
                    existingLink.MacAddress = device.MacAddress;
                    existingLink.PlatformMetadataJson = JsonSerializer.Serialize(device.PlatformMetadata);
                    existingLink.LastSyncAt = DateTime.UtcNow;

                    deviceInstance = await sentinelDb.DeviceInstances.FindAsync(new object[] { existingLink.DeviceInstanceId }, ct);
                    if (deviceInstance != null)
                    {
                        deviceInstance.DisplayName = device.DeviceName;
                        deviceInstance.IpAddress = device.IpAddress;
                        if (!string.IsNullOrEmpty(device.MacAddress)) deviceInstance.MacAddress ??= device.MacAddress;
                        deviceInstance.LastEditDate = DateTime.UtcNow;
                        if (!deviceInstance.DescriptorVersionId.HasValue)
                            await TryAutoAssignDeviceModelAsync(sentinelDb, deviceInstance, ct);
                    }
                    updated++;
                }
                else
                {
                    // Strong-key correlation: the same physical device may already exist via another source.
                    deviceInstance = await DeviceIdentity.FindByStrongKeyAsync(sentinelDb, conn.CustomerId, device.MacAddress, ct);
                    if (deviceInstance == null)
                    {
                        deviceInstance = new DeviceInstance
                        {
                            DescriptorId = descriptor.DescriptorId,
                            DisplayName = device.DeviceName,
                            IpAddress = device.IpAddress,
                            MacAddress = device.MacAddress,
                            CustomerId = conn.CustomerId,
                            IsEnabled = true,
                            CreateDate = DateTime.UtcNow
                        };
                        sentinelDb.DeviceInstances.Add(deviceInstance);
                        await sentinelDb.SaveChangesAsync(ct);
                        await TryAutoAssignDeviceModelAsync(sentinelDb, deviceInstance, ct);
                        created++;
                    }
                    else
                    {
                        updated++; // attaching a new cloud angle to an existing canonical device
                    }

                    sentinelDb.PlatformDeviceLinks.Add(new PlatformDeviceLink
                    {
                        ConnectorId = conn.ConnectorId,
                        DeviceInstanceId = deviceInstance.DeviceInstanceId,
                        PlatformDeviceId = device.PlatformDeviceId,
                        MacAddress = device.MacAddress,
                        PlatformMetadataJson = JsonSerializer.Serialize(device.PlatformMetadata),
                        LastSyncAt = DateTime.UtcNow
                    });
                    await sentinelDb.SaveChangesAsync(ct);
                }

                if (deviceInstance != null)
                {
                    // Record the connector's view as a source angle; the rollup recomputes the canonical state.
                    var sourceState = BuildDeviceState(platform, deviceInstance, device, descriptor);
                    await DeviceIdentity.WriteSourceStateAsync(
                        sentinelDb, deviceInstance, conn.CustomerId, DeviceIdentity.SourceCloudConnector,
                        $"{conn.ConnectorId}:{device.PlatformDeviceId}", sourceState);
                    deviceInstancesToProbe.Add((deviceInstance, device));
                }
            }

            // Run probe cascades based on connector's gateway mode
            if (conn.GatewayMode != "none")
            {
                await RunCloudDeviceProbesAsync(sentinelDb, probeService, deviceInstancesToProbe, conn.CustomerId, conn.GatewayMode, ct);
            }

            await sentinelDb.SaveChangesAsync(ct);
        }

        // Content path: deposit devices/folders into Director's tblDevice
        int folderCount = 0;
        if (conn.HasContentSync)
        {
            if (enabledImports == null || enabledImports.Contains("devices"))
            {
                if (conn.HasMonitoringSync && _sentinelAvailability.IsAvailable)
                    await SyncDevicesToDirectorAsync(sentinelDb, conn, devices, ct);
                else
                    await SyncDevicesToDirectorAsync(null, conn, devices, ct);
            }
            if (enabledImports == null || enabledImports.Contains("folders"))
                folderCount = await SyncFoldersToDirectorAsync(conn, platform, config, ct);
        }

        // Ink content source redirection: PATCH UseFileContentsUrls on all displays
        if (platform is OmnivexInkConnector inkConnector)
        {
            var hasLocalUrl = config.AdditionalSettings.TryGetValue("LocalContentBaseUrl", out var localUrl)
                && !string.IsNullOrWhiteSpace(localUrl);

            foreach (var device in devices)
            {
                try
                {
                    await inkConnector.PatchDisplayUseFileContentsUrlsAsync(
                        config, device.PlatformDeviceId, hasLocalUrl, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to PATCH UseFileContentsUrls for Ink display {DisplayId}", device.PlatformDeviceId);
                }
            }
        }

        conn.LastSyncAt = DateTime.UtcNow;
        conn.LastSyncStatus = "success";
        var folderStatus = folderCount switch
        {
            > 0 => $"{folderCount} folders",
            0 => "0 folders",
            _ => "folder sync failed (check logs)"
        };
        conn.LastSyncMessage = conn.HasContentSync
            ? $"Synced {devices.Count} devices ({created} new, {updated} updated), {folderStatus}"
            : $"Synced {devices.Count} devices ({created} new, {updated} updated)";

        await directorDb.SaveChangesAsync(ct);

        _logger.LogInformation("CloudConnectorSyncService: synced connector '{DisplayName}' — {Count} devices ({Created} new, {Updated} updated)",
            conn.DisplayName, devices.Count, created, updated);
    }

    /// <summary>
    /// Deposits cloud-managed devices into Director's tblDevice so they can have
    /// metadata/playlist assignments. Links Sentinel DeviceInstance to Director TblDevice
    /// via DirectorDeviceId.
    /// </summary>
    private async Task SyncDevicesToDirectorAsync(
        ISentinelDBContext? sentinelDb, TblCloudPlatformConnector conn,
        IList<PlatformDevice> platformDevices, CancellationToken ct)
    {
        using var directorScope = _scopeFactory.CreateScope();
        IDirectorDBContext directorDb; // TODO: Refactor to use IDeviceDataProvider instead of Director entities
        try
        {
            directorDb = directorScope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        }
        catch
        {
            _logger.LogDebug("Director DB not available for content sync");
            return;
        }

        // Load existing links to find DeviceInstances that map to platform devices
        Dictionary<string, PlatformDeviceLink> linkLookup;
        if (sentinelDb != null)
        {
            var existingLinks = await sentinelDb.PlatformDeviceLinks
                .Where(l => l.ConnectorId == conn.ConnectorId)
                .ToListAsync(ct);
            linkLookup = existingLinks.ToDictionary(l => l.PlatformDeviceId, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            linkLookup = new Dictionary<string, PlatformDeviceLink>(StringComparer.OrdinalIgnoreCase);
        }

        // Load existing Director devices created by this connector
        var deviceIdPrefix = $"cloud-{conn.ConnectorId}-";
        List<TblDevice> existingDirectorDevices;
        try
        {
            existingDirectorDevices = await directorDb.TblDevices
                .Where(d => d.DeviceId.StartsWith(deviceIdPrefix))
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Director DB query failed for content sync: {Message}", ex.Message);
            return;
        }

        var directorDeviceLookup = existingDirectorDevices.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);

        int directorCreated = 0;
        int directorUpdated = 0;

        foreach (var device in platformDevices)
        {
            var directorDeviceId = $"cloud-{conn.ConnectorId}-{device.PlatformDeviceId}";

            if (directorDeviceLookup.TryGetValue(directorDeviceId, out var existingDevice))
            {
                existingDevice.Description = device.DeviceName;
                existingDevice.Ipaddress = device.IpAddress ?? "";
                existingDevice.Macaddress = device.MacAddress ?? "";
                existingDevice.Manufacturer = device.Manufacturer ?? "";
                existingDevice.Model = device.DeviceModel ?? "";
                existingDevice.SerialNumber = device.SerialNumber ?? "";
                existingDevice.ExtendedData = System.Text.Json.JsonSerializer.Serialize(new
                {
                    sourceType = "cloud-connector",
                    connectorId = conn.ConnectorId,
                    platformId = conn.PlatformId
                });
                directorUpdated++;
            }
            else
            {
                var newDevice = DeviceProvisioning.Seed(new TblDevice
                {
                    DeviceId = directorDeviceId,
                    Description = device.DeviceName,
                    CustomerId = conn.CustomerId.Length > 3 ? conn.CustomerId[..3] : conn.CustomerId,
                    Ipaddress = device.IpAddress ?? "",
                    Macaddress = device.MacAddress ?? "",
                    Manufacturer = device.Manufacturer ?? "",
                    Model = device.DeviceModel ?? "",
                    SerialNumber = device.SerialNumber ?? "",
                    Osversion = "",
                    Heartbeat = DateTime.UtcNow,
                    CreateDate = DateTime.UtcNow,
                    ExtendedData = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        sourceType = "cloud-connector",
                        connectorId = conn.ConnectorId,
                        platformId = conn.PlatformId
                    })
                });
                directorDb.TblDevices.Add(newDevice);
                directorCreated++;
            }

            // Link Sentinel DeviceInstance to Director TblDevice (only when Sentinel is available)
            if (sentinelDb != null && linkLookup.TryGetValue(device.PlatformDeviceId, out var link))
            {
                var deviceInstance = await sentinelDb.DeviceInstances.FindAsync(new object[] { link.DeviceInstanceId }, ct);
                if (deviceInstance != null && deviceInstance.DirectorDeviceId != directorDeviceId)
                {
                    deviceInstance.DirectorDeviceId = directorDeviceId;
                }
            }
        }

        try
        {
            await directorDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Director devices for connector {ConnectorId}", conn.ConnectorId);
            return;
        }

        if (directorCreated > 0 || directorUpdated > 0)
        {
            _logger.LogInformation("Content sync for connector '{DisplayName}': {Created} created, {Updated} updated in Director",
                conn.DisplayName, directorCreated, directorUpdated);
        }
    }

    /// <summary>
    /// Syncs Ink folder inventory into Director's tblDevice as virtual folder entries.
    /// Folders can be assigned to Locations/Displays for content sync.
    /// </summary>
    private async Task<int> SyncFoldersToDirectorAsync(
        TblCloudPlatformConnector conn, ICloudPlatformConnector platform,
        PlatformConnectionConfig config, CancellationToken ct)
    {
        try
        {
            var folders = await platform.SyncFolderInventoryAsync(config, ct);
            _logger.LogInformation("SyncFoldersToDirectorAsync: connector '{DisplayName}' returned {Count} folders from {PlatformType}",
                conn.DisplayName, folders.Count, platform.GetType().Name);
            if (folders.Count == 0) return 0;

            using var scope = _scopeFactory.CreateScope();
            IDirectorDBContext directorDb; // TODO: Refactor to use IDeviceDataProvider instead of Director entities
            try
            {
                directorDb = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
            }
            catch
            {
                _logger.LogDebug("Director DB not available for folder sync");
                return 0;
            }

            var prefix = $"inkf-{conn.ConnectorId}-";
            var existingDevices = await directorDb.TblDevices
                .Where(d => d.DeviceId.StartsWith(prefix))
                .ToListAsync(ct);

            var existingByDeviceId = existingDevices.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
            var liveFolderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int created = 0, updated = 0;

            foreach (var folder in folders)
            {
                var deviceId = $"{prefix}{folder.FolderId}";
                liveFolderIds.Add(deviceId);

                if (existingByDeviceId.TryGetValue(deviceId, out var existing))
                {
                    existing.Description = folder.FolderName;
                    // Merge folder metadata into ExtendedData while preserving inkFileMap
                    existing.ExtendedData = MergeInkFolderExtendedData(existing.ExtendedData, conn.ConnectorId, folder);
                    updated++;
                }
                else
                {
                    var newExtData = JsonSerializer.Serialize(new
                    {
                        sourceType = "ink-folder",
                        connectorId = conn.ConnectorId,
                        folderId = folder.FolderId,
                        folderName = folder.FolderName,
                        parentFolderId = folder.ParentFolderId
                    });

                    directorDb.TblDevices.Add(DeviceProvisioning.Seed(new TblDevice // TODO: Refactor to use IDeviceDataProvider instead of Director entities
                    {
                        DeviceId = deviceId,
                        Description = folder.FolderName,
                        CustomerId = conn.CustomerId.Length > 3 ? conn.CustomerId[..3] : conn.CustomerId,
                        Manufacturer = "",
                        Model = "",
                        SerialNumber = "",
                        Ipaddress = "",
                        Macaddress = "",
                        Osversion = "",
                        Heartbeat = DateTime.UtcNow,
                        CreateDate = DateTime.UtcNow,
                        ExtendedData = newExtData
                    }));
                    created++;
                }
            }

            // Remove entries for folders that no longer exist in Ink
            var stale = existingDevices.Where(d => !liveFolderIds.Contains(d.DeviceId)).ToList();
            foreach (var s in stale)
            {
                directorDb.TblDevices.Remove(s);
            }

            if (created > 0 || updated > 0 || stale.Count > 0)
            {
                await directorDb.SaveChangesAsync(ct);
                _logger.LogInformation("Folder sync for connector '{DisplayName}': {Created} created, {Updated} updated, {Removed} removed",
                    conn.DisplayName, created, updated, stale.Count);
            }

            return folders.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync folders for connector {ConnectorId}: {Message}", conn.ConnectorId, ex.Message);
            return -1;
        }
    }

    /// <summary>
    /// Rebuilds the folder device's ExtendedData with current folder metadata
    /// while preserving the inkFileMap used by InkContentSyncService for file tracking.
    /// </summary>
    private static string MergeInkFolderExtendedData(string? existingExtData, int connectorId, PlatformFolder folder)
    {
        // Preserve inkFileMap from existing ExtendedData if present
        JsonElement? inkFileMap = null;
        if (!string.IsNullOrEmpty(existingExtData))
        {
            try
            {
                using var doc = JsonDocument.Parse(existingExtData);
                if (doc.RootElement.TryGetProperty("inkFileMap", out var mapProp))
                    inkFileMap = mapProp.Clone();
            }
            catch { }
        }

        var dict = new Dictionary<string, object>
        {
            ["sourceType"] = "ink-folder",
            ["connectorId"] = connectorId,
            ["folderId"] = folder.FolderId,
            ["folderName"] = folder.FolderName
        };

        if (folder.ParentFolderId != null)
            dict["parentFolderId"] = folder.ParentFolderId;

        if (inkFileMap.HasValue)
            dict["inkFileMap"] = inkFileMap.Value;

        return JsonSerializer.Serialize(dict);
    }

    private async Task RunCloudDeviceProbesAsync(
        ISentinelDBContext sentinelDb, ProbeExecutionService probeService,
        List<(DeviceInstance Instance, PlatformDevice PlatformDev)> devices,
        string customerId, string gatewayMode, CancellationToken ct)
    {
        // Resolve probe strategies sequentially to avoid concurrent DbContext access
        var devicesToProbe = devices.Where(d => !string.IsNullOrEmpty(d.Instance.IpAddress)).ToList();
        var strategies = new List<(DeviceInstance Instance, ResolvedProbeStrategy Strategy)>();
        foreach (var d in devicesToProbe)
        {
            var strategy = await probeService.ResolveStrategyAsync(
                d.Instance.DeviceInstanceId,
                d.PlatformDev.Manufacturer,
                d.PlatformDev.DeviceModel,
                customerId, sentinelDb);
            strategies.Add((d.Instance, strategy));
        }

        // Run network probes in parallel (no DbContext access)
        var probeTasks = strategies.Select(async s =>
        {
            ProbeResult result;

            if (gatewayMode == "remote" && s.Instance.SiteGatewayId.HasValue)
            {
                // Route probe through the assigned gateway
                result = await probeService.ExecuteProbeViaGatewayAsync(
                    s.Instance.SiteGatewayId.Value, s.Instance.IpAddress!, s.Strategy, _gatewayManager, ct)
                    ?? await probeService.ExecuteProbeAsync(s.Instance.IpAddress!, s.Strategy, ct);
            }
            else
            {
                // Built-in mode, or remote mode with no gateway assigned (Local Director)
                result = await probeService.ExecuteProbeAsync(s.Instance.IpAddress!, s.Strategy, ct);
            }

            result.Trigger = "scheduled";
            return (Device: s.Instance, Result: result);
        }).ToList();

        var results = await Task.WhenAll(probeTasks);

        foreach (var (device, probeResult) in results)
        {
            // Record the gateway probe as its own source angle (no longer clobbers the canonical blob).
            var resultJson = ProbeExecutionService.SerializeResult(probeResult);
            var probeState = new DeviceState
            {
                DeviceInstanceId = device.DeviceInstanceId,
                DisplayName = device.DisplayName,
                IpAddress = device.IpAddress,
                OverallHealth = probeResult.Overall != "online"
                    ? OverallHealthStatus.Offline
                    : probeResult.Color switch
                    {
                        "green" => OverallHealthStatus.Healthy,
                        "yellow" => OverallHealthStatus.Warning,
                        _ => OverallHealthStatus.Critical
                    },
                StateTimestamp = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow,
                DataSource = new DataSourceInfo { HasGateway = true, PrimarySource = "gateway-probe", LastGatewayData = DateTime.UtcNow },
                MonitorReadings = new Dictionary<string, MonitorReading>
                {
                    ["direct-probe"] = new MonitorReading
                    {
                        MonitorPointId = "direct-probe",
                        Name = "Direct Network Probe",
                        Value = resultJson,
                        DisplayValue = probeResult.Overall == "online"
                            ? (probeResult.Color == "yellow" ? "Degraded" : "Online")
                            : "Offline",
                        Status = probeResult.Color switch
                        {
                            "green" => "healthy",
                            "yellow" => "warning",
                            _ => "critical"
                        },
                        Timestamp = DateTime.UtcNow
                    }
                }
            };
            await DeviceIdentity.WriteSourceStateAsync(
                sentinelDb, device, device.CustomerId, DeviceIdentity.SourceGateway,
                device.SiteGatewayId?.ToString() ?? "direct", probeState);

            // Write probe history
            try
            {
                sentinelDb.ProbeHistories.Add(new ProbeHistory
                {
                    DeviceInstanceId = device.DeviceInstanceId,
                    ProbeTimestamp = probeResult.Timestamp,
                    OverallResult = probeResult.Overall,
                    StatusColor = probeResult.Color,
                    ProfileName = probeResult.ProfileName,
                    ResultJson = ProbeExecutionService.SerializeResult(probeResult),
                    CustomerId = customerId,
                    CreateDate = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to write probe history for device {DeviceId}", device.DeviceInstanceId);
            }
        }
    }

    #endregion

    #region Phase 2: Director Device Sync into Sentinel

    /// <summary>
    /// Syncs real Engage players from the Director device table into Sentinel as DeviceInstance
    /// records (a "heartbeat" source angle). Excludes Ink content folders — the Ink content-sync
    /// writes folder rows into the device table with DeviceId "inkf-...", which are not monitorable
    /// devices. Resolves to a canonical device by DirectorDeviceId or strong-key MAC match.
    /// </summary>
    private async Task SyncDirectorDevicesAsync(IServiceScope scope, ISentinelDBContext sentinelDb, CancellationToken ct)
    {
        IDirectorDBContext directorDb; // TODO: Refactor to use IDeviceDataProvider instead of Director entities
        try
        {
            directorDb = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        }
        catch
        {
            return;
        }

        List<TblDevice> directorDevices;
        try
        {
            // Engage players only — skip Ink content folders (DeviceId "inkf-...") that the Ink
            // content-sync deposits in the device table; those are not monitorable devices.
            directorDevices = await directorDb.TblDevices
                .Where(d => !d.DeviceId.StartsWith("inkf-"))
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Director DB not available for device sync: {Message}", ex.Message);
            return;
        }

        if (directorDevices.Count == 0)
            return;

        // Find or create the "director-engage" platform descriptor
        var descriptor = await sentinelDb.DeviceDescriptors
            .FirstOrDefaultAsync(d => d.DeviceType == "director-engage" && d.IsActive, ct);

        if (descriptor == null)
        {
            descriptor = new DeviceDescriptor
            {
                DeviceType = "director-engage",
                DisplayName = "Engage",
                Category = "director-managed",
                IsActive = true,
                CreateDate = DateTime.UtcNow
            };
            sentinelDb.DeviceDescriptors.Add(descriptor);
            await sentinelDb.SaveChangesAsync(ct);
        }

        // Load existing Sentinel instances linked to Director devices
        var existingInstances = await sentinelDb.DeviceInstances
            .Where(d => d.DirectorDeviceId != null)
            .ToListAsync(ct);

        var instanceLookup = existingInstances.ToDictionary(d => d.DirectorDeviceId!, StringComparer.OrdinalIgnoreCase);

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var customerId = configuration["CustomerInfo:CustomerCode"] ?? "";

        int created = 0;
        int updated = 0;

        foreach (var device in directorDevices)
        {
            // One tenant per Director DB: every Engage player belongs to this host's customer.
            // The per-device CustomerId is a legacy RIADirector multi-customer artifact (carries
            // stale values like "RAD") and must not leak into Sentinel's customer scope.
            var directorCustomer = string.IsNullOrEmpty(customerId) ? device.CustomerId : customerId;
            var sourceState = BuildDirectorDeviceState(device);

            var target = instanceLookup.TryGetValue(device.DeviceId, out var existing) ? existing : null;
            if (target == null)
            {
                // Strong-key correlation: this physical device may already exist via a cloud connector (same MAC).
                target = await DeviceIdentity.FindByStrongKeyAsync(sentinelDb, directorCustomer, device.Macaddress, ct);
                if (target == null)
                {
                    target = new DeviceInstance
                    {
                        DescriptorId = descriptor.DescriptorId,
                        DirectorDeviceId = device.DeviceId,
                        DisplayName = device.Description ?? device.DeviceId,
                        IpAddress = device.Ipaddress,
                        MacAddress = device.Macaddress,
                        CustomerId = directorCustomer,
                        IsEnabled = true,
                        LastStateUpdated = DateTime.UtcNow,
                        CreateDate = DateTime.UtcNow,
                        CommissionedDate = device.CreateDate,
                        CommissionedSource = "auto"
                    };
                    sentinelDb.DeviceInstances.Add(target);
                    await sentinelDb.SaveChangesAsync(ct);
                    await TryAutoAssignDeviceModelAsync(sentinelDb, target, ct);
                    created++;
                }
                else
                {
                    target.DirectorDeviceId ??= device.DeviceId; // attach the heartbeat angle to the canonical device
                    updated++;
                }
            }
            else
            {
                updated++;
            }

            target.DisplayName = device.Description ?? device.DeviceId;
            target.CustomerId = directorCustomer; // self-heal rows previously synced under a legacy customer
            if (!string.IsNullOrEmpty(device.Ipaddress)) target.IpAddress = device.Ipaddress;
            if (!string.IsNullOrEmpty(device.Macaddress)) target.MacAddress ??= device.Macaddress;
            target.LastEditDate = DateTime.UtcNow;
            if (!target.DescriptorVersionId.HasValue)
                await TryAutoAssignDeviceModelAsync(sentinelDb, target, ct);

            await DeviceIdentity.WriteSourceStateAsync(
                sentinelDb, target, target.CustomerId, DeviceIdentity.SourceHeartbeat, device.DeviceId, sourceState);
        }

        if (created > 0 || updated > 0)
        {
            _logger.LogInformation("Director device sync: {Total} devices ({Created} new, {Updated} updated)",
                directorDevices.Count, created, updated);
        }
    }

    private static DeviceState BuildDirectorDeviceState(TblDevice device)
    {
        var now = DateTime.UtcNow;
        var isOnline = (now - device.Heartbeat) < HeartbeatStaleThreshold;

        var state = new DeviceState
        {
            DeviceType = "director-engage",
            DisplayName = device.Description ?? device.DeviceId,
            IpAddress = device.Ipaddress,
            OverallHealth = isOnline ? OverallHealthStatus.Healthy : OverallHealthStatus.Offline,
            LastHeartbeat = device.Heartbeat,
            StateTimestamp = now,
            DataSource = new DataSourceInfo
            {
                HasHeartbeat = true,
                PrimarySource = "director-heartbeat",
                LastHeartbeatData = device.Heartbeat
            },
            MonitorReadings = new Dictionary<string, MonitorReading>()
        };

        // For an online player, derive real health from its DirectorClient content telemetry
        // (ExtendedData). Offline players keep Offline — stale telemetry would be misleading.
        if (isOnline)
        {
            var telemetryHealth = EngageTelemetryParser.Apply(state, device.ExtendedData, device.DeviceId, now);
            if (telemetryHealth != OverallHealthStatus.Unknown)
                state.OverallHealth = telemetryHealth;
        }

        if (!string.IsNullOrEmpty(device.Manufacturer))
        {
            state.MonitorReadings["device-manufacturer"] = new MonitorReading
            {
                MonitorPointId = "device-manufacturer",
                Name = "Manufacturer",
                Value = device.Manufacturer,
                DisplayValue = device.Manufacturer,
                Status = "healthy",
                Timestamp = DateTime.UtcNow
            };
        }

        if (!string.IsNullOrEmpty(device.Model))
        {
            state.MonitorReadings["device-model"] = new MonitorReading
            {
                MonitorPointId = "device-model",
                Name = "Model",
                Value = device.Model,
                DisplayValue = device.Model,
                Status = "healthy",
                Timestamp = DateTime.UtcNow
            };
        }

        if (!string.IsNullOrEmpty(device.SerialNumber))
        {
            state.MonitorReadings["device-serial"] = new MonitorReading
            {
                MonitorPointId = "device-serial",
                Name = "Serial Number",
                Value = device.SerialNumber,
                DisplayValue = device.SerialNumber,
                Status = "healthy",
                Timestamp = DateTime.UtcNow
            };
        }

        return state;
    }

    #endregion

    #region Phase 3: Director Stale Heartbeat Probes

    private async Task ProbeStaleDirectorDevicesAsync(
        IServiceScope scope, ISentinelDBContext sentinelDb,
        ProbeExecutionService probeService, CancellationToken ct)
    {
        IDirectorDBContext directorDb; // TODO: Refactor to use IDeviceDataProvider instead of Director entities
        try
        {
            directorDb = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
        }
        catch
        {
            return; // Director DB not available
        }

        var staleThreshold = DateTime.UtcNow - HeartbeatStaleThreshold;

        List<TblDevice> staleDevices;
        try
        {
            staleDevices = await directorDb.TblDevices
                .Where(d => d.Heartbeat < staleThreshold
                    && d.Ipaddress != null
                    && d.Ipaddress != "")
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Director DB not available for stale heartbeat probes: {Message}", ex.Message);
            return;
        }

        if (staleDevices.Count == 0)
        {
            // Clear any previous results for devices that are no longer stale
            DirectorProbeResults.Clear();
            return;
        }

        _logger.LogDebug("Probing {Count} Director devices with stale heartbeats", staleDevices.Count);

        // Determine customer ID from config
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var customerId = configuration["CustomerInfo:CustomerCode"] ?? "";

        // Resolve probe strategies sequentially to avoid concurrent DbContext access
        var deviceStrategies = new List<(TblDevice Device, ResolvedProbeStrategy Strategy)>();
        foreach (var device in staleDevices)
        {
            var strategy = await probeService.ResolveStrategyAsync(
                0, device.Manufacturer, device.Model, customerId, sentinelDb);
            deviceStrategies.Add((device, strategy));
        }

        // Run network probes in parallel (no DbContext access)
        var probeTasks = deviceStrategies.Select(async ds =>
        {
            var result = await probeService.ExecuteProbeAsync(ds.Device.Ipaddress!, ds.Strategy, ct);
            result.Trigger = "heartbeat-stale";
            return (Device: ds.Device, Result: result);
        }).ToList();

        var results = await Task.WhenAll(probeTasks);

        // Update the shared dictionary and write history
        var activeDeviceIds = new HashSet<string>();
        foreach (var (device, probeResult) in results)
        {
            DirectorProbeResults[device.DeviceId] = probeResult;
            activeDeviceIds.Add(device.DeviceId);

            try
            {
                sentinelDb.ProbeHistories.Add(new ProbeHistory
                {
                    DirectorDeviceId = device.DeviceId,
                    ProbeTimestamp = probeResult.Timestamp,
                    OverallResult = probeResult.Overall,
                    StatusColor = probeResult.Color,
                    ProfileName = probeResult.ProfileName,
                    ResultJson = ProbeExecutionService.SerializeResult(probeResult),
                    CustomerId = customerId,
                    CreateDate = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to write probe history for Director device {DeviceId}", device.DeviceId);
            }
        }

        // Remove results for devices that are no longer stale
        foreach (var key in DirectorProbeResults.Keys)
        {
            if (!activeDeviceIds.Contains(key))
                DirectorProbeResults.TryRemove(key, out _);
        }

        try
        {
            await sentinelDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to save probe history for Director devices");
        }
    }

    #endregion

    #region Helpers

    // TODO: Restore per-connector BuildDeviceState once connectors implement ISentinelAwareConnector
    private static DeviceState BuildDeviceState(ICloudPlatformConnector connector, DeviceInstance deviceInstance, PlatformDevice platformDevice, DeviceDescriptor descriptor)
    {
        var state = new DeviceState
        {
            DeviceInstanceId = deviceInstance.DeviceInstanceId,
            DeviceType = descriptor.DeviceType,
            DisplayName = platformDevice.DeviceName,
            IpAddress = platformDevice.IpAddress,
            OverallHealth = OverallHealthStatus.Unknown,
            StateTimestamp = DateTime.UtcNow,
            DataSource = new DataSourceInfo
            {
                HasCloudPlatform = true,
                PrimarySource = connector.PlatformId,
                LastPlatformData = DateTime.UtcNow
            },
            MonitorReadings = new Dictionary<string, MonitorReading>
            {
                ["device-manufacturer"] = new MonitorReading
                {
                    MonitorPointId = "device-manufacturer",
                    Name = "Manufacturer",
                    Value = platformDevice.Manufacturer ?? "",
                    DisplayValue = platformDevice.Manufacturer ?? "Unknown",
                    Status = "healthy",
                    Timestamp = DateTime.UtcNow
                },
                ["device-model"] = new MonitorReading
                {
                    MonitorPointId = "device-model",
                    Name = "Model",
                    Value = platformDevice.DeviceModel ?? "",
                    DisplayValue = platformDevice.DeviceModel ?? "Unknown",
                    Status = "healthy",
                    Timestamp = DateTime.UtcNow
                },
                ["device-serial"] = new MonitorReading
                {
                    MonitorPointId = "device-serial",
                    Name = "Serial Number",
                    Value = platformDevice.SerialNumber ?? "",
                    DisplayValue = platformDevice.SerialNumber ?? "Unknown",
                    Status = "healthy",
                    Timestamp = DateTime.UtcNow
                }
            }
        };
        return state;
    }

    /// <summary>
    /// Auto-assigns a matching Device Model (DescriptorVersionId) to a device based on
    /// manufacturer/model from its state. Make/Model match takes priority over Make-only.
    /// </summary>
    private async Task TryAutoAssignDeviceModelAsync(ISentinelDBContext sentinelDb, DeviceInstance device, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(device.LastStateJson)) return;

            DeviceState? state;
            try { state = JsonSerializer.Deserialize<DeviceState>(device.LastStateJson); }
            catch { return; }
            if (state?.MonitorReadings == null) return;

            var mfr = state.MonitorReadings.TryGetValue("device-manufacturer", out var mfrReading) ? mfrReading.Value?.ToString() : null;
            if (string.IsNullOrEmpty(mfr)) return;

            var model = state.MonitorReadings.TryGetValue("device-model", out var modelReading) ? modelReading.Value?.ToString() : null;

            // Load user-created device models (those with at least one version)
            var candidates = await sentinelDb.DeviceDescriptors
                .AsNoTracking()
                .Include(d => d.DeviceDescriptorVersions)
                .Where(d => d.IsActive && d.DeviceDescriptorVersions.Any(v => v.IsActive))
                .ToListAsync(ct);

            DeviceDescriptor? bestMatch = null;

            // First pass: try Make/Model match (most specific)
            if (!string.IsNullOrEmpty(model))
            {
                bestMatch = candidates.FirstOrDefault(c =>
                    string.Equals(c.Manufacturer, mfr, StringComparison.OrdinalIgnoreCase) &&
                    c.DeviceType.Contains('-') &&
                    c.DeviceType != c.Manufacturer?.ToLower().Replace(" ", "-") &&
                    (c.DisplayName?.Contains(model, StringComparison.OrdinalIgnoreCase) == true ||
                     c.DeviceType.Contains(model.ToLower().Replace(" ", "-"), StringComparison.OrdinalIgnoreCase)));
            }

            // Second pass: fall back to Make-only match
            bestMatch ??= candidates.FirstOrDefault(c =>
                string.Equals(c.Manufacturer, mfr, StringComparison.OrdinalIgnoreCase) &&
                (c.DeviceType == c.Manufacturer?.ToLower().Replace(" ", "-") ||
                 !c.DeviceType.Contains('-')));

            if (bestMatch == null) return;

            var activeVersion = bestMatch.DeviceDescriptorVersions.FirstOrDefault(v => v.IsActive);
            if (activeVersion == null) return;

            device.DescriptorId = bestMatch.DescriptorId;
            device.DescriptorVersionId = activeVersion.VersionId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-assign device model failed for device {DeviceId}", device.DeviceInstanceId);
        }
    }

    /// <summary>
    /// Parses the EnabledImports JSON array. Returns null when all imports are enabled (null input),
    /// otherwise returns the set of enabled resource type IDs.
    /// </summary>
    private static HashSet<string>? ParseEnabledImports(string? enabledImportsJson)
    {
        if (string.IsNullOrEmpty(enabledImportsJson)) return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(enabledImportsJson);
            return list != null ? new HashSet<string>(list, StringComparer.OrdinalIgnoreCase) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Sends a one-time auth failure notification to Admin users, with 24-hour dedup.
    /// Uses its own scope to avoid interfering with the sync's DbContext.
    /// </summary>
    private async Task NotifyAuthFailureAsync(string sourceType, int sourceId, string displayName, CancellationToken ct)
    {
        try
        {
            using var notifyScope = _scopeFactory.CreateScope();
            var db = notifyScope.ServiceProvider.GetRequiredService<IDirectorDBContext>(); // TODO: Refactor to use IDeviceDataProvider instead of Director entities
            var notificationService = notifyScope.ServiceProvider.GetRequiredService<INotificationService>();

            // ReferenceId: connector uses raw ID, LLM provider would use offset (handled in LlmGateway)
            var referenceId = sourceId;
            var since = DateTime.UtcNow.AddHours(-24);

            var alreadyNotified = await db.TblNotifications
                .AsNoTracking()
                .AnyAsync(n => n.NotificationType == "credential-auth-failure"
                            && n.ReferenceId == referenceId
                            && n.CreatedDate >= since, ct);

            if (alreadyNotified) return;

            var adminUserIds = await db.TblUsers
                .AsNoTracking()
                .Where(u => u.Role.RoleName == "Admin")
                .Select(u => u.UserId)
                .ToListAsync(ct);

            if (adminUserIds.Count == 0) return;

            var label = sourceType == "connector" ? "Cloud connector" : "LLM provider";

            await notificationService.CreateBulkNotificationsAsync(
                adminUserIds,
                $"{label} '{displayName}' authentication failed",
                $"The {label.ToLower()} '{displayName}' returned an authentication error. The API key may be expired or revoked.",
                "credential-auth-failure",
                referenceId,
                "/Settings");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send auth failure notification for {SourceType} {SourceId}", sourceType, sourceId);
        }
    }

    private PlatformConnectionConfig? DeserializeConfig(string? configJson)
    {
        return _configProtector.Decrypt(configJson);
    }

    #endregion
}
