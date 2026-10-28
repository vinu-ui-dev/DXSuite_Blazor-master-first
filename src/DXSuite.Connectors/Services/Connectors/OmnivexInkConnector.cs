using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Connectors;

/// <summary>
/// Cloud platform connector for Omnivex Ink CMS.
/// Implements device inventory sync, status monitoring, and remote actions via the Ink REST API v5.
/// Core entity in Ink is a "Display" — managed through /api/displays/v5 endpoints.
/// Auth is via the Ink-Api-Key header (base64-encoded key from Ink portal).
/// </summary>
public class OmnivexInkConnector : ICloudPlatformConnector
{
    public string PlatformId => "omnivex-ink";
    public string DisplayName => "Omnivex Ink";

    private readonly HttpClient _httpClient;
    private readonly ILogger<OmnivexInkConnector> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OmnivexInkConnector(HttpClient httpClient, ILogger<OmnivexInkConnector> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IList<PlatformDevice>> SyncDeviceInventoryAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        var devices = new List<PlatformDevice>();

        try
        {
            // Ink paginates via continuationToken; loop until all displays are fetched
            string? continuationToken = null;

            do
            {
                var path = "api/displays/v5?fields=id,name,description,status,assignedAppId,assignedAppName,properties,location";
                if (!string.IsNullOrEmpty(continuationToken))
                    path += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";

                var request = CreateRequest(HttpMethod.Get, config, path);
                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                List<InkDisplay>? displayList = null;
                string? nextToken = null;

                var trimmed = content.TrimStart();
                if (trimmed.StartsWith('['))
                {
                    // Flat array response
                    displayList = JsonSerializer.Deserialize<List<InkDisplay>>(content, JsonOptions);
                }
                else
                {
                    // Paginated wrapper response
                    var page = JsonSerializer.Deserialize<InkDisplayListResponse>(content, JsonOptions);
                    displayList = page?.Contents;
                    nextToken = page?.ContinuationToken;
                }

                if (displayList != null)
                {
                    foreach (var display in displayList)
                    {
                        devices.Add(MapToPlatformDevice(display));
                    }
                }

                continuationToken = nextToken;

            } while (!string.IsNullOrEmpty(continuationToken));

            _logger.LogInformation("Synced {Count} displays from Omnivex Ink at {BaseUrl}", devices.Count, config.BaseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync device inventory from Omnivex Ink at {BaseUrl}", config.BaseUrl);
            throw;
        }

        return devices;
    }

    public async Task<PlatformDeviceState> GetDeviceStateAsync(PlatformConnectionConfig config, string platformDeviceId, CancellationToken cancellationToken = default)
    {
        var state = new PlatformDeviceState { PlatformDeviceId = platformDeviceId };

        try
        {
            var request = CreateRequest(HttpMethod.Get, config,
                $"api/displays/v5/{Uri.EscapeDataString(platformDeviceId)}?fields=id,name,status,assignedAppId,assignedAppName,properties,maintenance,syncpack,upgrade,updated,updatedDateTime");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var display = JsonSerializer.Deserialize<InkDisplay>(content, JsonOptions);

            if (display != null)
            {
                state.IsOnline = string.Equals(display.Status, "online", StringComparison.OrdinalIgnoreCase);
                state.LastSeen = display.UpdatedDateTime;
                state.StateData = new Dictionary<string, object>
                {
                    ["status"] = display.Status ?? "unknown",
                    ["assignedAppName"] = display.AssignedAppName ?? string.Empty,
                    ["assignedAppId"] = display.AssignedAppId ?? string.Empty
                };

                if (display.Properties != null)
                    state.StateData["properties"] = display.Properties;
                if (display.Maintenance != null)
                    state.StateData["maintenance"] = display.Maintenance;
                if (display.Syncpack != null)
                    state.StateData["syncpack"] = display.Syncpack;
                if (display.Upgrade != null)
                    state.StateData["upgrade"] = display.Upgrade;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get device state for Ink display {DisplayId}", platformDeviceId);
            state.StateData["error"] = ex.Message;
        }

        return state;
    }

    /// <summary>
    /// Ink does not expose a GET endpoint for screenshots/thumbnails.
    /// Thumbnails are uploaded TO Ink by the display client via POST /api/displays/v5/{id}/thumbnail.
    /// This method returns null since we cannot pull screenshots on demand.
    /// </summary>
    public Task<byte[]?> GetScreenshotAsync(PlatformConnectionConfig config, string platformDeviceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Screenshot retrieval not supported by Ink API — thumbnails are push-only from display clients");
        return Task.FromResult<byte[]?>(null);
    }

    public PlatformCapabilities GetCapabilities()
    {
        return new PlatformCapabilities
        {
            CanListDevices = true,
            CanGetDeviceState = true,
            CanGetScreenshot = false,
            CanDeployContent = true,
            CanRebootDevice = false,
            CanPowerControl = false,
            SupportedCommands = new List<string>
            {
                "send-action",
                "assign-syncpack",
                "send-test-image"
            },
            ImportableResources = new List<ImportableResourceType>
            {
                new("devices", "Displays"),
                new("folders", "Folders")
            }
        };
    }

    public async Task<CommandResult> ExecuteCommandAsync(PlatformConnectionConfig config, string platformDeviceId, string commandId, Dictionary<string, object>? parameters = null, CancellationToken cancellationToken = default)
    {
        var result = new CommandResult
        {
            CommandId = commandId,
            RoutedVia = "cloud-platform:omnivex-ink"
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            switch (commandId.ToLowerInvariant())
            {
                case "send-action":
                    result = await SendActionAsync(config, platformDeviceId, parameters, cancellationToken);
                    break;

                case "assign-syncpack":
                    result = await AssignSyncpackAsync(config, platformDeviceId, parameters, cancellationToken);
                    break;

                case "send-test-image":
                    result = await SendTestImageAsync(config, platformDeviceId, parameters, cancellationToken);
                    break;

                default:
                    result.ErrorMessage = $"Command '{commandId}' is not supported by the Omnivex Ink connector";
                    break;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error executing command {CommandId} on Ink display {DisplayId}", commandId, platformDeviceId);
        }
        finally
        {
            sw.Stop();
            result.CommandId = commandId;
            result.RoutedVia = "cloud-platform:omnivex-ink";
            result.ResultData ??= new Dictionary<string, object>();
            result.ResultData["elapsedMs"] = sw.ElapsedMilliseconds;
        }

        return result;
    }

    public async Task<bool> TestConnectionAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the displays list endpoint with maxCount=1 as a lightweight connectivity check
            var request = CreateRequest(HttpMethod.Get, config, "api/displays/v5?maxCount=1&fields=id");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Omnivex Ink connection test failed for {BaseUrl}", config.BaseUrl);
            return false;
        }
    }

    /// <summary>
    /// Sends a remote action to one or more displays via POST /api/displays/v5/actions.
    /// Requires "actionName" parameter. Optionally accepts "displayIds" (comma-separated) for multi-target.
    /// </summary>
    private async Task<CommandResult> SendActionAsync(PlatformConnectionConfig config, string platformDeviceId, Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var result = new CommandResult { CommandId = "send-action" };

        var actionName = parameters?.TryGetValue("actionName", out var an) == true ? an?.ToString() : null;
        if (string.IsNullOrEmpty(actionName))
        {
            result.ErrorMessage = "Parameter 'actionName' is required for send-action command";
            return result;
        }

        var payload = new
        {
            displayIds = new[] { platformDeviceId },
            actionName
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var request = CreateRequest(HttpMethod.Post, config, "api/displays/v5/actions");
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.ResultData = new Dictionary<string, object>
            {
                ["response"] = $"Action '{actionName}' sent to display {platformDeviceId}"
            };
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            result.ErrorMessage = $"Action failed ({response.StatusCode}): {body}";
        }

        return result;
    }

    /// <summary>
    /// Assigns a syncpack to one or more displays via POST /api/displays/v5/syncpack.
    /// Requires "syncpackId" parameter.
    /// </summary>
    private async Task<CommandResult> AssignSyncpackAsync(PlatformConnectionConfig config, string platformDeviceId, Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var result = new CommandResult { CommandId = "assign-syncpack" };

        var syncpackId = parameters?.TryGetValue("syncpackId", out var sid) == true ? sid?.ToString() : null;
        if (string.IsNullOrEmpty(syncpackId))
        {
            result.ErrorMessage = "Parameter 'syncpackId' is required for assign-syncpack command";
            return result;
        }

        var payload = new
        {
            displayIds = new[] { platformDeviceId },
            syncpackId
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var request = CreateRequest(HttpMethod.Post, config, "api/displays/v5/syncpack");
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.ResultData = new Dictionary<string, object>
            {
                ["response"] = $"Syncpack '{syncpackId}' assigned to display {platformDeviceId}"
            };
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            result.ErrorMessage = $"Syncpack assignment failed ({response.StatusCode}): {body}";
        }

        return result;
    }

    /// <summary>
    /// Sends a test image to a display via POST /api/displays/v5/{displayId}/image.
    /// Requires "imageBase64" parameter (base64-encoded image data).
    /// </summary>
    private async Task<CommandResult> SendTestImageAsync(PlatformConnectionConfig config, string platformDeviceId, Dictionary<string, object>? parameters, CancellationToken ct)
    {
        var result = new CommandResult { CommandId = "send-test-image" };

        var imageBase64 = parameters?.TryGetValue("imageBase64", out var img) == true ? img?.ToString() : null;
        if (string.IsNullOrEmpty(imageBase64))
        {
            result.ErrorMessage = "Parameter 'imageBase64' is required for send-test-image command";
            return result;
        }

        var imageBytes = Convert.FromBase64String(imageBase64);
        var request = CreateRequest(HttpMethod.Post, config, $"api/displays/v5/{Uri.EscapeDataString(platformDeviceId)}/image");
        request.Content = new ByteArrayContent(imageBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await _httpClient.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
        {
            result.Success = true;
            result.ResultData = new Dictionary<string, object>
            {
                ["response"] = $"Test image sent to display {platformDeviceId}"
            };
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            result.ErrorMessage = $"Test image failed ({response.StatusCode}): {body}";
        }

        return result;
    }

    public async Task<IList<PlatformFolder>> SyncFolderInventoryAsync(PlatformConnectionConfig config, CancellationToken cancellationToken = default)
    {
        var folders = new List<PlatformFolder>();

        try
        {
            string? continuationToken = null;

            do
            {
                var path = "api/folders/v5?fields=id,name";
                if (!string.IsNullOrEmpty(continuationToken))
                    path += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";

                var request = CreateRequest(HttpMethod.Get, config, path);
                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var trimmed = content.TrimStart();

                if (trimmed.StartsWith('['))
                {
                    var list = JsonSerializer.Deserialize<List<InkFolderItem>>(content, JsonOptions);
                    if (list != null)
                        folders.AddRange(list.Select(f => new PlatformFolder
                        {
                            FolderId = f.Id ?? "",
                            FolderName = f.Name ?? "",
                            ParentFolderId = f.ParentFolderId
                        }));
                    break;
                }
                else
                {
                    var page = JsonSerializer.Deserialize<InkFolderListResponse>(content, JsonOptions);
                    if (page?.Contents != null)
                        folders.AddRange(page.Contents.Select(f => new PlatformFolder
                        {
                            FolderId = f.Id ?? "",
                            FolderName = f.Name ?? "",
                            ParentFolderId = f.ParentFolderId
                        }));
                    continuationToken = page?.ContinuationToken;
                }
            } while (!string.IsNullOrEmpty(continuationToken));

            _logger.LogInformation("Synced {Count} folders from Omnivex Ink at {BaseUrl}", folders.Count, config.BaseUrl);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Ink API key does not have folder/content permissions at {BaseUrl}. Enable the Content scope in the Ink portal.", config.BaseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync folder inventory from Omnivex Ink at {BaseUrl}", config.BaseUrl);
        }

        return folders;
    }

    // Ink chunked upload API constraints:
    //   ChunkLength must be >= 2 MB (2097152) and < TotalFileLength and <= 100 MB
    // Files smaller than ~4 MB can't satisfy chunkLength >= 2MB && chunkLength < totalSize,
    // so they use the single-shot upload endpoint instead.
    private const int ChunkSize = 10 * 1024 * 1024;       // 10 MB per chunk
    private const long MinChunkedFileSize = 4 * 1024 * 1024; // 4 MB threshold for chunked upload

    /// <summary>
    /// Uploads a file to an Ink folder. Uses the chunked API for files >= 4 MB,
    /// and the single-shot API for smaller files.
    /// </summary>
    public async Task<(string? FileId, string? Error)> UploadFileToFolderAsync(PlatformConnectionConfig config, string folderId, Stream stream, string fileName, Dictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        try
        {
            var totalLength = stream.Length;

            if (totalLength >= MinChunkedFileSize)
                return await UploadChunkedAsync(config, folderId, stream, fileName, totalLength, metadata, ct);
            else
                return await UploadSingleShotAsync(config, folderId, stream, fileName, totalLength, metadata, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading {FileName} to Ink folder {FolderId}", fileName, folderId);
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Single-shot upload for small files (under 4 MB).
    /// Uses the same header-based approach as chunked init, with file body sent directly.
    /// </summary>
    private async Task<(string? FileId, string? Error)> UploadSingleShotAsync(
        PlatformConnectionConfig config, string folderId, Stream stream,
        string fileName, long totalLength, Dictionary<string, object>? metadata, CancellationToken ct)
    {
        var mimeType = GetMimeType(fileName);
        _logger.LogInformation("Ink single-shot upload: file={FileName}, size={Size}, folder={FolderId}",
            fileName, totalLength, folderId);

        var request = CreateRequest(HttpMethod.Post, config, "api/files/v5/upload");
        using var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        formContent.Add(streamContent, "file", fileName);

        request.Headers.Add("FolderId", folderId);

        // Metadata is applied via PATCH after upload to avoid format incompatibilities
        // between the upload header format and the PATCH body format.

        request.Content = formContent;
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = $"HTTP {(int)response.StatusCode}: {body}";
            _logger.LogWarning("Ink single-shot upload failed for '{FileName}': {Error}", fileName, error);
            return (null, error);
        }

        var fileId = ExtractFileId(body);
        if (fileId == null)
            return (null, $"Upload succeeded but no file ID in response: {body}");

        _logger.LogInformation("Ink upload complete for '{FileName}': fileId={FileId}", fileName, fileId);

        // Apply metadata via PATCH after successful upload
        if (metadata != null && metadata.Count > 0)
        {
            var patched = await PatchFileMetadataAsync(config, fileId, metadata, ct);
            if (!patched)
                _logger.LogWarning("Ink file '{FileName}' uploaded but metadata PATCH failed", fileName);
        }

        return (fileId, null);
    }

    /// <summary>
    /// Chunked upload for large files (>= 4 MB).
    /// Three-step flow: initiate -> upload chunk(s) -> finalize.
    /// </summary>
    private async Task<(string? FileId, string? Error)> UploadChunkedAsync(
        PlatformConnectionConfig config, string folderId, Stream stream,
        string fileName, long totalLength, Dictionary<string, object>? metadata, CancellationToken ct)
    {
        var mimeType = GetMimeType(fileName);
        var chunkLength = (long)ChunkSize;

        _logger.LogInformation("Ink chunked upload: file={FileName}, size={Size}, chunkSize={ChunkSize}, folder={FolderId}",
            fileName, totalLength, chunkLength, folderId);

        // Step 1: Initiate chunked upload
        var initRequest = CreateRequest(HttpMethod.Post, config, "api/files/v5/upload/chunked");
        initRequest.Headers.Add("FileName", fileName);
        initRequest.Headers.Add("ContentType", mimeType);
        initRequest.Headers.Add("TotalFileLength", totalLength.ToString());
        initRequest.Headers.Add("ChunkLength", chunkLength.ToString());
        initRequest.Headers.Add("FolderId", folderId);

        // Metadata is applied via PATCH after finalize to avoid format incompatibilities.

        var initResponse = await _httpClient.SendAsync(initRequest, ct);
        var initBody = await initResponse.Content.ReadAsStringAsync(ct);

        if (!initResponse.IsSuccessStatusCode)
        {
            var error = $"Chunked init failed: HTTP {(int)initResponse.StatusCode}: {initBody}";
            _logger.LogWarning("Ink chunked upload init failed for '{FileName}': {Error}", fileName, error);
            return (null, error);
        }

        var uploadId = ExtractUploadId(initBody);
        if (string.IsNullOrEmpty(uploadId))
            return (null, $"Chunked init succeeded but no uploadId in response: {initBody}");

        _logger.LogDebug("Ink chunked upload initiated: uploadId={UploadId}", uploadId);

        // Step 2: Upload chunks
        var buffer = new byte[chunkLength];
        int chunkIndex = 0;
        long bytesRemaining = totalLength;

        while (bytesRemaining > 0)
        {
            var bytesToRead = (int)Math.Min(chunkLength, bytesRemaining);
            var bytesRead = await ReadFullAsync(stream, buffer, bytesToRead, ct);

            var chunkRequest = CreateRequest(HttpMethod.Patch, config,
                $"api/files/v5/upload/chunked/{Uri.EscapeDataString(uploadId)}");
            chunkRequest.Headers.Add("ChunkIndex", chunkIndex.ToString());
            chunkRequest.Content = new ByteArrayContent(buffer, 0, bytesRead);
            chunkRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var chunkResponse = await _httpClient.SendAsync(chunkRequest, ct);

            if (!chunkResponse.IsSuccessStatusCode)
            {
                var chunkBody = await chunkResponse.Content.ReadAsStringAsync(ct);
                var error = $"Chunk {chunkIndex} failed: HTTP {(int)chunkResponse.StatusCode}: {chunkBody}";
                _logger.LogWarning("Ink chunk upload failed for '{FileName}': {Error}", fileName, error);
                return (null, error);
            }

            bytesRemaining -= bytesRead;
            chunkIndex++;
        }

        _logger.LogDebug("Ink chunked upload: all {ChunkCount} chunks sent for '{FileName}'", chunkIndex, fileName);

        // Step 3: Finalize upload
        var finalRequest = CreateRequest(HttpMethod.Post, config,
            $"api/files/v5/upload/chunked/{Uri.EscapeDataString(uploadId)}");
        var finalResponse = await _httpClient.SendAsync(finalRequest, ct);
        var finalBody = await finalResponse.Content.ReadAsStringAsync(ct);

        if (!finalResponse.IsSuccessStatusCode)
        {
            var error = $"Finalize failed: HTTP {(int)finalResponse.StatusCode}: {finalBody}";
            _logger.LogWarning("Ink chunked upload finalize failed for '{FileName}': {Error}", fileName, error);
            return (null, error);
        }

        var fileId = ExtractFileId(finalBody);
        if (fileId == null)
            return (null, $"Finalize succeeded but no file ID in response: {finalBody}");

        _logger.LogInformation("Ink upload complete for '{FileName}': fileId={FileId}, chunks={Chunks}",
            fileName, fileId, chunkIndex);

        // Apply metadata via PATCH after successful upload
        if (metadata != null && metadata.Count > 0)
        {
            var patched = await PatchFileMetadataAsync(config, fileId, metadata, ct);
            if (!patched)
                _logger.LogWarning("Ink file '{FileName}' uploaded but metadata PATCH failed", fileName);
        }

        return (fileId, null);
    }

    /// <summary>
    /// Reads exactly the requested number of bytes from a stream, handling partial reads.
    /// </summary>
    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    /// <summary>
    /// Extracts the uploadId from the chunked upload initiation response.
    /// </summary>
    private static string? ExtractUploadId(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Try direct property
            if (root.TryGetProperty("uploadId", out var uid))
                return uid.GetString();

            // Try nested in contents
            if (root.TryGetProperty("contents", out var contents))
            {
                if (contents.TryGetProperty("uploadId", out var cuid))
                    return cuid.GetString();
                // The contents itself might be the uploadId string
                if (contents.ValueKind == JsonValueKind.String)
                    return contents.GetString();
                // Try id field
                if (contents.TryGetProperty("id", out var cid))
                    return cid.GetString();
            }

            // Try id at root
            if (root.TryGetProperty("id", out var id))
                return id.GetString();

            // If the body is a plain string (guid), use it directly
            if (root.ValueKind == JsonValueKind.String)
                return root.GetString();
        }
        catch { }

        // Try as plain text guid
        var trimmed = responseBody.Trim().Trim('"');
        if (Guid.TryParse(trimmed, out _))
            return trimmed;

        return null;
    }

    /// <summary>
    /// Extracts the file ID from the finalize response.
    /// </summary>
    private static string? ExtractFileId(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Object)
            {
                if (contents.TryGetProperty("id", out var cid))
                    return cid.GetString();
            }

            if (root.TryGetProperty("id", out var id))
                return id.GetString();
        }
        catch { }

        var trimmed = responseBody.Trim().Trim('"');
        if (Guid.TryParse(trimmed, out _))
            return trimmed;

        return null;
    }

    /// <summary>
    /// Lists file contents of an Ink folder. Used by content sync for diffing.
    /// </summary>
    public async Task<List<InkFileItem>> ListFolderContentsAsync(PlatformConnectionConfig config, string folderId, CancellationToken ct = default)
    {
        var files = new List<InkFileItem>();
        string? continuationToken = null;

        try
        {
            do
            {
                var path = $"api/folders/v5/{Uri.EscapeDataString(folderId)}/contents?fields=id,payload,metadata";
                if (!string.IsNullOrEmpty(continuationToken))
                    path += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";

                var request = CreateRequest(HttpMethod.Get, config, path);
                var response = await _httpClient.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(ct);
                var trimmed = content.TrimStart();

                if (trimmed.StartsWith('['))
                {
                    var list = JsonSerializer.Deserialize<List<InkFileItem>>(content, JsonOptions);
                    if (list != null) files.AddRange(list);
                    break;
                }
                else
                {
                    var page = JsonSerializer.Deserialize<InkFileListResponse>(content, JsonOptions);
                    if (page?.Contents != null) files.AddRange(page.Contents);
                    continuationToken = page?.ContinuationToken;
                }
            } while (!string.IsNullOrEmpty(continuationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Ink folder contents for {FolderId}", folderId);
        }

        return files;
    }

    /// <summary>
    /// Updates metadata on an existing Ink file via JSON Patch (RFC 6902).
    /// Ink requires application/json-patch+json with operations on /metadata/* paths.
    /// Metadata values must be wrapped in {"value": X} per the Ink API spec.
    /// Uses "add" op which creates the field if missing or replaces if present.
    /// </summary>
    public async Task<bool> PatchFileMetadataAsync(PlatformConnectionConfig config, string fileId, Dictionary<string, object> metadata, CancellationToken ct = default)
    {
        try
        {
            var patchOps = new List<object>();
            foreach (var kvp in metadata)
            {
                patchOps.Add(new
                {
                    op = "add",
                    path = $"/metadata/{kvp.Key}",
                    value = new { value = kvp.Value }
                });
            }

            var json = JsonSerializer.Serialize(patchOps, JsonOptions);
            _logger.LogDebug("Ink metadata PATCH payload for {FileId}: {Json}", fileId, json);

            var request = CreateRequest(HttpMethod.Patch, config, $"api/files/v5/{Uri.EscapeDataString(fileId)}");
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json-patch+json");

            var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ink metadata PATCH failed for {FileId}: {Status} {Body}",
                    fileId, (int)response.StatusCode, responseBody);
            }
            else
            {
                _logger.LogDebug("Ink metadata PATCH succeeded for {FileId}: {Status}", fileId, (int)response.StatusCode);
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error patching Ink file metadata for {FileId}", fileId);
            return false;
        }
    }

    /// <summary>
    /// Sets the FileContentsUrl on an Ink file, enabling content source redirection
    /// so displays download from a local server instead of the Ink CDN.
    /// Uses /payload path (plain value, no {"value":X} wrapper).
    /// </summary>
    public async Task<bool> PatchFileContentsUrlAsync(
        PlatformConnectionConfig config, string fileId, string fileContentsUrl, CancellationToken ct = default)
    {
        try
        {
            var patchOps = new object[]
            {
                new { op = "add", path = "/payload/fileContentsUrl", value = fileContentsUrl }
            };

            var json = JsonSerializer.Serialize(patchOps, JsonOptions);
            var request = CreateRequest(HttpMethod.Patch, config, $"api/files/v5/{Uri.EscapeDataString(fileId)}");
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json-patch+json");

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Ink FileContentsUrl PATCH failed for {FileId}: {Status} {Body}",
                    fileId, (int)response.StatusCode, body);
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error patching FileContentsUrl for Ink file {FileId}", fileId);
            return false;
        }
    }

    /// <summary>
    /// Enables or disables file contents URL redirection on an Ink display.
    /// When enabled, the display downloads content from FileContentsUrl instead of the Ink CDN.
    /// Uses /payload path (plain value, no {"value":X} wrapper).
    /// </summary>
    public async Task<bool> PatchDisplayUseFileContentsUrlsAsync(
        PlatformConnectionConfig config, string displayId, bool enabled, CancellationToken ct = default)
    {
        try
        {
            var patchOps = new object[]
            {
                new { op = "add", path = "/payload/useFileContentsUrls", value = enabled }
            };

            var json = JsonSerializer.Serialize(patchOps, JsonOptions);
            var request = CreateRequest(HttpMethod.Patch, config, $"api/displays/v5/{Uri.EscapeDataString(displayId)}");
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json-patch+json");

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Ink UseFileContentsUrls PATCH failed for display {DisplayId}: {Status} {Body}",
                    displayId, (int)response.StatusCode, body);
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error patching UseFileContentsUrls for Ink display {DisplayId}", displayId);
            return false;
        }
    }

    /// <summary>
    /// Deletes a file from Ink by ID. Used by content sync.
    /// </summary>
    public async Task<bool> DeleteFileFromFolderAsync(PlatformConnectionConfig config, string fileId, CancellationToken ct = default)
    {
        try
        {
            var request = CreateRequest(HttpMethod.Delete, config, $"api/files/v5/{Uri.EscapeDataString(fileId)}");
            var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Ink file {FileId}", fileId);
            return false;
        }
    }

    /// <summary>
    /// Replaces an existing Ink file with a new upload, preserving metadata.
    /// Workaround for unreliable PATCH metadata — deletes the old file then re-uploads.
    /// </summary>
    public async Task<(string? FileId, string? Error)> ReplaceFileWithMetadataAsync(
        PlatformConnectionConfig config, string existingFileId,
        string folderId, Stream stream, string fileName,
        Dictionary<string, object>? metadata = null, CancellationToken ct = default)
    {
        await DeleteFileFromFolderAsync(config, existingFileId, ct);
        return await UploadFileToFolderAsync(config, folderId, stream, fileName, metadata, ct);
    }

    internal static string MapInkStatusToReadingStatus(string? inkStatus) =>
        inkStatus?.ToLowerInvariant() switch
        {
            "online" => "healthy",
            "offline" => "critical",
            "warning" => "warning",
            "error" => "critical",
            _ => "unknown"
        };

    /// <summary>
    /// Maps file extensions to MIME types for Ink file uploads.
    /// Ink rejects application/octet-stream — the actual content type is required.
    /// </summary>
    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".mkv" => "video/x-matroska",
            ".pdf" => "application/pdf",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, PlatformConnectionConfig config, string relativePath)
    {
        var baseUrl = config.BaseUrl.TrimEnd('/') + "/";
        var request = new HttpRequestMessage(method, $"{baseUrl}{relativePath}");

        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            request.Headers.Add("Ink-Api-Key", config.ApiKey);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private PlatformDevice MapToPlatformDevice(InkDisplay display)
    {
        var device = new PlatformDevice
        {
            PlatformDeviceId = display.Id ?? string.Empty,
            DeviceName = display.Name ?? string.Empty,
            PlatformMetadata = new Dictionary<string, object>
            {
                ["status"] = display.Status ?? string.Empty,
                ["description"] = display.Description ?? string.Empty,
                ["assignedAppId"] = display.AssignedAppId ?? string.Empty,
                ["assignedAppName"] = display.AssignedAppName ?? string.Empty
            }
        };

        if (display.Location != null)
            device.PlatformMetadata["location"] = display.Location;

        if (display.Properties != null)
        {
            device.PlatformMetadata["properties"] = display.Properties;

            // Ink nests hardware info under properties.device
            if (display.Properties.Value.ValueKind == JsonValueKind.Object
                && display.Properties.Value.TryGetProperty("device", out var deviceProps)
                && deviceProps.ValueKind == JsonValueKind.Object)
            {
                device.IpAddress = TryGetStringProperty(deviceProps, "ipAddress");
                device.SerialNumber = TryGetStringProperty(deviceProps, "deviceUniqueId");
                device.Manufacturer = TryGetStringProperty(deviceProps, "manufacturer");
                device.DeviceModel = TryGetStringProperty(deviceProps, "modelName");
            }

            // Ink nests browser info under properties.browser
            if (display.Properties.Value.TryGetProperty("browser", out var browserProps)
                && browserProps.ValueKind == JsonValueKind.Object)
            {
                device.BrowserType = TryGetStringProperty(browserProps, "type");
                device.BrowserVersion = TryGetStringProperty(browserProps, "version");
            }
        }

        return device;
    }

    /// <summary>
    /// Attempts to read a string property from a JsonElement by trying multiple possible key names.
    /// Ink's property schema varies by display type, so we probe common variants.
    /// </summary>
    private static string? TryGetStringProperty(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var val = prop.GetString();
                if (!string.IsNullOrWhiteSpace(val) && !string.Equals(val, "n/a", StringComparison.OrdinalIgnoreCase))
                    return val;
            }
        }
        return null;
    }

    // Ink Displays v5 API response models

    private class InkDisplayListResponse
    {
        [JsonPropertyName("contents")]
        public List<InkDisplay>? Contents { get; set; }

        [JsonPropertyName("continuationToken")]
        public string? ContinuationToken { get; set; }

        [JsonPropertyName("statusCode")]
        public int? StatusCode { get; set; }
    }

    private class InkDisplay
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("assignedAppId")]
        public string? AssignedAppId { get; set; }

        [JsonPropertyName("assignedAppName")]
        public string? AssignedAppName { get; set; }

        [JsonPropertyName("updated")]
        public string? Updated { get; set; }

        [JsonPropertyName("updatedDateTime")]
        public DateTime? UpdatedDateTime { get; set; }

        [JsonPropertyName("updatedUser")]
        public string? UpdatedUser { get; set; }

        [JsonPropertyName("created")]
        public string? Created { get; set; }

        [JsonPropertyName("createdDateTime")]
        public DateTime? CreatedDateTime { get; set; }

        /// <summary>
        /// Display hardware/software properties (OS, resolution, etc.) — structure varies by display type
        /// </summary>
        [JsonPropertyName("properties")]
        public JsonElement? Properties { get; set; }

        [JsonPropertyName("location")]
        public JsonElement? Location { get; set; }

        [JsonPropertyName("maintenance")]
        public JsonElement? Maintenance { get; set; }

        [JsonPropertyName("syncpack")]
        public JsonElement? Syncpack { get; set; }

        [JsonPropertyName("upgrade")]
        public JsonElement? Upgrade { get; set; }

        [JsonPropertyName("logging")]
        public JsonElement? Logging { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; set; }
    }

    // Ink Folders & Files v5 API response models

    private class InkFolderListResponse
    {
        [JsonPropertyName("contents")]
        public List<InkFolderItem>? Contents { get; set; }

        [JsonPropertyName("continuationToken")]
        public string? ContinuationToken { get; set; }
    }

    internal class InkFolderItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("parentFolderId")]
        public string? ParentFolderId { get; set; }
    }

    private class InkFileListResponse
    {
        [JsonPropertyName("contents")]
        public List<InkFileItem>? Contents { get; set; }

        [JsonPropertyName("continuationToken")]
        public string? ContinuationToken { get; set; }
    }

    public class InkFileItem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? RawName { get; set; }

        [JsonPropertyName("folderId")]
        public string? FolderId { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("contentType")]
        public string? ContentType { get; set; }

        [JsonPropertyName("payload")]
        public InkFilePayload? Payload { get; set; }

        /// <summary>
        /// Resolved filename — Ink nests the actual name under payload.name
        /// </summary>
        [JsonIgnore]
        public string? Name => Payload?.Name ?? RawName;
    }

    public class InkFilePayload
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("fileSize")]
        public long? FileSize { get; set; }
    }

    /// <summary>
    /// Ink API wrapper for single-item responses (e.g., upload, update).
    /// The actual entity is nested under "contents".
    /// </summary>
    private class InkSingleItemResponse<T>
    {
        [JsonPropertyName("statusCode")]
        public int? StatusCode { get; set; }

        [JsonPropertyName("contents")]
        public T? Contents { get; set; }
    }
}
