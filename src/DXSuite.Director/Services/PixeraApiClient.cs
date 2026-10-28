using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using DXSuite.Shared.Services;

namespace DXSuite.Director.Services
{
    public class PixeraApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _rpcUrl;
        private readonly ILogger? _logger;

        public PixeraApiClient(string rpcUrl, HttpClient httpClient, ILogger? logger = null)
        {
            _rpcUrl    = rpcUrl;
            _httpClient = httpClient;
            _logger    = logger;
        }

        private async Task<JToken> CallAsync(string method, JObject parameters, CancellationToken ct = default)
        {
            var requestBody = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters,
                ["id"] = 1
            };

            var content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_rpcUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync(ct);
            var responseJson = JObject.Parse(responseString);

            // Pixera returns HTTP 200 for both success and API-level errors; the "error" field
            // distinguishes them. Without this check, errors are silently treated as success.
            if (responseJson["error"] is JToken error && error.Type != JTokenType.Null)
            {
                var code    = error["code"]?.Value<int>();
                var message = error["message"]?.ToString() ?? "unknown error";
                throw new InvalidOperationException(
                    $"Pixera JSON-RPC error [{code}] calling {method}: {message}");
            }

            return responseJson["result"];
        }

        // Connection test — verifies Pixera JSON-RPC is reachable and responding
        public async Task<bool> GetApiVersionAsync(CancellationToken ct = default)
        {
            try
            {
                // getApiRevision returns the API revision number (even = release, odd = beta)
                var result = await CallAsync("Pixera.Utility.getApiRevision", new JObject(), ct);
                return result != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>One physical Pixera machine from Pixera.LiveSystems.</summary>
        public sealed record PixeraLiveSystem(string Name, string? Ip, string? State, string? MacAddress, bool IsMultiUserMember);

        /// <summary>
        /// Enumerates every machine in the Pixera project: live systems plus
        /// multi-user members (the client machines of a multi-server setup, which
        /// additionally expose a MAC address). Used for device inventory sync and
        /// per-system health probing.
        /// </summary>
        public async Task<IList<PixeraLiveSystem>> GetLiveSystemsAsync(CancellationToken ct = default)
        {
            var systems = new List<PixeraLiveSystem>();

            async Task CollectAsync(string listMethod, string classPrefix, bool isMember)
            {
                var handles = await CallAsync($"Pixera.LiveSystems.{listMethod}", new JObject(), ct);
                foreach (var handle in handles as JArray ?? new JArray())
                {
                    var p = new JObject { ["handle"] = handle };
                    var name = (await CallAsync($"{classPrefix}.getName", p, ct))?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    async Task<string?> TryGetAsync(string method)
                    {
                        try { return (await CallAsync(method, p, ct))?.ToString(); }
                        catch { return null; }
                    }

                    // State/IP/MAC failures degrade to nulls — a half-answering
                    // system should still appear in the inventory.
                    var ip    = await TryGetAsync($"{classPrefix}.getIp");
                    var state = await TryGetAsync($"{classPrefix}.getState");
                    var mac   = isMember ? await TryGetAsync($"{classPrefix}.getMacAddress") : null;

                    systems.Add(new PixeraLiveSystem(name, ip, state, mac, isMember));
                }
            }

            await CollectAsync("getLiveSystems", "Pixera.LiveSystems.LiveSystem", isMember: false);
            try
            {
                await CollectAsync("getMultiUserMembers", "Pixera.LiveSystems.MultiUserMember", isMember: true);
            }
            catch
            {
                // Single-server projects may not expose the multi-user surface at all.
            }

            return systems;
        }

        /// <summary>
        /// Raw performance JSON for the named live system (shape is undocumented —
        /// captured to logs for empirical discovery, not parsed).
        /// </summary>
        public async Task<string?> GetPerformanceJsonAsync(string systemName, CancellationToken ct = default)
        {
            var handles = await CallAsync("Pixera.LiveSystems.getLiveSystems", new JObject(), ct);
            foreach (var handle in handles as JArray ?? new JArray())
            {
                var p = new JObject { ["handle"] = handle };
                var name = (await CallAsync("Pixera.LiveSystems.LiveSystem.getName", p, ct))?.ToString();
                if (!string.Equals(name, systemName, StringComparison.OrdinalIgnoreCase)) continue;
                p["filter"] = "system,outputsTotal";
                return (await CallAsync("Pixera.LiveSystems.LiveSystem.getPerformanceMonitoringValuesJsonEx", p, ct))?.ToString();
            }
            return null;
        }

        // Folder inventory — lists all resource folders recursively for SyncFolderInventoryAsync
        public async Task<IList<PlatformFolder>> ListResourceFoldersAsync(CancellationToken ct = default)
        {
            var topLevelHandles = await CallAsync("Pixera.Resources.getResourceFolders", new JObject(), ct);
            var folders = new List<PlatformFolder>();

            if (topLevelHandles is JArray handleArray)
            {
                foreach (var handle in handleArray)
                {
                    await EnumerateFolderAsync(folders, handle, ct);
                }
            }

            return folders;
        }

        // Recursively enumerates a folder and all its subfolders into the result list
        private async Task EnumerateFolderAsync(List<PlatformFolder> folders, JToken handle, CancellationToken ct)
        {
            try
            {
                var name = await CallAsync("Pixera.Resources.ResourceFolder.getName",
                    new JObject { ["handle"] = handle }, ct);
                // getInstancePath returns the full path used by getResourceFolderWithNamePath
                var instancePath = await CallAsync("Pixera.Resources.ResourceFolder.getInstancePath",
                    new JObject { ["handle"] = handle }, ct);

                var path = instancePath?.ToString() ?? "";
                folders.Add(new PlatformFolder
                {
                    FolderId = path,
                    FolderName = name?.ToString() ?? path
                });

                // Recurse into subfolders
                var children = await CallAsync("Pixera.Resources.ResourceFolder.getResourceFolders",
                    new JObject { ["handle"] = handle }, ct);
                if (children is JArray childArray)
                {
                    foreach (var child in childArray)
                    {
                        await EnumerateFolderAsync(folders, child, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "PixeraApiClient: skipping folder with handle {Handle} — enumeration failed", handle);
            }
        }

        // Resource listing — returns handle/name pairs for all resources in a folder
        public async Task<List<(JToken Handle, string Name)>> ListFolderResourcesAsync(
            JToken folderHandle, CancellationToken ct = default)
        {
            var handles = await CallAsync("Pixera.Resources.ResourceFolder.getResources",
                new JObject { ["handle"] = folderHandle }, ct);

            var resources = new List<(JToken Handle, string Name)>();

            if (handles is JArray handleArray)
            {
                foreach (var handle in handleArray)
                {
                    try
                    {
                        var name = await CallAsync("Pixera.Resources.Resource.getName",
                            new JObject { ["handle"] = handle }, ct);
                        resources.Add((handle, name?.ToString() ?? ""));
                    }
                    catch { /* Skip resources that cannot be queried */ }
                }
            }

            return resources;
        }

        /// <summary>
        /// Enumerates every resource in the project (top-level plus all folders,
        /// recursively) and returns those that expose a web URL. Non-web resources
        /// have no URL and are skipped, as is any resource or folder whose queries
        /// fail — a partial walk is more useful than no walk.
        /// </summary>
        public async Task<List<(JToken Handle, string Url)>> GetWebResourceUrlsAsync(CancellationToken ct = default)
        {
            var handles = new List<JToken>();
            try
            {
                if (await CallAsync("Pixera.Resources.getResources", new JObject(), ct) is JArray top)
                    handles.AddRange(top);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "PixeraApiClient: top-level resource enumeration failed");
            }

            try
            {
                if (await CallAsync("Pixera.Resources.getResourceFolders", new JObject(), ct) is JArray folders)
                    foreach (var folder in folders)
                        await CollectFolderResourceHandlesAsync(handles, folder, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "PixeraApiClient: folder resource enumeration failed");
            }

            var results = new List<(JToken Handle, string Url)>();
            foreach (var handle in handles)
            {
                try
                {
                    var url = (await CallAsync("Pixera.Resources.Resource.getUrl",
                        new JObject { ["handle"] = handle }, ct))?.ToString();
                    if (!string.IsNullOrWhiteSpace(url)) results.Add((handle, url));
                }
                catch { /* not a web resource */ }
            }
            return results;
        }

        private async Task CollectFolderResourceHandlesAsync(List<JToken> handles, JToken folderHandle, CancellationToken ct)
        {
            try
            {
                if (await CallAsync("Pixera.Resources.ResourceFolder.getResources",
                        new JObject { ["handle"] = folderHandle }, ct) is JArray res)
                    handles.AddRange(res);
                if (await CallAsync("Pixera.Resources.ResourceFolder.getResourceFolders",
                        new JObject { ["handle"] = folderHandle }, ct) is JArray children)
                    foreach (var child in children)
                        await CollectFolderResourceHandlesAsync(handles, child, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "PixeraApiClient: skipping folder {Handle} during resource walk", folderHandle);
            }
        }

        /// <summary>
        /// Re-asserts a web resource's URL, forcing its browser instance to run a
        /// fresh navigation (the recovery lever for layers stuck on an error page).
        /// </summary>
        public Task SetResourceUrlAsync(JToken handle, string url, CancellationToken ct = default) =>
            CallAsync("Pixera.Resources.Resource.setUrl",
                new JObject { ["handle"] = handle, ["url"] = url }, ct);

        // Layer inventory — enumerates all timelines and their layers for the Settings modal dropdown
        public async Task<IList<PlatformFolder>> ListLayersAsync(CancellationToken ct = default)
        {
            var handles = await CallAsync("Pixera.Timelines.getTimelines", new JObject(), ct);
            var layers = new List<PlatformFolder>();

            if (handles is JArray arr)
            {
                foreach (var h in arr)
                {
                    try
                    {
                        var name = (await CallAsync("Pixera.Timelines.Timeline.getName",
                            new JObject { ["handle"] = h }, ct))?.ToString() ?? "";
                        var layerNames = await CallAsync("Pixera.Timelines.Timeline.getLayerNames",
                            new JObject { ["handle"] = h }, ct);
                        if (layerNames is JArray ln)
                        {
                            foreach (var l in ln)
                            {
                                layers.Add(new PlatformFolder
                                {
                                    // FolderId is the dot-path used by Compound.assignResourceToLayer
                                    FolderId   = $"{name}.{l}",
                                    // FolderName is the human-readable label shown in the UI
                                    FolderName = $"{name} / {l}"
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "PixeraApiClient: skipping timeline {Handle}", h);
                    }
                }
            }

            return layers;
        }

        // Timeline inventory — names only; the timeline watchdog and settings UI
        // address timelines by name via the Compound transport calls below.
        public async Task<List<string>> ListTimelineNamesAsync(CancellationToken ct = default)
        {
            var handles = await CallAsync("Pixera.Timelines.getTimelines", new JObject(), ct);
            var names = new List<string>();
            if (handles is JArray arr)
            {
                foreach (var h in arr)
                {
                    try
                    {
                        var name = (await CallAsync("Pixera.Timelines.Timeline.getName",
                            new JObject { ["handle"] = h }, ct))?.ToString();
                        if (!string.IsNullOrEmpty(name)) names.Add(name);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "PixeraApiClient: skipping timeline {Handle}", h);
                    }
                }
            }
            return names;
        }

        // Timeline transport modes: 1 = Play, 2 = Pause, 3 = Stop.
        public async Task<int?> GetTransportModeOnTimelineAsync(string timelineName, CancellationToken ct = default)
        {
            var result = await CallAsync("Pixera.Compound.getTransportModeOnTimeline",
                new JObject { ["timelineName"] = timelineName }, ct);
            return result == null || result.Type == JTokenType.Null ? null : result.Value<int>();
        }

        public async Task SetTransportModeOnTimelineAsync(string timelineName, int mode, CancellationToken ct = default)
            => await CallAsync("Pixera.Compound.setTransportModeOnTimeline",
                new JObject { ["timelineName"] = timelineName, ["mode"] = mode }, ct);

        // Layer assignment — maps a resource to a composition layer for video wall scheduling
        public async Task AssignResourceToLayerAsync(
            string resourcePath, string layerPath, CancellationToken ct = default)
        {
            _logger?.LogDebug(
                "PixeraApiClient: assignResourceToLayer resourcePath={ResourcePath} layerPath={LayerPath}",
                resourcePath, layerPath);
            var parameters = new JObject
            {
                ["resourcePath"] = resourcePath,
                ["layerPath"]    = layerPath
            };
            await CallAsync("Pixera.Compound.assignResourceToLayer", parameters, ct);
        }

        // Transport mode — sets playback mode (1 = play) and loop flag on a composition layer
        public async Task SetTransportModeOnLayerAsync(
            string layerPath, int mode, bool loop, CancellationToken ct = default)
        {
            var parameters = new JObject
            {
                ["layerPath"] = layerPath,
                ["mode"]      = mode,
                ["loop"]      = loop
            };
            await CallAsync("Pixera.Compound.setTransportModeOnLayer", parameters, ct);
        }

        // ── Timeline-governed (clip-level) assignment ────────────────────────
        // For layers whose playback is governed by the Pixera timeline (clip play
        // mode "Lock to time"), media must be swapped on the programmed clip rather
        // than live-assigned to the layer: any live assignment — the API's
        // assignResourceToLayer or a manual drag in the Pixera UI — creates a
        // "domination" (live override) state that detaches the layer from timeline
        // sync until it is explicitly reset.

        public async Task<JToken?> GetLayerHandleAsync(string layerPath, CancellationToken ct = default)
        {
            var parameters = new JObject { ["instancePath"] = layerPath };
            return await CallAsync("Pixera.Timelines.Layer.getHandleFromInstancePath", parameters, ct);
        }

        public async Task<JToken?> GetClipAtIndexAsync(JToken layerHandle, int index, CancellationToken ct = default)
        {
            var parameters = new JObject { ["handle"] = layerHandle, ["index"] = index };
            return await CallAsync("Pixera.Timelines.Layer.getClipAtIndex", parameters, ct);
        }

        public async Task<string?> GetClipAssignedResourceNameAsync(JToken clipHandle, CancellationToken ct = default)
        {
            var name = await CallAsync("Pixera.Timelines.Clip.getAssignedResourceName",
                new JObject { ["handle"] = clipHandle }, ct);
            return name == null || name.Type == JTokenType.Null ? null : name.ToString();
        }

        // Resolves a resource id (Resource.getId — the value Clip.assignResource expects)
        // from a folder name path plus resource name. Returns null when not found.
        public async Task<double?> GetResourceIdAsync(string folderNamePath, string resourceName, CancellationToken ct = default)
        {
            var folder = await CallAsync("Pixera.Resources.getResourceFolderWithNamePath",
                new JObject { ["namePath"] = folderNamePath }, ct);
            if (folder == null || folder.Type == JTokenType.Null) return null;

            var resource = await CallAsync("Pixera.Resources.ResourceFolder.getResourceByName",
                new JObject { ["handle"] = folder, ["name"] = resourceName }, ct);
            if (resource == null || resource.Type == JTokenType.Null) return null;

            var id = await CallAsync("Pixera.Resources.Resource.getId",
                new JObject { ["handle"] = resource }, ct);
            return id == null || id.Type == JTokenType.Null ? null : id.Value<double>();
        }

        // setToResourceDuration=false preserves the clip's existing duration/position
        // (e.g. a 24-hour looping block) and only swaps the media.
        public async Task AssignResourceToClipAsync(
            JToken clipHandle, double resourceId, bool setToResourceDuration, CancellationToken ct = default)
        {
            var parameters = new JObject
            {
                ["handle"]                = clipHandle,
                ["resId"]                 = resourceId,
                ["setToResourceDuration"] = setToResourceDuration
            };
            await CallAsync("Pixera.Timelines.Clip.assignResource", parameters, ct);
        }

        // Clears the dominant (live override) state of the layer's transport parameter.
        public async Task ResetLayerTransportModeAsync(JToken layerHandle, CancellationToken ct = default)
            => await CallAsync("Pixera.Timelines.Layer.resetTransportMode",
                new JObject { ["handle"] = layerHandle }, ct);

        // Clears a live layer-level resource assignment so the programmed clip shows through.
        public async Task ResetLayerAssignedResourceAsync(JToken layerHandle, CancellationToken ct = default)
            => await CallAsync("Pixera.Timelines.Layer.resetAssignedResource",
                new JObject { ["handle"] = layerHandle }, ct);

        // Adds a file to a named resource folder — preferred upload method as it takes the
        // folder name path directly and does not require resolving a handle first
        public async Task AddResourceToFolderAsync(string namePath, string filePath, CancellationToken ct = default)
        {
            var parameters = new JObject
            {
                ["namePath"] = namePath,
                ["filePath"] = filePath
            };
            await CallAsync("Pixera.Compound.addResourceToFolder", parameters, ct);
        }

        public async Task<JToken> GetResourceFolderWithNamePathAsync(string namePath)
        {
            var parameters = new JObject { ["namePath"] = namePath };
            return await CallAsync("Pixera.Resources.getResourceFolderWithNamePath", parameters);
        }

        public async Task CreateFoldersFromAsync(JToken rootFolderHandle, string path)
        {
            var parameters = new JObject
            {
                ["handle"] = rootFolderHandle,
                ["path"] = path
            };
            await CallAsync("Pixera.Resources.ResourceFolder.createFoldersFrom", parameters);
        }

        public async Task AddResourceAsync(JToken folderHandle, string filePath)
        {
            var parameters = new JObject
            {
                ["handle"] = folderHandle,
                ["path"] = filePath
            };
            await CallAsync("Pixera.Resources.ResourceFolder.addResource", parameters);
        }

        public async Task<JToken> GetResourceByPathAsync(string resourcePath)
        {
            // Stub — use ListFolderResourcesAsync + name matching instead
            return null;
        }

        public async Task RemoveResourceFromUIAsync(JToken resourceHandle)
        {
            var parameters = new JObject { ["handle"] = resourceHandle };
            await CallAsync("Pixera.Resources.Resource.removeThis", parameters);
        }

        public async Task DeleteResourceIncludingAssetsAsync(JToken resourceHandle)
        {
            var parameters = new JObject { ["handle"] = resourceHandle };
            await CallAsync("Pixera.Resources.Resource.removeThisIncludingAssets", parameters);
        }
    }
}
