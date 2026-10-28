using System.Text.Json;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using DXSuite.Director.Entities;
using DXSuite.Director.Models;
using DXSuite.Director.Services.DistributedLock;
using DXSuite.Connectors.Services.Connectors;
using DXSuite.Shared.Multitenancy;
using ICloudPlatformConnectorRegistry = DXSuite.Shared.Services.ICloudPlatformConnectorRegistry;

namespace DXSuite.Director.Services;

/// <summary>
/// Background service that syncs Director content (playlists and schedule assets)
/// to assigned Ink folders. Runs on a 60-second cycle, iterating all worker tenants so
/// each tenant's connectors sync against that tenant's Director DB and site URL.
/// </summary>
public class InkContentSyncService : BackgroundService
{
    private readonly IWorkerScopeFactory _workerScopeFactory;
    private readonly ILogger<InkContentSyncService> _logger;
    private readonly IDistributedLockService _lockService;
    private readonly ICloudPlatformConnectorRegistry _connectorRegistry;
    private readonly ConnectorConfigProtector _configProtector;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public InkContentSyncService(
        IWorkerScopeFactory workerScopeFactory,
        ILogger<InkContentSyncService> logger,
        IDistributedLockService lockService,
        ICloudPlatformConnectorRegistry connectorRegistry,
        ConnectorConfigProtector configProtector,
        IWebHostEnvironment env,
        IConfiguration configuration)
    {
        _workerScopeFactory = workerScopeFactory;
        _logger = logger;
        _lockService = lockService;
        _connectorRegistry = connectorRegistry;
        _configProtector = configProtector;
        _env = env;
        _configuration = configuration;
        Console.WriteLine(">>> InkContentSyncService: constructed successfully");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InkContentSyncService: starting (delay {Delay}s)", StartupDelay.TotalSeconds);
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var distLock = await _lockService.TryAcquireLockAsync(
                "ink-content-sync", TimeSpan.FromMinutes(5), stoppingToken);

            if (distLock != null)
            {
                try
                {
                    _logger.LogInformation("InkContentSyncService: running sync cycle");
                    await RunSyncCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "InkContentSyncService: error during sync cycle");
                }
            }
            else
            {
                _logger.LogDebug("Another instance is handling Ink content sync, skipping this cycle");
            }

            try { await Task.Delay(SyncInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken ct)
    {
        foreach (var tenantSubdomain in _workerScopeFactory.WorkerTenants)
        {
            try
            {
                using var scope = await _workerScopeFactory.CreateTenantScopeAsync(tenantSubdomain, ct);
                var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();

                // Tenants without a Director module license have no Director DB to sync.
                if (tenantCtx.DirectorConnectionString == null) continue;

                var directorDb = scope.ServiceProvider.GetRequiredService<IDirectorDBContext>();
                await SyncTenantAsync(directorDb, tenantCtx, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InkContentSyncService: error syncing tenant {Tenant}", tenantSubdomain);
            }
        }
    }

    private async Task SyncTenantAsync(IDirectorDBContext directorDb, ITenantContext tenantCtx, CancellationToken ct)
    {
        var mediaRoot = TenantMediaPaths.GetRoot(tenantCtx, _env, _configuration);

        // Load Ink connectors with content sync enabled (content-only or content-and-monitoring)
        var connectors = (await directorDb.TblCloudPlatformConnectors
            .Where(c => c.IsEnabled && c.ContentMode != "monitoring-only" && c.PlatformId == "omnivex-ink")
            .ToListAsync(ct))
            .Where(c => IsImportEnabled(c.EnabledImports, "folders"))
            .ToList();

        _logger.LogInformation("InkContentSyncService: found {Count} Ink connectors with content sync enabled for tenant {Tenant}",
            connectors.Count, tenantCtx.Subdomain);
        if (connectors.Count == 0)
        {
            // Still run content source URL resolution (supports local-only mode without folder sync)
            await UpdateContentSourceUrlsAsync(directorDb, tenantCtx, ct);
            return;
        }

        foreach (var conn in connectors)
        {
            try
            {
                await SyncConnectorFoldersAsync(directorDb, conn, mediaRoot, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InkContentSyncService: error syncing connector {ConnectorId}", conn.ConnectorId);
            }
        }

        // Resolve content source URLs after folder sync completes
        await UpdateContentSourceUrlsAsync(directorDb, tenantCtx, ct);
    }

    private async Task SyncConnectorFoldersAsync(
        IDirectorDBContext directorDb, TblCloudPlatformConnector conn, string mediaRoot, CancellationToken ct)
    {
        var platform = _connectorRegistry.GetConnector(conn.PlatformId);
        if (platform is not OmnivexInkConnector inkConnector) return;

        var config = DeserializeConfig(conn.ConfigJson) ?? new DXSuite.Shared.Services.PlatformConnectionConfig();

        // Load ink-folder channels for this connector that have a location or display assigned
        var folderChannels = await directorDb.TblContentChannels
            .Where(c => c.SourceType == "ink-folder" && c.ConnectorId == conn.ConnectorId
                        && (c.LocationId != null || c.DisplayId != null))
            .ToListAsync(ct);

        _logger.LogInformation("InkContentSyncService: connector {ConnectorId} has {Count} assigned ink-folder channels",
            conn.ConnectorId, folderChannels.Count);
        if (folderChannels.Count == 0) return;

        foreach (var channel in folderChannels)
        {
            try
            {
                // ExternalId is the Ink folder ID set during the CloudConnectors sync
                if (string.IsNullOrEmpty(channel.ExternalId)) continue;

                var result = await SyncFolderContentAsync(
                    directorDb, inkConnector, config, channel, channel.ExternalId, mediaRoot, ct);

                if (result.Uploaded > 0 || result.Patched > 0 || result.Deleted > 0)
                {
                    _logger.LogInformation(
                        "InkContentSync: folder '{FolderName}' â€” {Uploaded} uploaded, {Patched} patched, {Deleted} deleted",
                        channel.Name, result.Uploaded, result.Patched, result.Deleted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InkContentSyncService: error syncing ink-folder channel {ChannelId}", channel.ChannelId);
            }
        }
    }

    private async Task<InkSyncResult> SyncFolderContentAsync(
        IDirectorDBContext directorDb, OmnivexInkConnector inkConnector,
        DXSuite.Shared.Services.PlatformConnectionConfig config, TblContentChannel channel, string folderId,
        string mediaRoot, CancellationToken ct)
    {
        var result = new InkSyncResult();

        // Check if content source redirection is configured
        config.AdditionalSettings.TryGetValue("LocalContentBaseUrl", out var localContentBaseUrl);
        var hasLocalUrl = !string.IsNullOrWhiteSpace(localContentBaseUrl);

        // Load persisted filename â†’ Ink file ID map from channel ExtendedData
        var inkFileMap = LoadInkFileMap(channel);
        var inkFileMapChanged = false;

        // Resolve content source
        var directorFiles = await ResolveDirectorContentAsync(directorDb, channel, mediaRoot, ct);
        _logger.LogInformation("InkContentSyncService: folder '{FolderName}' (loc:{LocationId}, disp:{DisplayId}) resolved {FileCount} Director files",
            channel.Name, channel.LocationId, channel.DisplayId, directorFiles.Count);
        if (directorFiles.Count == 0) return result;

        // Get current Ink folder contents â€” build lookups by both name and ID
        var inkFiles = await inkConnector.ListFolderContentsAsync(config, folderId, ct);
        _logger.LogInformation("InkContentSyncService: Ink folder {FolderId} has {InkCount} existing files", folderId, inkFiles.Count);
        var inkByName = new Dictionary<string, OmnivexInkConnector.InkFileItem>(StringComparer.OrdinalIgnoreCase);
        var inkById = new Dictionary<string, OmnivexInkConnector.InkFileItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in inkFiles)
        {
            _logger.LogDebug("InkContentSyncService:   Ink file: id={InkId}, name='{InkName}', payloadName='{PayloadName}'",
                f.Id, f.Name, f.Payload?.Name);
            inkByName.TryAdd(f.Name ?? "", f);
            if (f.Id != null)
                inkById.TryAdd(f.Id, f);
        }

        _logger.LogInformation("InkContentSyncService: inkFileMap has {MapCount} stored entries, inkById has {ByIdCount} entries, inkByName has {ByNameCount} entries",
            inkFileMap.Count, inkById.Count, inkByName.Count);

        // Track which Ink file IDs are still associated with Director files (for deletion check)
        var activeInkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dirFile in directorFiles)
        {
            var metadata = BuildInkMetadata(dirFile);
            _logger.LogDebug("InkContentSyncService: '{FileName}' metadata keys: [{Keys}]",
                dirFile.FileName, string.Join(", ", metadata.Keys));

            // Match by stored Ink ID first, then fall back to filename
            OmnivexInkConnector.InkFileItem? existingInk = null;
            if (inkFileMap.TryGetValue(dirFile.FileName, out var storedInkId) && inkById.TryGetValue(storedInkId, out var byId))
            {
                _logger.LogDebug("InkContentSyncService: '{FileName}' matched by stored ID {InkId}", dirFile.FileName, storedInkId);
                existingInk = byId;
            }
            else if (inkByName.TryGetValue(dirFile.FileName, out var byName))
            {
                _logger.LogDebug("InkContentSyncService: '{FileName}' matched by name", dirFile.FileName);
                existingInk = byName;
                // Backfill the map for files that existed before ID tracking was added
                if (existingInk.Id != null)
                {
                    inkFileMap[dirFile.FileName] = existingInk.Id;
                    inkFileMapChanged = true;
                }
            }
            else
            {
                _logger.LogDebug("InkContentSyncService: '{FileName}' no match (storedId={StoredId}, inById={InById})",
                    dirFile.FileName, storedInkId ?? "(none)",
                    storedInkId != null && inkById.ContainsKey(storedInkId));
            }

            if (existingInk?.Id != null)
            {
                // File exists in Ink â€” PATCH metadata
                activeInkIds.Add(existingInk.Id);
                _logger.LogInformation("InkContentSyncService: PATCHing metadata for '{FileName}' (inkId: {InkId})", dirFile.FileName, existingInk.Id);
                var patched = await inkConnector.PatchFileMetadataAsync(
                    config, existingInk.Id, metadata, ct);
                if (patched) result.Patched++;
                else _logger.LogWarning("InkContentSyncService: PATCH failed for '{FileName}'", dirFile.FileName);

                if (hasLocalUrl)
                {
                    var fileUrl = BuildFileContentsUrl(localContentBaseUrl!, mediaRoot, dirFile.DiskPath);
                    await inkConnector.PatchFileContentsUrlAsync(config, existingInk.Id, fileUrl, ct);
                }
            }
            else
            {
                // New file â€” upload to Ink folder
                if (File.Exists(dirFile.DiskPath))
                {
                    _logger.LogInformation("InkContentSyncService: uploading '{FileName}' to folder {FolderId}", dirFile.FileName, folderId);
                    using var stream = File.OpenRead(dirFile.DiskPath);
                    var (inkFileId, uploadError) = await inkConnector.UploadFileToFolderAsync(
                        config, folderId, stream, dirFile.FileName, metadata, ct);

                    if (inkFileId != null)
                    {
                        result.Uploaded++;
                        activeInkIds.Add(inkFileId);
                        inkFileMap[dirFile.FileName] = inkFileId;
                        inkFileMapChanged = true;
                        StoreInkIdentifier(dirFile.DiskPath, inkFileId);

                        if (hasLocalUrl)
                        {
                            var fileUrl = BuildFileContentsUrl(localContentBaseUrl!, mediaRoot, dirFile.DiskPath);
                            await inkConnector.PatchFileContentsUrlAsync(config, inkFileId, fileUrl, ct);
                        }
                    }
                    else
                    {
                        result.Errors++;
                        _logger.LogWarning("InkContentSyncService: upload failed for '{FileName}': {Error}", dirFile.FileName, uploadError ?? "unknown");
                    }
                }
                else
                {
                    _logger.LogWarning("InkContentSyncService: file not found on disk: {DiskPath}", dirFile.DiskPath);
                }
            }
        }

        // Delete Ink files whose IDs are in our map but no longer active (removed from Director)
        var staleMapEntries = new List<string>();
        foreach (var kvp in inkFileMap)
        {
            if (!activeInkIds.Contains(kvp.Value) && inkById.ContainsKey(kvp.Value))
            {
                _logger.LogInformation("InkContentSyncService: deleting '{FileName}' (inkId: {InkId}) from Ink (no longer in Director)", kvp.Key, kvp.Value);
                var deleted = await inkConnector.DeleteFileFromFolderAsync(config, kvp.Value, ct);
                if (deleted) result.Deleted++;
                staleMapEntries.Add(kvp.Key);
            }
        }
        foreach (var key in staleMapEntries)
        {
            inkFileMap.Remove(key);
            inkFileMapChanged = true;
        }

        // Persist the updated map if anything changed
        if (inkFileMapChanged)
        {
            SaveInkFileMap(channel, inkFileMap);
            await directorDb.SaveChangesAsync(ct);
        }

        _logger.LogInformation("InkContentSyncService: sync complete â€” {Uploaded} uploaded, {Patched} patched, {Deleted} deleted, {Errors} errors",
            result.Uploaded, result.Patched, result.Deleted, result.Errors);
        return result;
    }

    /// <summary>
    /// Builds a FileContentsUrl from DiskPath relative to the tenant media root, so Ink
    /// displays download content from the local Director server instead of the Ink CDN.
    /// The root-relative part matches the public URL shape (/MediaAssets/...) because the
    /// tenant media middleware maps those URLs back onto the tenant root when serving.
    /// </summary>
    private static string BuildFileContentsUrl(string baseUrl, string mediaRoot, string diskPath)
    {
        var relativePath = diskPath.Substring(mediaRoot.Length).Replace('\\', '/');
        if (!relativePath.StartsWith('/')) relativePath = "/" + relativePath;
        return $"{baseUrl.TrimEnd('/')}{relativePath}";
    }

    private async Task<List<DirectorContentFile>> ResolveDirectorContentAsync(
        IDirectorDBContext directorDb, TblContentChannel channel, string mediaRoot, CancellationToken ct)
    {
        var files = new List<DirectorContentFile>();
        var wwwroot = mediaRoot;

        // Check if content approval is enabled for the location
        var enableContentApproval = false;
        var locationId = channel.LocationId;
        DXSuite.Director.Entities.TblLocation? location = null;
        if (locationId.HasValue)
        {
            location = await directorDb.TblLocations.AsNoTracking()
                .Include(l => l.LocationType)
                .FirstOrDefaultAsync(l => l.LocationId == locationId.Value, ct);
            enableContentApproval = location?.EnableContentApproval ?? false;
        }

        // Build a set of approved asset IDs when content approval is active
        HashSet<int>? approvedAssetIds = null;
        if (enableContentApproval && locationId.HasValue)
        {
            approvedAssetIds = (await directorDb.TblAssetApprovals
                .AsNoTracking()
                .Include(a => a.Asset)
                .Where(a => a.Asset != null && a.Asset.LocationId == locationId.Value
                    && a.ApprovalStatus == (int)Types.ApprovalStatus.Approved)
                .Select(a => a.AssetId)
                .ToListAsync(ct))
                .ToHashSet();
        }

        if (channel.LocationId.HasValue)
        {
            // Check if this is a playlist-type location (files on disk)
            var mediaDir = Path.Combine(wwwroot, "MediaAssets", channel.LocationId.Value.ToString());

            // Asset Libraries store files in a constraint subfolder
            if (location?.LocationType?.ComponentTypeName?.EndsWith("AssetLibrary") == true)
            {
                var globalConstraintId = ExtendedDataHelper.Parse<LocationExtendedData>(location.ExtendedData)?.GlobalConstraintId;
                if (globalConstraintId.HasValue)
                    mediaDir = Path.Combine(mediaDir, globalConstraintId.Value.ToString());
            }

            if (Directory.Exists(mediaDir))
            {
                // Build file-to-assetId map for DB-based approval enforcement in playlist path
                Dictionary<string, int>? playlistFileToAssetId = null;
                if (enableContentApproval && locationId.HasValue)
                {
                    playlistFileToAssetId = (await directorDb.TblAssets.AsNoTracking()
                        .Where(a => a.LocationId == locationId.Value && !a.IsDeleted)
                        .Select(a => new { a.AssetId, a.AssetPath })
                        .ToListAsync(ct))
                        .ToDictionary(a => Path.GetFileName(a.AssetPath), a => a.AssetId, StringComparer.OrdinalIgnoreCase);
                }

                foreach (var filePath in Directory.GetFiles(mediaDir))
                {
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext == ".metadata" || ext == ".tmp") continue;

                    // DB-based approval gate: exclude unapproved content
                    if (enableContentApproval && approvedAssetIds != null)
                    {
                        var fn = Path.GetFileName(filePath);
                        if (playlistFileToAssetId == null || !playlistFileToAssetId.TryGetValue(fn, out var fileAssetId)
                            || !approvedAssetIds.Contains(fileAssetId))
                            continue;
                    }

                    var fileName = Path.GetFileName(filePath);
                    var metadataPath = filePath + ".metadata";
                    var metadataExists = File.Exists(metadataPath);
                    var schedule = ReadScheduleFromMetadata(metadataPath);
                    _logger.LogDebug("InkContentSyncService: file '{FileName}' â€” .metadata exists={MetadataExists}, schedule={ScheduleInfo}",
                        fileName, metadataExists,
                        schedule != null ? $"{schedule.Count} entries: {System.Text.Json.JsonSerializer.Serialize(schedule)}" : "null");
                    var customFields = ReadCustomFieldsFromMetadata(metadataPath);

                    files.Add(new DirectorContentFile
                    {
                        FileName = fileName,
                        DiskPath = filePath,
                        Schedule = schedule,
                        CustomMetadata = customFields
                    });
                }
            }
        }

        if (channel.DisplayId.HasValue)
        {
            // Schedule-based: read TblScheduleEvent + TblAsset from DB
            var events = await directorDb.TblScheduleEvents
                .Where(e => e.DisplayId == channel.DisplayId.Value)
                .ToListAsync(ct);

            // Collect all asset IDs across events
            var assetIds = new HashSet<int>();
            var assetSchedules = new Dictionary<int, List<InkScheduleEntry>>();

            foreach (var evt in events)
            {
                if (string.IsNullOrEmpty(evt.ContentAssignment)) continue;

                try
                {
                    var ca = JsonConvert.DeserializeObject<ContentAssignment>(evt.ContentAssignment);
                    if (ca?.Content == null) continue;

                    var days = ConvertDayBoolsToArray(evt);
                    var entry = new InkScheduleEntry
                    {
                        StartDate = evt.StartDate?.ToString("yyyy-MM-dd"),
                        EndDate = evt.EndDate?.ToString("yyyy-MM-dd"),
                        StartTime = evt.StartTime?.ToString("HH:mm:ss"),
                        EndTime = evt.EndTime?.ToString("HH:mm:ss"),
                        AllDay = evt.StartTime == null && evt.EndTime == null,
                        Days = days.Count > 0 ? days : null
                    };

                    foreach (var ci in ca.Content)
                    {
                        assetIds.Add(ci.AssetId);
                        if (!assetSchedules.ContainsKey(ci.AssetId))
                            assetSchedules[ci.AssetId] = new List<InkScheduleEntry>();
                        assetSchedules[ci.AssetId].Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing ContentAssignment for event {EventId}", evt.ScheduleEventId);
                }
            }

            if (assetIds.Count > 0)
            {
                var assets = await directorDb.TblAssets
                    .Where(a => assetIds.Contains(a.AssetId) && !a.IsDeleted)
                    .ToListAsync(ct);

                foreach (var asset in assets)
                {
                    // Skip unapproved assets when content approval is enabled
                    if (enableContentApproval && approvedAssetIds != null
                        && !approvedAssetIds.Contains(asset.AssetId))
                        continue;

                    var diskPath = Path.Combine(wwwroot, asset.AssetPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(diskPath)) continue;

                    assetSchedules.TryGetValue(asset.AssetId, out var scheduleEntries);

                    files.Add(new DirectorContentFile
                    {
                        FileName = Path.GetFileName(diskPath),
                        DiskPath = diskPath,
                        Schedule = scheduleEntries,
                        AssetId = asset.AssetId
                    });
                }
            }
        }

        return files;
    }

    /// <summary>
    /// Builds metadata dictionary for Ink. Schedule is passed as the native Ink Schedule type
    /// (array of InkScheduleEntry objects) rather than flattened string fields.
    /// </summary>
    private Dictionary<string, object> BuildInkMetadata(DirectorContentFile dirFile)
    {
        var metadata = new Dictionary<string, object>();

        if (dirFile.Schedule != null && dirFile.Schedule.Count > 0)
            metadata["Schedule"] = dirFile.Schedule.Select(s => s.ToInkFormat()).ToList();

        if (dirFile.CustomMetadata != null)
        {
            foreach (var kvp in dirFile.CustomMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return metadata;
    }

    private List<InkScheduleEntry>? ReadScheduleFromMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath)) return null;

        try
        {
            var doc = new XmlDocument();
            doc.Load(metadataPath);

            // Check for JSON Schedule field first
            var scheduleNode = doc.SelectSingleNode("//field[@name='Schedule']");
            if (scheduleNode?.Attributes?["value"]?.Value is string scheduleJson)
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<InkScheduleEntry>>(scheduleJson, JsonOptions);
            }

            // Fall back to individual fields (playlist-style metadata)
            var fields = new Dictionary<string, string>();
            var fieldNodes = doc.SelectNodes("//field");
            if (fieldNodes != null)
            {
                foreach (XmlNode node in fieldNodes)
                {
                    var name = node.Attributes?["name"]?.Value;
                    var value = node.Attributes?["value"]?.Value;
                    if (name != null && value != null)
                        fields[name] = value;
                }
            }

            if (fields.Count > 0)
            {
                var entry = new InkScheduleEntry
                {
                    StartDate = fields.GetValueOrDefault("StartDate"),
                    EndDate = fields.GetValueOrDefault("EndDate"),
                    StartTime = fields.GetValueOrDefault("StartTime"),
                    EndTime = fields.GetValueOrDefault("EndTime"),
                    AllDay = !fields.ContainsKey("StartTime") && !fields.ContainsKey("EndTime")
                };

                if (fields.TryGetValue("Days", out var daysStr) && !string.IsNullOrEmpty(daysStr))
                {
                    entry.Days = daysStr.Split(',', StringSplitOptions.TrimEntries).ToList();
                }

                return new List<InkScheduleEntry> { entry };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading schedule metadata from {Path}", metadataPath);
        }

        return null;
    }

    private Dictionary<string, string>? ReadCustomFieldsFromMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath)) return null;

        try
        {
            var doc = new XmlDocument();
            doc.Load(metadataPath);
            var fieldNodes = doc.SelectNodes("//field");
            if (fieldNodes == null) return null;

            // Standard field names to exclude from custom fields
            var standardFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Location", "FileName", "StartDate", "EndDate", "StartTime", "EndTime",
                "Days", "Schedule", "Language", "PlayOrder", "Duration",
                "ApprovalStatus", "Identifier"
            };

            var custom = new Dictionary<string, string>();
            foreach (XmlNode node in fieldNodes)
            {
                var name = node.Attributes?["name"]?.Value;
                var value = node.Attributes?["value"]?.Value;
                if (name != null && value != null && !standardFields.Contains(name))
                {
                    custom[name] = value;
                }
            }

            return custom.Count > 0 ? custom : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the ApprovalStatus field from a sidecar .metadata XML file.
    /// Returns the parsed int status, or null if the field is absent or the file doesn't exist.
    /// </summary>
    private static int? ReadApprovalStatusFromMetadata(string metadataPath)
    {
        if (!File.Exists(metadataPath)) return null;

        try
        {
            var doc = new XmlDocument();
            doc.Load(metadataPath);
            var node = doc.SelectSingleNode("//field[@name='ApprovalStatus']");
            var value = node?.Attributes?["value"]?.Value;
            return value != null && int.TryParse(value, out var status) ? status : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the persisted filename â†’ Ink file ID map from the channel's ExtendedData.
    /// This map survives across sync cycles so files are matched by their stable Ink ID
    /// rather than relying on filename matching against the Ink API response.
    /// </summary>
    private static Dictionary<string, string> LoadInkFileMap(TblContentChannel channel)
    {
        if (string.IsNullOrEmpty(channel.ExtendedData))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(channel.ExtendedData);
            if (doc.RootElement.TryGetProperty("inkFileMap", out var mapProp) && mapProp.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in mapProp.EnumerateObject())
                {
                    var val = entry.Value.GetString();
                    if (val != null)
                        map[entry.Name] = val;
                }
                return map;
            }
        }
        catch { }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists the filename â†’ Ink file ID map into the channel's ExtendedData.
    /// ExternalId and other Ink metadata live in dedicated columns; only the file map goes here.
    /// </summary>
    private static void SaveInkFileMap(TblContentChannel channel, Dictionary<string, string> inkFileMap)
    {
        channel.ExtendedData = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object> { ["inkFileMap"] = inkFileMap });
    }

    private void StoreInkIdentifier(string fileDiskPath, string inkFileId)
    {
        try
        {
            var metadataPath = fileDiskPath + ".metadata";

            if (File.Exists(metadataPath))
            {
                var doc = new XmlDocument();
                doc.Load(metadataPath);
                var root = doc.SelectSingleNode("usermetadata") ?? doc.DocumentElement;
                if (root != null)
                {
                    var existing = root.SelectSingleNode("field[@name='Identifier']");
                    if (existing != null)
                    {
                        existing.Attributes!["value"]!.Value = inkFileId;
                    }
                    else
                    {
                        var el = doc.CreateElement("field");
                        el.SetAttribute("name", "Identifier");
                        el.SetAttribute("datatype", "ovx_String");
                        el.SetAttribute("value", inkFileId);
                        root.AppendChild(el);
                    }
                    doc.Save(metadataPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error storing Ink identifier in metadata for {Path}", fileDiskPath);
        }
    }

    private static List<string> ConvertDayBoolsToArray(TblScheduleEvent evt)
    {
        var days = new List<string>();
        if (evt.Monday) days.Add("Mo");
        if (evt.Tuesday) days.Add("Tu");
        if (evt.Wednesday) days.Add("We");
        if (evt.Thursday) days.Add("Th");
        if (evt.Friday) days.Add("Fr");
        if (evt.Saturday) days.Add("Sa");
        if (evt.Sunday) days.Add("Su");
        return days;
    }

    /// <summary>
    /// Represents a Director content file to be synced to Ink.
    /// </summary>
    private class DirectorContentFile
    {
        public string FileName { get; set; } = string.Empty;
        public string DiskPath { get; set; } = string.Empty;
        public List<InkScheduleEntry>? Schedule { get; set; }
        public Dictionary<string, string>? CustomMetadata { get; set; }
        public int? AssetId { get; set; }
    }

    /// <summary>
    /// Checks whether a specific import resource type is enabled. Null EnabledImports means all enabled.
    /// </summary>
    private static bool IsImportEnabled(string? enabledImportsJson, string resourceType)
    {
        if (string.IsNullOrEmpty(enabledImportsJson)) return true;
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(enabledImportsJson);
            return list?.Contains(resourceType, StringComparer.OrdinalIgnoreCase) ?? true;
        }
        catch { return true; }
    }

    /// <summary>
    /// Resolves ContentSourceUrl for assets in locations that have content source mode configured.
    /// Runs independently of folder sync â€” supports ink, local, and failover modes.
    /// </summary>
    private async Task UpdateContentSourceUrlsAsync(IDirectorDBContext directorDb, ITenantContext tenantCtx, CancellationToken ct)
    {
        // No request is active here, so the tenant record's SiteUrl is authoritative; the global
        // FilePaths:SiteUrl covers tenants (and single-tenant installs) with no per-tenant URL set.
        var siteUrl = tenantCtx.SiteUrl ?? _configuration["FilePaths:SiteUrl"];
        if (string.IsNullOrEmpty(siteUrl))
        {
            _logger.LogWarning("InkContentSyncService: no tenant SiteUrl or FilePaths:SiteUrl configured, skipping content source URL resolution");
            return;
        }

        var locations = await directorDb.TblLocations.ToListAsync(ct);
        var updated = 0;

        foreach (var location in locations)
        {
            try
            {
                var config = GetContentSourceConfig(location);
                if (config == null || config.Mode == "disabled") continue;

                // Load the referenced connector
                TblCloudPlatformConnector? connector = null;
                DXSuite.Shared.Services.PlatformConnectionConfig? connConfig = null;
                if (config.ConnectorId.HasValue)
                {
                    connector = await directorDb.TblCloudPlatformConnectors
                        .FirstOrDefaultAsync(c => c.ConnectorId == config.ConnectorId.Value, ct);
                    if (connector != null)
                        connConfig = _configProtector.Decrypt(connector.ConfigJson);
                }

                // Determine whether to use Ink or local URLs
                bool useInk;
                switch (config.Mode)
                {
                    case "ink":
                        useInk = true;
                        break;
                    case "local":
                        useInk = false;
                        break;
                    case "failover":
                        useInk = connector?.LastSyncStatus == "success";
                        break;
                    default:
                        continue;
                }

                // Build Ink file map from ink-folder channels assigned to this location+connector
                Dictionary<string, string>? locationInkFileMap = null;
                if (useInk && config.ConnectorId.HasValue)
                {
                    var folderChannels = await directorDb.TblContentChannels
                        .Where(c => c.SourceType == "ink-folder" && c.ConnectorId == config.ConnectorId.Value
                                    && c.LocationId == location.LocationId)
                        .ToListAsync(ct);

                    locationInkFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var ch in folderChannels)
                    {
                        var map = LoadInkFileMap(ch);
                        foreach (var kvp in map)
                            locationInkFileMap.TryAdd(kvp.Key, kvp.Value);
                    }

                    // Also check display-assigned channels for this location's displays
                    var displayIds = await directorDb.TblDisplays
                        .Where(d => d.LocationId == location.LocationId)
                        .Select(d => d.DisplayId)
                        .ToListAsync(ct);

                    if (displayIds.Count > 0)
                    {
                        var displayFolderChannels = await directorDb.TblContentChannels
                            .Where(c => c.SourceType == "ink-folder" && c.ConnectorId == config.ConnectorId.Value
                                        && c.DisplayId.HasValue && displayIds.Contains(c.DisplayId.Value))
                            .ToListAsync(ct);

                        foreach (var ch in displayFolderChannels)
                        {
                            var map = LoadInkFileMap(ch);
                            foreach (var kvp in map)
                                locationInkFileMap.TryAdd(kvp.Key, kvp.Value);
                        }
                    }
                }

                // Load assets for this location
                var assets = await directorDb.TblAssets
                    .Where(a => a.LocationId == location.LocationId && !a.IsDeleted)
                    .ToListAsync(ct);

                foreach (var asset in assets)
                {
                    var fileName = Path.GetFileName(asset.AssetPath);
                    string? resolvedUrl;

                    if (useInk && connConfig != null && locationInkFileMap != null
                        && locationInkFileMap.TryGetValue(fileName, out var inkFileId))
                    {
                        resolvedUrl = $"{connConfig.BaseUrl.TrimEnd('/')}/media/{inkFileId}";
                    }
                    else
                    {
                        // Local URL â€” derive from AssetPath which already includes any subfolder (e.g. Asset Library constraint)
                        var relativePath = asset.AssetPath.TrimStart('/').Split('/').Select(Uri.EscapeDataString);
                        resolvedUrl = $"{siteUrl.TrimEnd('/')}/{string.Join("/", relativePath)}";
                    }

                    if (asset.ContentSourceUrl != resolvedUrl)
                    {
                        asset.ContentSourceUrl = resolvedUrl;
                        updated++;
                    }
                }

                if (updated > 0)
                    await directorDb.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InkContentSyncService: error updating content source URLs for location {LocationId}", location.LocationId);
            }
        }

        if (updated > 0)
            _logger.LogInformation("InkContentSyncService: updated {Count} asset ContentSourceUrl values", updated);
    }

    /// <summary>
    /// Parses content source configuration from a location's ExtendedData JSON.
    /// </summary>
    internal static ContentSourceConfig? GetContentSourceConfig(TblLocation location)
    {
        if (string.IsNullOrEmpty(location.ExtendedData)) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(location.ExtendedData);
            if (!doc.RootElement.TryGetProperty("contentSource", out var csProp))
                return null;

            var mode = csProp.TryGetProperty("mode", out var modeProp) ? modeProp.GetString() : null;
            int? connectorId = csProp.TryGetProperty("connectorId", out var cidProp) && cidProp.ValueKind == System.Text.Json.JsonValueKind.Number
                ? cidProp.GetInt32() : null;

            return new ContentSourceConfig { Mode = mode ?? "disabled", ConnectorId = connectorId };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Merges content source configuration into a location's ExtendedData JSON.
    /// </summary>
    internal static void SetContentSourceConfig(TblLocation location, string mode, int? connectorId)
    {
        Dictionary<string, object>? existing = null;
        if (!string.IsNullOrEmpty(location.ExtendedData))
        {
            try
            {
                existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(location.ExtendedData);
            }
            catch { }
        }
        existing ??= new Dictionary<string, object>();

        if (mode == "disabled")
        {
            existing.Remove("contentSource");
        }
        else
        {
            var cs = new Dictionary<string, object> { ["mode"] = mode };
            if (connectorId.HasValue)
                cs["connectorId"] = connectorId.Value;
            existing["contentSource"] = cs;
        }

        location.ExtendedData = existing.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(existing)
            : null;
    }

    internal class ContentSourceConfig
    {
        public string Mode { get; set; } = "disabled";
        public int? ConnectorId { get; set; }
    }

    private DXSuite.Shared.Services.PlatformConnectionConfig? DeserializeConfig(string? configJson)
    {
        return _configProtector.Decrypt(configJson);
    }
}
