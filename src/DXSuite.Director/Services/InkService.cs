using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services
{
    public class InkService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly ILogger<InkService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public InkService(HttpClient httpClient, IConfiguration config, ILogger<InkService> logger)
        {
            _httpClient = httpClient;
            _apiKey = config["Ink:ApiKey"] ?? string.Empty;
            _baseUrl = config["Ink:BaseUrl"] ?? string.Empty;
            _logger = logger;
        }

        public async Task<bool> DeleteInkFileFromMetadataAsync(string metadataPath)
        {
            if (!File.Exists(metadataPath))
                return false;

            string? fileId = await GetInkIdentifierFromMetadata(metadataPath);

            if (string.IsNullOrWhiteSpace(fileId))
                return false;

            return await DeleteFileAsync(fileId);
        }

        public async Task<bool> DeleteFileAsync(string fileId)
        {
            var request = CreateRequest(HttpMethod.Delete, $"api/files/v5/{Uri.EscapeDataString(fileId)}");
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// Updates metadata on an existing Ink file via JSON Patch (RFC 6902).
        /// Ink requires application/json-patch+json with operations on /metadata/* paths.
        /// All values must be strings per the Ink API spec.
        /// </summary>
        public async Task<bool> UpdateInkFileMetadataAsync(string fileId, Dictionary<string, object> metadata)
        {
            try
            {
                var patchOps = metadata.Select(kvp => new
                {
                    op = "replace",
                    path = $"/metadata/{kvp.Key}",
                    value = kvp.Value is string s ? s : JsonSerializer.Serialize(kvp.Value, JsonOptions)
                });

                var json = JsonSerializer.Serialize(patchOps, JsonOptions);

                var request = CreateRequest(HttpMethod.Patch, $"api/files/v5/{Uri.EscapeDataString(fileId)}");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Ink metadata PATCH failed for file {FileId}: {Status} {Body}",
                        fileId, response.StatusCode, body);
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating Ink file metadata for {FileId}", fileId);
                return false;
            }
        }

        /// <summary>
        /// Lists all folders in the Ink account.
        /// </summary>
        public async Task<List<InkFolder>> ListFoldersAsync()
        {
            var folders = new List<InkFolder>();
            string? continuationToken = null;

            try
            {
                do
                {
                    var path = "api/folders/v5";
                    if (!string.IsNullOrEmpty(continuationToken))
                        path += $"?continuationToken={Uri.EscapeDataString(continuationToken)}";

                    var request = CreateRequest(HttpMethod.Get, path);
                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var trimmed = content.TrimStart();

                    if (trimmed.StartsWith('['))
                    {
                        var list = JsonSerializer.Deserialize<List<InkFolder>>(content, JsonOptions);
                        if (list != null) folders.AddRange(list);
                        break;
                    }
                    else
                    {
                        var page = JsonSerializer.Deserialize<InkFolderListResponse>(content, JsonOptions);
                        if (page?.Contents != null) folders.AddRange(page.Contents);
                        continuationToken = page?.ContinuationToken;
                    }
                } while (!string.IsNullOrEmpty(continuationToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list Ink folders");
            }

            return folders;
        }

        /// <summary>
        /// Lists files in a specific Ink folder.
        /// </summary>
        public async Task<List<InkFile>> ListFolderContentsAsync(string folderId)
        {
            var files = new List<InkFile>();
            string? continuationToken = null;

            try
            {
                do
                {
                    var path = $"api/folders/v5/{Uri.EscapeDataString(folderId)}/contents";
                    if (!string.IsNullOrEmpty(continuationToken))
                        path += $"?continuationToken={Uri.EscapeDataString(continuationToken)}";

                    var request = CreateRequest(HttpMethod.Get, path);
                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var trimmed = content.TrimStart();

                    if (trimmed.StartsWith('['))
                    {
                        var list = JsonSerializer.Deserialize<List<InkFile>>(content, JsonOptions);
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
        /// Uploads a file to a specific Ink folder with optional metadata.
        /// </summary>
        public async Task<InkFile?> UploadFileToFolderAsync(string folderId, Stream file, string fileName, Dictionary<string, object>? metadata = null)
        {
            try
            {
                var request = CreateRequest(HttpMethod.Post, "api/files/v5/upload");

                using var formContent = new MultipartFormDataContent();
                var streamContent = new StreamContent(file);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                formContent.Add(streamContent, "file", fileName);
                formContent.Add(new StringContent(folderId), "folderId");

                if (metadata != null)
                {
                    var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);
                    formContent.Add(new StringContent(metadataJson), "metadata");
                }

                request.Content = formContent;
                // Remove the default Accept: application/json since we're posting multipart
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<InkFile>(responseContent, JsonOptions);
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Ink file upload failed: {Status} {Body}", response.StatusCode, body);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file {FileName} to Ink folder {FolderId}", fileName, folderId);
                return null;
            }
        }

        public async Task<string?> GetInkIdentifierFromMetadata(string metadataPath)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(metadataPath);
                var nodes = doc.SelectNodes("usermetadata/field");

                foreach (XmlNode node in nodes)
                {
                    var nameAttr = node.Attributes["name"];
                    var valueAttr = node.Attributes["value"];
                    if (nameAttr != null && nameAttr.Value == "Identifier" && valueAttr != null)
                    {
                        return valueAttr.Value;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        public void RemoveInkIdentifierFromMetadata(string metadataPath)
        {
            if (!File.Exists(metadataPath)) return;

            XmlDocument doc = new XmlDocument();
            doc.Load(metadataPath);

            XmlNodeList fields = doc.SelectNodes("//usermetadata/field");
            foreach (XmlNode field in fields)
            {
                if (field.Attributes?["name"]?.Value == "Identifier")
                {
                    field.ParentNode?.RemoveChild(field);
                    break;
                }
            }

            doc.Save(metadataPath);
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
        {
            var baseUrl = _baseUrl.TrimEnd('/') + "/";
            var request = new HttpRequestMessage(method, $"{baseUrl}{relativePath}");

            if (!string.IsNullOrEmpty(_apiKey))
            {
                request.Headers.Add("Ink-Api-Key", _apiKey);
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        // Ink Files API v5 response models

        public class InkFile
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("folderId")]
            public string? FolderId { get; set; }

            [JsonPropertyName("metadata")]
            public JsonElement? Metadata { get; set; }

            [JsonPropertyName("size")]
            public long? Size { get; set; }

            [JsonPropertyName("contentType")]
            public string? ContentType { get; set; }
        }

        public class InkFolder
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("parentFolderId")]
            public string? ParentFolderId { get; set; }
        }

        private class InkFolderListResponse
        {
            [JsonPropertyName("contents")]
            public List<InkFolder>? Contents { get; set; }

            [JsonPropertyName("continuationToken")]
            public string? ContinuationToken { get; set; }
        }

        private class InkFileListResponse
        {
            [JsonPropertyName("contents")]
            public List<InkFile>? Contents { get; set; }

            [JsonPropertyName("continuationToken")]
            public string? ContinuationToken { get; set; }
        }
    }
}
