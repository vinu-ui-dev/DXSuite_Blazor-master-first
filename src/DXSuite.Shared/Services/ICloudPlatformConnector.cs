namespace DXSuite.Shared.Services;

/// <summary>
/// Interface for cloud-hosted CMS and device management platform connectors.
/// Each connector handles device discovery, monitoring, and (optionally) control
/// through a cloud platform's API (Omnivex Ink, Scala, Four Winds, Broadsign, etc.)
/// </summary>
public interface ICloudPlatformConnector
{
    string PlatformId { get; }
    string DisplayName { get; }

    Task<IList<PlatformDevice>> SyncDeviceInventoryAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default);
    Task<PlatformDeviceState> GetDeviceStateAsync(PlatformConnectionConfig config, string platformDeviceId, CancellationToken cancellationToken = default);
    Task<byte[]?> GetScreenshotAsync(PlatformConnectionConfig config, string platformDeviceId, CancellationToken cancellationToken = default);

    PlatformCapabilities GetCapabilities();
    Task<CommandResult> ExecuteCommandAsync(PlatformConnectionConfig config, string platformDeviceId, string commandId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default);
    Task<IList<PlatformFolder>> SyncFolderInventoryAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registry for cloud platform connectors, resolved by platform ID.
/// </summary>
public interface ICloudPlatformConnectorRegistry
{
    ICloudPlatformConnector? GetConnector(string platformId);
    IReadOnlyCollection<string> GetRegisteredPlatforms();
    Task<PlatformConnectionConfig?> ResolveConfigAsync(string platformId);
}

public class PlatformConnectionConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthClientSecret { get; set; }
    public Dictionary<string, string> AdditionalSettings { get; set; } = new();
}

public class PlatformDevice
{
    public string PlatformDeviceId { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public string? SerialNumber { get; set; }
    public string? IpAddress { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string? DeviceModel { get; set; }
    public string? Manufacturer { get; set; }
    public string? BrowserType { get; set; }
    public string? BrowserVersion { get; set; }
    public Dictionary<string, object> PlatformMetadata { get; set; } = new();
}

public class PlatformDeviceState
{
    public string PlatformDeviceId { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
    public Dictionary<string, object> StateData { get; set; } = new();
}

public class PlatformFolder
{
    public string FolderId { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string? ParentFolderId { get; set; }
}

public class PlatformCapabilities
{
    public bool CanListDevices { get; set; }
    public bool CanGetDeviceState { get; set; }
    public bool CanGetScreenshot { get; set; }
    public bool CanDeployContent { get; set; }
    public bool CanRebootDevice { get; set; }
    public bool CanPowerControl { get; set; }
    public IList<string> SupportedCommands { get; set; } = new List<string>();
    public IList<ImportableResourceType> ImportableResources { get; set; } = new List<ImportableResourceType>();
}

public class ImportableResourceType
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public ImportableResourceType() { }

    public ImportableResourceType(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}

public class CommandResult
{
    public bool Success { get; set; }
    public string CommandId { get; set; } = string.Empty;
    public string? RoutedVia { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? ResultData { get; set; }
}
