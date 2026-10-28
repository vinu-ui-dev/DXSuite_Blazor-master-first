using Microsoft.EntityFrameworkCore;
using DXSuite.Director.Entities;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Connectors.Services.Connectors;

namespace DXSuite.Director.Services.Pixera;

/// <summary>
/// Background service that syncs Director playlist content to assigned Pixera resource folders.
/// Files are delivered via UNC share (FilePaths:BaseUNC); Pixera loads them directly from the path.
/// Runs on a 60-second cycle using a distributed lock to prevent concurrent execution across replicas.
/// </summary>
public class PixeraContentSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PixeraContentSyncService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly ConnectorConfigProtector _configProtector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(35);

    public PixeraContentSyncService(
        IServiceScopeFactory scopeFactory,
        ILogger<PixeraContentSyncService> logger,
        IDistributedLockService lockService,
        ConnectorConfigProtector configProtector,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment env,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _lockService = lockService;
        _configProtector = configProtector;
        _httpClientFactory = httpClientFactory;
        _env = env;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PixeraContentSyncService: starting (delay {Delay}s)", StartupDelay.TotalSeconds);
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                "pixera-content-sync", TimeSpan.FromMinutes(5), stoppingToken);

            if (distLock != null)
            {
                try
                {
                    _logger.LogInformation("PixeraContentSyncService: running sync cycle");
                    await RunSyncCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PixeraContentSyncService: error during sync cycle");
                }
            }
            else
            {
                _logger.LogDebug("Another instance is handling Pixera content sync, skipping this cycle");
            }

            try { await Task.Delay(SyncInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        var uncBasePath = _configuration["FilePaths:BaseUNC"];
        if (string.IsNullOrWhiteSpace(uncBasePath))
        {
            _logger.LogWarning("PixeraContentSyncService: FilePaths:BaseUNC not configured — skipping sync");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var directorDb = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();

        var connectors = await directorDb.TblCloudPlatformConnectors
            .Where(c => c.IsEnabled && c.ContentMode != "monitoring-only" && c.PlatformId == "pixera")
            .ToListAsync(ct);

        _logger.LogInformation("PixeraContentSyncService: found {Count} Pixera connectors with content sync enabled",
            connectors.Count);

        foreach (var conn in connectors)
        {
            try
            {
                await SyncConnectorFoldersAsync(directorDb, conn, uncBasePath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PixeraContentSyncService: error syncing connector {ConnectorId}", conn.ConnectorId);
            }
        }
    }

    private async Task SyncConnectorFoldersAsync(
        IDirectorDBContext directorDb, TblCloudPlatformConnector conn,
        string uncBasePath, CancellationToken ct)
    {
        var config = _configProtector.Decrypt(conn.ConfigJson);
        if (string.IsNullOrEmpty(config?.BaseUrl))
        {
            _logger.LogWarning("PixeraContentSyncService: connector {ConnectorId} has no BaseUrl configured", conn.ConnectorId);
            return;
        }

        var rpcUrl = config.BaseUrl.TrimEnd('/') + "/jsonrpc";
        var apiClient = new PixeraApiClient(rpcUrl, _httpClientFactory.CreateClient(), _logger);

        // Include both location-based (playlist) channels and display-based (schedule) channels.
        // A channel with only DisplayId set is valid: content comes from the display's schedule
        // events rather than a static location folder.
        var folderChannels = await directorDb.TblContentChannels
            .Where(c => c.SourceType == "pixera-folder" && c.ConnectorId == conn.ConnectorId
                        && (c.LocationId != null || c.DisplayId != null))
            .ToListAsync(ct);

        _logger.LogInformation("PixeraContentSyncService: connector {ConnectorId} has {Count} assigned Pixera folder channels",
            conn.ConnectorId, folderChannels.Count);

        foreach (var channel in folderChannels)
        {
            try
            {
                if (string.IsNullOrEmpty(channel.ExternalId)) continue;
                await SyncFolderContentAsync(directorDb, apiClient, channel, uncBasePath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PixeraContentSyncService: error syncing channel {ChannelId}", channel.ChannelId);
            }
        }
    }

    private async Task SyncFolderContentAsync(
        IDirectorDBContext directorDb, PixeraApiClient apiClient,
        TblContentChannel channel, string uncBasePath, CancellationToken ct)
    {
        // ExternalId is the Pixera name path set during the CloudConnectors folder sync
        Newtonsoft.Json.Linq.JToken folderHandle;
        try
        {
            folderHandle = await apiClient.GetResourceFolderWithNamePathAsync(channel.ExternalId!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PixeraContentSyncService: could not resolve folder '{NamePath}'", channel.ExternalId);
            return;
        }

        if (folderHandle == null || folderHandle.Type == Newtonsoft.Json.Linq.JTokenType.Null)
        {
            _logger.LogWarning("PixeraContentSyncService: folder '{NamePath}' not found in Pixera", channel.ExternalId);
            return;
        }

        // Load persisted filename→file-entry map so we can track what we've already uploaded
        var handleMap = LoadHandleMap(channel);
        var handleMapChanged = false;

        // Resolve Director content files for the assigned location, excluding declined assets
        var directorFiles = await ResolveDirectorContentAsync(directorDb, channel, ct);
        _logger.LogInformation("PixeraContentSyncService: folder '{FolderName}' (loc:{LocationId}) resolved {Count} Director files",
            channel.Name, channel.LocationId, directorFiles.Count);

        if (directorFiles.Count == 0) return;

        // Get current Pixera resources to detect what already exists and what to remove
        List<(Newtonsoft.Json.Linq.JToken Handle, string Name)> existingResources;
        try
        {
            existingResources = await apiClient.ListFolderResourcesAsync(folderHandle, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PixeraContentSyncService: could not list resources in folder '{FolderName}'", channel.Name);
            return;
        }

        var resourceByName = existingResources
            .ToDictionary(r => r.Name, r => r.Handle, StringComparer.OrdinalIgnoreCase);

        var activeFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uploaded = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var (fileName, diskPath) in directorFiles)
        {
            activeFileNames.Add(fileName);

            // Convert the local wwwroot path to a UNC path Pixera can read from the file share
            var uncPath = diskPath.Replace(_env.WebRootPath, uncBasePath, StringComparison.OrdinalIgnoreCase);
            // Pixera resource path used by assignResourceToLayer: "{folderNamePath}/{filename}"
            var pixeraPath = $"{channel.ExternalId}/{fileName}";

            if (resourceByName.ContainsKey(fileName))
            {
                // Already present in Pixera — populate the entry so the layer sync service can
                // find both the UNC path and the Pixera resource path without a separate upload cycle
                skipped++;
                handleMap.TryAdd(fileName, new PixeraFileEntry(uncPath, pixeraPath));
                continue;
            }

            if (!File.Exists(diskPath))
            {
                _logger.LogWarning("PixeraContentSyncService: file not found on disk: {DiskPath}", diskPath);
                errors++;
                continue;
            }

            try
            {
                _logger.LogInformation("PixeraContentSyncService: adding '{FileName}' to folder '{NamePath}' — UNC path: {UncPath}",
                    fileName, channel.ExternalId, uncPath);
                // Use Compound.addResourceToFolder — takes the name path directly, no handle needed
                await apiClient.AddResourceToFolderAsync(channel.ExternalId!, uncPath, ct);
                uploaded++;
                handleMap[fileName] = new PixeraFileEntry(uncPath, pixeraPath);
                handleMapChanged = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PixeraContentSyncService: failed to add '{FileName}'", fileName);
                errors++;
            }
        }

        // Remove Pixera resources that are tracked in our map but no longer in Director content
        var staleKeys = new List<string>();
        foreach (var kvp in handleMap)
        {
            if (!activeFileNames.Contains(kvp.Key) && resourceByName.ContainsKey(kvp.Key))
            {
                try
                {
                    _logger.LogInformation("PixeraContentSyncService: deleting '{FileName}' from Pixera (removed from Director)", kvp.Key);
                    await apiClient.DeleteResourceIncludingAssetsAsync(resourceByName[kvp.Key]);
                    staleKeys.Add(kvp.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PixeraContentSyncService: failed to delete '{FileName}'", kvp.Key);
                }
            }
        }

        foreach (var key in staleKeys)
        {
            handleMap.Remove(key);
            handleMapChanged = true;
        }

        if (handleMapChanged)
        {
            // Uploads above can take a long time, so merge into the current database
            // value rather than this cycle's snapshot — an admin settings save landing
            // mid-cycle must survive this write. The handle map itself has no other
            // writer (distributed lock), so replacing it wholesale is safe.
            channel.ExtendedData = PixeraChannelExtendedData.WithHandleMap(
                await PixeraChannelExtendedData.ReadCurrentAsync(directorDb, channel.ChannelId, ct),
                Newtonsoft.Json.Linq.JObject.FromObject(handleMap));
            await directorDb.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "PixeraContentSyncService: sync complete for '{FolderName}' — {Uploaded} uploaded, {Skipped} already present, {Deleted} deleted, {Errors} errors",
            channel.Name, uploaded, skipped, staleKeys.Count, errors);
    }

    /// <summary>
    /// Returns the media files for this channel that are eligible to sync to Pixera.
    /// Supports two resolution paths that can both contribute to the result:
    ///
    /// 1. Location path (LocationId set): scans MediaAssets/{locationId}/ and cross-checks
    ///    against TblAsset to exclude declined and deleted assets. Used for playlist-style channels.
    ///
    /// 2. Schedule path (DisplayId set): reads TblScheduleEvent.ContentAssignment for the display,
    ///    extracts every referenced asset ID, and resolves those assets regardless of which location
    ///    they live in. Used for display-based (schedule) channels where assets may come from an
    ///    asset library at a different location than the channel's own LocationId.
    ///
    /// Declined assets (ApprovalStatus == 2) are excluded from both paths.
    /// </summary>
    private async Task<List<(string FileName, string DiskPath)>> ResolveDirectorContentAsync(
        IDirectorDBContext directorDb, TblContentChannel channel, CancellationToken ct)
    {
        var files = new List<(string FileName, string DiskPath)>();
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Path 1: Location-based (playlist) ──────────────────────────────────────────────
        if (channel.LocationId.HasValue)
        {
            var allowedFilenames = (await directorDb.TblAssets
                .Where(a => a.LocationId == channel.LocationId.Value && !a.IsDeleted)
                .Where(a => a.AssetApproval == null || a.AssetApproval.ApprovalStatus != 2)
                .Select(a => a.AssetPath)
                .ToListAsync(ct))
                .Select(p => Path.GetFileName(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Pixera pipeline reads the global web root: the Pixera-owning tenant must keep
            // FilePaths_MediaRoot='wwwroot' when tenant media isolation is enabled.
            var mediaDir = Path.Combine(_env.WebRootPath, "MediaAssets", channel.LocationId.Value.ToString());
            if (Directory.Exists(mediaDir))
            {
                // Assets are organised into constraint subfolders (MediaAssets/{locationId}/{constraintId}/file),
                // so scan recursively to reach all depth levels.
                foreach (var filePath in Directory.GetFiles(mediaDir, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext == ".metadata" || ext == ".tmp") continue;
                    var fileName = Path.GetFileName(filePath);
                    if (!allowedFilenames.Contains(fileName)) continue;
                    if (seenFileNames.Add(fileName))
                        files.Add((fileName, filePath));
                }
            }
        }

        // ── Path 2: Schedule-based (display) ───────────────────────────────────────────────
        // Assets are referenced by ID in ContentAssignment JSON; they may live in any location
        // (e.g., an asset library), so we resolve them directly rather than scanning a folder.
        if (channel.DisplayId.HasValue)
        {
            var events = await directorDb.TblScheduleEvents
                .Where(e => e.DisplayId == channel.DisplayId.Value)
                .Select(e => e.ContentAssignment)
                .ToListAsync(ct);

            var scheduledAssetIds = new HashSet<int>();
            foreach (var ca in events)
            {
                foreach (var id in ParseContentAssignmentAssetIds(ca))
                    scheduledAssetIds.Add(id);
            }

            if (scheduledAssetIds.Count > 0)
            {
                var scheduleAssets = await directorDb.TblAssets
                    .Where(a => scheduledAssetIds.Contains(a.AssetId) && !a.IsDeleted)
                    .Where(a => a.AssetApproval == null || a.AssetApproval.ApprovalStatus != 2)
                    .ToListAsync(ct);

                foreach (var asset in scheduleAssets)
                {
                    var fileName = Path.GetFileName(asset.AssetPath);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    // AssetPath is a relative web path (e.g. "MediaAssets/123/file.mp4");
                    // convert to an absolute disk path for file-existence check and UNC mapping.
                    var relativePath = asset.AssetPath
                        .TrimStart('/', '\\')
                        .Replace('/', Path.DirectorySeparatorChar);
                    var diskPath = Path.Combine(_env.WebRootPath, relativePath);

                    if (!File.Exists(diskPath)) continue;
                    if (seenFileNames.Add(fileName))
                        files.Add((fileName, diskPath));
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Parses all AssetId values from a ContentAssignment JSON string.
    /// Returns an empty sequence on null, empty, or malformed input.
    /// </summary>
    private static List<int> ParseContentAssignmentAssetIds(string? contentAssignment)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(contentAssignment)) return ids;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(contentAssignment);
            if (doc.RootElement.TryGetProperty("Content", out var content) &&
                content.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("AssetId", out var assetIdProp))
                    {
                        var id = assetIdProp.GetInt32();
                        if (id > 0) ids.Add(id);
                    }
                }
            }
        }
        catch { }
        return ids;
    }

    /// <summary>
    /// Tracks a file that has been uploaded to (or found in) a Pixera resource folder.
    /// UncPath is the Windows share path Pixera loads the file from;
    /// PixeraPath is the "{folderNamePath}/{filename}" string used by assignResourceToLayer.
    /// </summary>
    private sealed record PixeraFileEntry(string UncPath, string PixeraPath);

    /// <summary>
    /// Reads the persisted filename→entry map from the channel's ExtendedData.
    /// Supports the old string-value format (UNC path only) for backward compatibility.
    /// </summary>
    private static Dictionary<string, PixeraFileEntry> LoadHandleMap(TblContentChannel channel)
    {
        if (string.IsNullOrEmpty(channel.ExtendedData))
            return new Dictionary<string, PixeraFileEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(channel.ExtendedData);
            if (doc.RootElement.TryGetProperty("pixeraHandleMap", out var mapProp)
                && mapProp.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var map = new Dictionary<string, PixeraFileEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in mapProp.EnumerateObject())
                {
                    if (entry.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        // Old format: value was a plain UNC path string — PixeraPath will be
                        // populated on the next upload or "already present" pass
                        var uncPath = entry.Value.GetString();
                        if (uncPath != null)
                            map[entry.Name] = new PixeraFileEntry(uncPath, "");
                    }
                    else if (entry.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var uncPath = entry.Value.TryGetProperty("UncPath", out var u) ? u.GetString() ?? "" : "";
                        var pixeraPath = entry.Value.TryGetProperty("PixeraPath", out var p) ? p.GetString() ?? "" : "";
                        map[entry.Name] = new PixeraFileEntry(uncPath, pixeraPath);
                    }
                }
                return map;
            }
        }
        catch { }

        return new Dictionary<string, PixeraFileEntry>(StringComparer.OrdinalIgnoreCase);
    }
}
