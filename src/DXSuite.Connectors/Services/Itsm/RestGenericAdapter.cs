using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Itsm;

/// <summary>
/// Configuration-driven ITSM adapter for REST-based platforms. Uses JSON-defined endpoints
/// and field mappings stored in ItsmConnectionInfo to communicate with any REST API
/// without writing custom C# code. Suitable for platforms like Freshservice, Zendesk, Halo, etc.
///
/// The adapter reads its behavior from a JSON configuration blob stored in the Token field
/// of ItsmConnectionInfo (the Server field holds the base URL, UserName/Password hold credentials).
///
/// Configuration JSON schema:
/// {
///   "authType": "basic|bearer|apikey",
///   "apiKeyHeader": "X-Api-Key",           // only for apikey auth
///   "endpoints": {
///     "create":  { "method": "POST", "path": "/api/v2/tickets", "bodyTemplate": "..." },
///     "update":  { "method": "PUT",  "path": "/api/v2/tickets/{{ticketId}}", "bodyTemplate": "..." },
///     "resolve": { "method": "PUT",  "path": "/api/v2/tickets/{{ticketId}}", "bodyTemplate": "..." },
///     "get":     { "method": "GET",  "path": "/api/v2/tickets/{{ticketId}}" },
///     "attach":  { "method": "POST", "path": "/api/v2/tickets/{{ticketId}}/attachments" },
///     "test":    { "method": "GET",  "path": "/api/v2/me" }
///   },
///   "responseMapping": {
///     "ticketId": "$.id",
///     "ticketUrl": "$.html_url",
///     "title": "$.subject",
///     "status": "$.status",
///     "priority": "$.priority",
///     "createdAt": "$.created_at",
///     "updatedAt": "$.updated_at",
///     "resolvedAt": "$.resolved_at"
///   },
///   "priorityMapping": {
///     "critical": "1",
///     "high": "2",
///     "medium": "3",
///     "low": "4"
///   }
/// }
/// </summary>
public partial class RestGenericAdapter : IItsmAdapter
{
    public string PlatformId => "rest-generic";

    private readonly HttpClient _httpClient;
    private readonly IItsmConnectionProvider _connectionProvider;
    private readonly ILogger<RestGenericAdapter> _logger;
    private readonly ItsmCredentialProtector _credentialProtector;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RestGenericAdapter(
        HttpClient httpClient,
        IItsmConnectionProvider connectionProvider,
        ILogger<RestGenericAdapter> logger,
        ItsmCredentialProtector credentialProtector)
    {
        _httpClient = httpClient;
        _connectionProvider = connectionProvider;
        _logger = logger;
        _credentialProtector = credentialProtector;
    }

    public async Task<TicketResult> CreateTicketAsync(TicketRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var (config, adapterConfig) = await LoadConfigAsync(cancellationToken);
            if (config == null || adapterConfig == null)
                return ErrorResult("No REST generic integration configuration found");

            ConfigureAuth(config, adapterConfig);

            var endpoint = GetEndpoint(adapterConfig, "create");
            if (endpoint == null)
                return ErrorResult("No 'create' endpoint configured");

            var templateVars = BuildTemplateVariables(request);
            var body = RenderTemplate(endpoint.BodyTemplate, templateVars);
            var url = BuildUrl(config.Server, RenderTemplate(endpoint.Path, templateVars));

            var httpRequest = new HttpRequestMessage(new HttpMethod(endpoint.Method), url);
            if (!string.IsNullOrWhiteSpace(body))
                httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("REST generic create failed: {StatusCode} {Body}", response.StatusCode, responseBody);
                return ErrorResult($"API returned {(int)response.StatusCode}: {TruncateBody(responseBody)}");
            }

            var responseNode = TryParseJson(responseBody);
            var responseMapping = adapterConfig.ResponseMapping;

            return new TicketResult
            {
                Success = true,
                TicketId = ExtractJsonPath(responseNode, responseMapping?.TicketId)?.ToString(),
                TicketUrl = ExtractJsonPath(responseNode, responseMapping?.TicketUrl)?.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "REST generic create ticket failed");
            return ErrorResult($"Exception: {ex.Message}");
        }
    }

    public async Task<TicketResult> UpdateTicketAsync(string ticketId, TicketUpdate update, CancellationToken cancellationToken = default)
    {
        try
        {
            var (config, adapterConfig) = await LoadConfigAsync(cancellationToken);
            if (config == null || adapterConfig == null)
                return ErrorResult("No REST generic integration configuration found");

            ConfigureAuth(config, adapterConfig);

            var endpoint = GetEndpoint(adapterConfig, "update");
            if (endpoint == null)
                return ErrorResult("No 'update' endpoint configured");

            var templateVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ticketId", ticketId },
                { "comment", update.Comment ?? "" },
                { "priority", MapPriority(adapterConfig, update.Priority) },
                { "status", update.Status ?? "" }
            };

            if (update.UpdatedFields != null)
            {
                foreach (var (key, value) in update.UpdatedFields)
                    templateVars[$"field.{key}"] = value;
            }

            var body = RenderTemplate(endpoint.BodyTemplate, templateVars);
            var url = BuildUrl(config.Server, RenderTemplate(endpoint.Path, templateVars));

            var httpRequest = new HttpRequestMessage(new HttpMethod(endpoint.Method), url);
            if (!string.IsNullOrWhiteSpace(body))
                httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("REST generic update failed for {TicketId}: {StatusCode} {Body}", ticketId, response.StatusCode, responseBody);
                return ErrorResult($"API returned {(int)response.StatusCode}: {TruncateBody(responseBody)}");
            }

            return new TicketResult
            {
                Success = true,
                TicketId = ticketId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "REST generic update ticket failed for {TicketId}", ticketId);
            return ErrorResult($"Exception: {ex.Message}");
        }
    }

    public async Task<TicketResult> ResolveTicketAsync(string ticketId, ItsmResolutionInfo resolution, CancellationToken cancellationToken = default)
    {
        try
        {
            var (config, adapterConfig) = await LoadConfigAsync(cancellationToken);
            if (config == null || adapterConfig == null)
                return ErrorResult("No REST generic integration configuration found");

            ConfigureAuth(config, adapterConfig);

            var endpoint = GetEndpoint(adapterConfig, "resolve");
            if (endpoint == null)
                return ErrorResult("No 'resolve' endpoint configured");

            var templateVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ticketId", ticketId },
                { "resolutionNote", resolution.ResolutionNote },
                { "resolvedBy", resolution.ResolvedBy },
                { "autoResolved", resolution.AutoResolved.ToString().ToLowerInvariant() },
                { "actionsTaken", resolution.ActionsTaken ?? "" }
            };

            var body = RenderTemplate(endpoint.BodyTemplate, templateVars);
            var url = BuildUrl(config.Server, RenderTemplate(endpoint.Path, templateVars));

            var httpRequest = new HttpRequestMessage(new HttpMethod(endpoint.Method), url);
            if (!string.IsNullOrWhiteSpace(body))
                httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("REST generic resolve failed for {TicketId}: {StatusCode} {Body}", ticketId, response.StatusCode, responseBody);
                return ErrorResult($"API returned {(int)response.StatusCode}: {TruncateBody(responseBody)}");
            }

            _logger.LogInformation("REST generic resolved ticket {TicketId}", ticketId);

            return new TicketResult
            {
                Success = true,
                TicketId = ticketId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "REST generic resolve ticket failed for {TicketId}", ticketId);
            return ErrorResult($"Exception: {ex.Message}");
        }
    }

    public async Task<TicketResult> AddAttachmentAsync(string ticketId, TicketAttachment attachment, CancellationToken cancellationToken = default)
    {
        try
        {
            var (config, adapterConfig) = await LoadConfigAsync(cancellationToken);
            if (config == null || adapterConfig == null)
                return ErrorResult("No REST generic integration configuration found");

            ConfigureAuth(config, adapterConfig);

            var endpoint = GetEndpoint(adapterConfig, "attach");
            if (endpoint == null)
                return ErrorResult("No 'attach' endpoint configured");

            var templateVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ticketId", ticketId }
            };

            var url = BuildUrl(config.Server, RenderTemplate(endpoint.Path, templateVars));

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(attachment.Content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(attachment.ContentType);
            form.Add(fileContent, "file", attachment.FileName);

            var response = await _httpClient.PostAsync(url, form, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("REST generic attach failed for {TicketId}: {StatusCode} {Body}", ticketId, response.StatusCode, responseBody);
                return ErrorResult($"Attachment upload failed: {TruncateBody(responseBody)}");
            }

            return new TicketResult
            {
                Success = true,
                TicketId = ticketId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "REST generic attachment failed for {TicketId}", ticketId);
            return ErrorResult($"Exception: {ex.Message}");
        }
    }

    public async Task<TicketInfo> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (config, adapterConfig) = await LoadConfigAsync(cancellationToken);
            if (config == null || adapterConfig == null)
                return new TicketInfo { Found = false, ErrorMessage = "No REST generic integration configuration found" };

            ConfigureAuth(config, adapterConfig);

            var endpoint = GetEndpoint(adapterConfig, "get");
            if (endpoint == null)
                return new TicketInfo { Found = false, ErrorMessage = "No 'get' endpoint configured" };

            var templateVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ticketId", ticketId }
            };

            var url = BuildUrl(config.Server, RenderTemplate(endpoint.Path, templateVars));
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new TicketInfo { TicketId = ticketId, Found = false };

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("REST generic get failed for {TicketId}: {StatusCode} {Body}", ticketId, response.StatusCode, responseBody);
                return new TicketInfo { TicketId = ticketId, Found = false, ErrorMessage = TruncateBody(responseBody) };
            }

            var responseNode = TryParseJson(responseBody);
            var mapping = adapterConfig.ResponseMapping;

            return new TicketInfo
            {
                TicketId = ExtractJsonPath(responseNode, mapping?.TicketId)?.ToString() ?? ticketId,
                TicketUrl = ExtractJsonPath(responseNode, mapping?.TicketUrl)?.ToString(),
                Title = ExtractJsonPath(responseNode, mapping?.Title)?.ToString() ?? string.Empty,
                Status = ExtractJsonPath(responseNode, mapping?.Status)?.ToString() ?? string.Empty,
                Priority = ExtractJsonPath(responseNode, mapping?.Priority)?.ToString() ?? string.Empty,
                CreatedAt = ParseDateValue(ExtractJsonPath(responseNode, mapping?.CreatedAt)?.ToString()),
                UpdatedAt = ParseNullableDateValue(ExtractJsonPath(responseNode, mapping?.UpdatedAt)?.ToString()),
                ResolvedAt = ParseNullableDateValue(ExtractJsonPath(responseNode, mapping?.ResolvedAt)?.ToString()),
                Found = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "REST generic get ticket failed for {TicketId}", ticketId);
            return new TicketInfo { TicketId = ticketId, Found = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var (config, adapterConfig) = await LoadConfigAsync(cancellationToken);
            if (config == null || adapterConfig == null)
                return false;

            ConfigureAuth(config, adapterConfig);

            var endpoint = GetEndpoint(adapterConfig, "test");
            if (endpoint == null)
            {
                _logger.LogWarning("No 'test' endpoint configured for REST generic adapter");
                return false;
            }

            var url = BuildUrl(config.Server, endpoint.Path);
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("REST generic connection test succeeded");
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("REST generic connection test failed: {StatusCode} {Body}", response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "REST generic connection test threw exception");
            return false;
        }
    }

    /// <summary>
    /// Loads the connection record and parses the adapter configuration JSON from the Token field.
    /// </summary>
    private async Task<(ItsmConnectionInfo? Config, AdapterConfig? AdapterConfig)> LoadConfigAsync(CancellationToken cancellationToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync("rest-generic", cancellationToken);

        if (connection == null)
            return (null, null);

        if (string.IsNullOrWhiteSpace(connection.Token))
        {
            _logger.LogError("REST generic connection {ConnectionId} has no configuration in Token field", connection.ConnectionId);
            return (connection, null);
        }

        try
        {
            var adapterConfig = JsonSerializer.Deserialize<AdapterConfig>(connection.Token, JsonOptions);
            return (connection, adapterConfig);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse REST generic adapter config for connection {ConnectionId}", connection.ConnectionId);
            return (connection, null);
        }
    }

    private void ConfigureAuth(ItsmConnectionInfo config, AdapterConfig adapterConfig)
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
        _httpClient.DefaultRequestHeaders.Remove(adapterConfig.ApiKeyHeader ?? "X-Api-Key");

        var decryptedPassword = _credentialProtector.Decrypt(config.Password);

        switch (adapterConfig.AuthType?.ToLowerInvariant())
        {
            case "basic":
                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{config.UserName}:{decryptedPassword}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);
                break;

            case "bearer":
                // For bearer auth, the Token field holds the adapter config JSON,
                // so the bearer token is stored in the Password field
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", decryptedPassword);
                break;

            case "apikey":
                var headerName = adapterConfig.ApiKeyHeader ?? "X-Api-Key";
                _httpClient.DefaultRequestHeaders.Add(headerName, decryptedPassword);
                break;

            default:
                _logger.LogWarning("Unknown auth type '{AuthType}' for REST generic adapter", adapterConfig.AuthType);
                break;
        }
    }

    /// <summary>
    /// Builds template variables from a TicketRequest, making all request properties
    /// available for substitution in body/path templates via {{variableName}} syntax.
    /// </summary>
    private static Dictionary<string, string> BuildTemplateVariables(TicketRequest request)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "title", request.Title },
            { "description", request.Description },
            { "priority", request.Priority },
            { "category", request.Category },
            { "floorplanUrl", request.FloorplanUrl ?? "" },
            { "locationName", request.LocationName ?? "" },
            { "customerReference", request.CustomerReference ?? "" },
            { "affectedDevices", string.Join(", ", request.AffectedDevices) },
            { "affectedDevicesJson", JsonSerializer.Serialize(request.AffectedDevices) }
        };

        // Expose custom fields with "field." prefix
        foreach (var (key, value) in request.CustomFields)
        {
            vars[$"field.{key}"] = value;
        }

        return vars;
    }

    /// <summary>
    /// Replaces {{variableName}} placeholders in a template string with actual values.
    /// Handles JSON escaping for values embedded in JSON body templates.
    /// </summary>
    private static string RenderTemplate(string? template, Dictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;

        return TemplatePlaceholderRegex().Replace(template, match =>
        {
            var varName = match.Groups[1].Value.Trim();
            if (variables.TryGetValue(varName, out var value))
            {
                // Escape for JSON safety when the template appears to be JSON
                return EscapeJsonString(value);
            }
            return match.Value;
        });
    }

    [GeneratedRegex(@"\{\{(\s*[\w.]+\s*)\}\}")]
    private static partial Regex TemplatePlaceholderRegex();

    /// <summary>
    /// Escapes a string for safe embedding in a JSON value (handles quotes, backslashes, newlines).
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    /// Extracts a value from a JSON node using a simplified JSONPath expression.
    /// Supports root property access ($.field) and single-level nested access ($.parent.child).
    /// </summary>
    private static JsonNode? ExtractJsonPath(JsonNode? root, string? path)
    {
        if (root == null || string.IsNullOrWhiteSpace(path))
            return null;

        // Remove leading "$." prefix
        var normalizedPath = path.StartsWith("$.") ? path[2..] : path;
        var segments = normalizedPath.Split('.');

        var current = root;
        foreach (var segment in segments)
        {
            if (current == null)
                return null;

            // Handle array index notation: "items[0]"
            var arrayMatch = ArrayIndexRegex().Match(segment);
            if (arrayMatch.Success)
            {
                var propName = arrayMatch.Groups[1].Value;
                var index = int.Parse(arrayMatch.Groups[2].Value);

                if (!string.IsNullOrEmpty(propName))
                    current = current[propName];

                current = current?[index];
            }
            else
            {
                current = current[segment];
            }
        }

        return current;
    }

    [GeneratedRegex(@"^(\w*)\[(\d+)\]$")]
    private static partial Regex ArrayIndexRegex();

    private static EndpointConfig? GetEndpoint(AdapterConfig config, string name)
    {
        return name.ToLowerInvariant() switch
        {
            "create" => config.Endpoints?.Create,
            "update" => config.Endpoints?.Update,
            "resolve" => config.Endpoints?.Resolve,
            "get" => config.Endpoints?.Get,
            "attach" => config.Endpoints?.Attach,
            "test" => config.Endpoints?.Test,
            _ => null
        };
    }

    private string MapPriority(AdapterConfig config, string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
            return "";

        if (config.PriorityMapping != null &&
            config.PriorityMapping.TryGetValue(priority, out var mapped))
        {
            return mapped;
        }

        return priority;
    }

    private static string BuildUrl(string? server, string? path)
    {
        var baseUrl = server?.TrimEnd('/') ?? "https://localhost";
        if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            baseUrl = "https://" + baseUrl;

        if (string.IsNullOrWhiteSpace(path))
            return baseUrl;

        if (!path.StartsWith('/'))
            path = "/" + path;

        return baseUrl + path;
    }

    private static JsonNode? TryParseJson(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonNode>(body, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime ParseDateValue(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return DateTime.MinValue;

        return DateTime.TryParse(dateStr, out var dt) ? dt.ToUniversalTime() : DateTime.MinValue;
    }

    private static DateTime? ParseNullableDateValue(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        return DateTime.TryParse(dateStr, out var dt) ? dt.ToUniversalTime() : null;
    }

    private static string TruncateBody(string body)
    {
        return body.Length > 200 ? body[..200] + "..." : body;
    }

    private static TicketResult ErrorResult(string message)
    {
        return new TicketResult { Success = false, ErrorMessage = message };
    }

    #region Configuration DTOs

    /// <summary>
    /// Deserialized adapter configuration from the ItsmConnectionInfo Token field.
    /// Defines endpoints, auth type, field mappings, and priority translations.
    /// </summary>
    internal class AdapterConfig
    {
        public string? AuthType { get; set; }
        public string? ApiKeyHeader { get; set; }
        public EndpointSet? Endpoints { get; set; }
        public ResponseMappingConfig? ResponseMapping { get; set; }
        public Dictionary<string, string>? PriorityMapping { get; set; }
    }

    internal class EndpointSet
    {
        public EndpointConfig? Create { get; set; }
        public EndpointConfig? Update { get; set; }
        public EndpointConfig? Resolve { get; set; }
        public EndpointConfig? Get { get; set; }
        public EndpointConfig? Attach { get; set; }
        public EndpointConfig? Test { get; set; }
    }

    internal class EndpointConfig
    {
        public string Method { get; set; } = "GET";
        public string Path { get; set; } = "/";
        public string? BodyTemplate { get; set; }
    }

    internal class ResponseMappingConfig
    {
        public string? TicketId { get; set; }
        public string? TicketUrl { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
        public string? ResolvedAt { get; set; }
    }

    #endregion
}
