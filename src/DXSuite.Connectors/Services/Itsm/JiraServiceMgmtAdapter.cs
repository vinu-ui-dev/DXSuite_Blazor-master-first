using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DXSuite.Shared.Services;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Itsm;

/// <summary>
/// ITSM adapter for Jira Service Management. Creates and manages JSM issues via REST API v3.
/// Uses Basic auth (email:apiToken) per Jira Cloud API requirements. Description fields use
/// Atlassian Document Format (ADF) for rich text rendering in the Jira UI.
/// Connection configuration (Server, UserName, Token) is read from IItsmConnectionProvider.
/// </summary>
public class JiraServiceMgmtAdapter : IItsmAdapter
{
    public string PlatformId => "JiraSM";

    private readonly HttpClient _httpClient;
    private readonly IItsmConnectionProvider _connectionProvider;
    private readonly ILogger<JiraServiceMgmtAdapter> _logger;
    private readonly ItsmCredentialProtector _credentialProtector;

    private static readonly Dictionary<string, string> PriorityMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        { "critical", "1" },
        { "high", "2" },
        { "medium", "3" },
        { "low", "4" }
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public JiraServiceMgmtAdapter(
        HttpClient httpClient,
        IItsmConnectionProvider connectionProvider,
        ILogger<JiraServiceMgmtAdapter> logger,
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
            var config = await GetConnectionConfigAsync(cancellationToken);
            if (config == null)
                return ErrorResult("No Jira SM integration configuration found");

            ConfigureAuth(config);

            // Use JSM Service Desk API when a structured config with ServiceDeskId is present
            var jsmConfig = TryParseJsmConfig(config.ConfigJson);
            if (jsmConfig != null && !string.IsNullOrEmpty(jsmConfig.ServiceDeskId))
            {
                return await CreateTicketViaJsmApiAsync(request, config, jsmConfig, cancellationToken);
            }

            return await CreateTicketViaRestApiAsync(request, config, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Jira issue for request: {Title}", request.Title);
            return ErrorResult($"Exception creating Jira issue: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a customer request via the JSM Service Desk API, which enables SLA tracking
    /// and service desk routing. Uses plain text description (JSM API doesn't use ADF).
    /// </summary>
    private async Task<TicketResult> CreateTicketViaJsmApiAsync(TicketRequest request, ItsmConnectionInfo config, JsmConnectionConfig jsmConfig, CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeBaseUrl(config.Server);

        var requestTypeId = jsmConfig.DefaultRequestTypeId;
        if (string.IsNullOrEmpty(requestTypeId))
        {
            requestTypeId = jsmConfig.RequestTypes.FirstOrDefault()?.Id;
        }

        if (string.IsNullOrEmpty(requestTypeId))
        {
            _logger.LogWarning("No request type configured for JSM service desk {DeskId}", jsmConfig.ServiceDeskId);
            return ErrorResult("No request type configured for this JSM service desk");
        }

        var description = BuildPlainTextDescription(request);

        var payload = new JsonObject
        {
            ["serviceDeskId"] = jsmConfig.ServiceDeskId,
            ["requestTypeId"] = requestTypeId,
            ["requestFieldValues"] = new JsonObject
            {
                ["summary"] = request.Title,
                ["description"] = description
            }
        };

        var url = $"{baseUrl}/rest/servicedeskapi/request";
        var content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("JSM create request failed: {StatusCode} {Body}", response.StatusCode, responseBody);
            return ErrorResult($"JSM API returned {(int)response.StatusCode}: {ExtractErrorMessage(responseBody)}");
        }

        var result = JsonSerializer.Deserialize<JsonNode>(responseBody, JsonOptions);
        var issueKey = result?["issueKey"]?.GetValue<string>();
        var issueId = result?["issueId"]?.GetValue<string>();

        _logger.LogInformation("Created JSM request {IssueKey} via service desk {DeskId} for: {Title}", issueKey, jsmConfig.ServiceDeskId, request.Title);

        return new TicketResult
        {
            Success = true,
            TicketId = issueKey ?? issueId,
            TicketUrl = issueKey != null ? $"{baseUrl}/browse/{issueKey}" : null
        };
    }

    /// <summary>
    /// Legacy path: creates an issue via the standard Jira REST API v3, which bypasses JSM
    /// service desk routing. Used when no JsmConnectionConfig is present (backward compat).
    /// </summary>
    private async Task<TicketResult> CreateTicketViaRestApiAsync(TicketRequest request, ItsmConnectionInfo config, CancellationToken cancellationToken)
    {
        var descriptionAdf = BuildAdfDescription(request);

        var labels = new List<string> { "sentinel-auto" };
        if (!string.IsNullOrWhiteSpace(request.Category))
            labels.Add(SanitizeLabel(request.Category));

        var payload = new JsonObject
        {
            ["fields"] = new JsonObject
            {
                ["project"] = new JsonObject
                {
                    // Legacy convention: project key stored in Password field
                    ["key"] = config.Password ?? "SUPPORT"
                },
                ["summary"] = request.Title,
                ["description"] = descriptionAdf,
                ["issuetype"] = new JsonObject
                {
                    ["name"] = "Service Request"
                },
                ["priority"] = new JsonObject
                {
                    ["id"] = PriorityMapping.GetValueOrDefault(request.Priority, "3")
                },
                ["labels"] = new JsonArray(labels.Select(l => JsonValue.Create(l)).ToArray<JsonNode>())
            }
        };

        if (request.CustomFields.Count > 0)
        {
            var fields = payload["fields"]!.AsObject();
            foreach (var (key, value) in request.CustomFields)
            {
                fields[key] = value;
            }
        }

        var url = $"{NormalizeBaseUrl(config.Server)}/rest/api/3/issue";
        var content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Jira create issue failed: {StatusCode} {Body}", response.StatusCode, responseBody);
            return ErrorResult($"Jira API returned {(int)response.StatusCode}: {ExtractErrorMessage(responseBody)}");
        }

        var result = JsonSerializer.Deserialize<JsonNode>(responseBody, JsonOptions);
        var issueKey = result?["key"]?.GetValue<string>();
        var issueId = result?["id"]?.GetValue<string>();

        _logger.LogInformation("Created Jira issue {IssueKey} for request: {Title}", issueKey, request.Title);

        return new TicketResult
        {
            Success = true,
            TicketId = issueKey ?? issueId,
            TicketUrl = issueKey != null ? $"{NormalizeBaseUrl(config.Server)}/browse/{issueKey}" : null
        };
    }

    public async Task<TicketResult> UpdateTicketAsync(string ticketId, TicketUpdate update, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await GetConnectionConfigAsync(cancellationToken);
            if (config == null)
                return ErrorResult("No Jira SM integration configuration found");

            ConfigureAuth(config);
            var baseUrl = NormalizeBaseUrl(config.Server);

            // Add a comment if provided
            if (!string.IsNullOrWhiteSpace(update.Comment))
            {
                var commentAdf = BuildAdfParagraph(update.Comment);
                var commentPayload = new JsonObject { ["body"] = commentAdf };
                var commentUrl = $"{baseUrl}/rest/api/3/issue/{ticketId}/comment";
                var commentContent = new StringContent(commentPayload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
                var commentResponse = await _httpClient.PostAsync(commentUrl, commentContent, cancellationToken);

                if (!commentResponse.IsSuccessStatusCode)
                {
                    var errBody = await commentResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Failed to add comment to {TicketId}: {StatusCode} {Body}", ticketId, commentResponse.StatusCode, errBody);
                }
            }

            // Update fields (priority, custom fields) if any are specified
            var fieldsToUpdate = new JsonObject();
            bool hasFieldUpdates = false;

            if (!string.IsNullOrWhiteSpace(update.Priority))
            {
                fieldsToUpdate["priority"] = new JsonObject
                {
                    ["id"] = PriorityMapping.GetValueOrDefault(update.Priority, "3")
                };
                hasFieldUpdates = true;
            }

            if (update.UpdatedFields != null)
            {
                foreach (var (key, value) in update.UpdatedFields)
                {
                    fieldsToUpdate[key] = value;
                    hasFieldUpdates = true;
                }
            }

            if (hasFieldUpdates)
            {
                var updatePayload = new JsonObject { ["fields"] = fieldsToUpdate };
                var updateUrl = $"{baseUrl}/rest/api/3/issue/{ticketId}";
                var updateContent = new StringContent(updatePayload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
                var updateRequest = new HttpRequestMessage(HttpMethod.Put, updateUrl) { Content = updateContent };
                var updateResponse = await _httpClient.SendAsync(updateRequest, cancellationToken);

                if (!updateResponse.IsSuccessStatusCode)
                {
                    var errBody = await updateResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Failed to update fields on {TicketId}: {StatusCode} {Body}", ticketId, updateResponse.StatusCode, errBody);
                    return ErrorResult($"Field update failed: {ExtractErrorMessage(errBody)}");
                }
            }

            _logger.LogInformation("Updated Jira issue {TicketId}", ticketId);

            return new TicketResult
            {
                Success = true,
                TicketId = ticketId,
                TicketUrl = $"{baseUrl}/browse/{ticketId}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Jira issue {TicketId}", ticketId);
            return ErrorResult($"Exception updating Jira issue: {ex.Message}");
        }
    }

    public async Task<TicketResult> ResolveTicketAsync(string ticketId, ItsmResolutionInfo resolution, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await GetConnectionConfigAsync(cancellationToken);
            if (config == null)
                return ErrorResult("No Jira SM integration configuration found");

            ConfigureAuth(config);
            var baseUrl = NormalizeBaseUrl(config.Server);

            // Add a resolution comment before transitioning
            var resolveComment = BuildResolutionComment(resolution);
            var commentAdf = BuildAdfParagraph(resolveComment);
            var commentPayload = new JsonObject { ["body"] = commentAdf };
            var commentUrl = $"{baseUrl}/rest/api/3/issue/{ticketId}/comment";
            var commentContent = new StringContent(commentPayload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(commentUrl, commentContent, cancellationToken);

            // Get available transitions to find the "Done" or "Resolved" transition
            var transitionsUrl = $"{baseUrl}/rest/api/3/issue/{ticketId}/transitions";
            var transitionsResponse = await _httpClient.GetAsync(transitionsUrl, cancellationToken);
            var transitionsBody = await transitionsResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!transitionsResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get transitions for {TicketId}: {Body}", ticketId, transitionsBody);
                return ErrorResult($"Could not retrieve transitions: {ExtractErrorMessage(transitionsBody)}");
            }

            var transitionsNode = JsonSerializer.Deserialize<JsonNode>(transitionsBody, JsonOptions);
            var transitions = transitionsNode?["transitions"]?.AsArray();
            var resolveTransition = FindResolveTransition(transitions);

            if (resolveTransition == null)
            {
                _logger.LogWarning("No resolve/done transition found for {TicketId}. Available: {Transitions}",
                    ticketId, transitionsBody);
                return ErrorResult("No resolve or done transition available for this issue");
            }

            // Execute the transition
            var transitionPayload = new JsonObject
            {
                ["transition"] = new JsonObject
                {
                    ["id"] = resolveTransition
                }
            };

            var transitionContent = new StringContent(transitionPayload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");
            var transitionResponse = await _httpClient.PostAsync(transitionsUrl, transitionContent, cancellationToken);

            if (!transitionResponse.IsSuccessStatusCode)
            {
                var errBody = await transitionResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to transition {TicketId}: {StatusCode} {Body}", ticketId, transitionResponse.StatusCode, errBody);
                return ErrorResult($"Transition failed: {ExtractErrorMessage(errBody)}");
            }

            _logger.LogInformation("Resolved Jira issue {TicketId} (auto={AutoResolved})", ticketId, resolution.AutoResolved);

            return new TicketResult
            {
                Success = true,
                TicketId = ticketId,
                TicketUrl = $"{baseUrl}/browse/{ticketId}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve Jira issue {TicketId}", ticketId);
            return ErrorResult($"Exception resolving Jira issue: {ex.Message}");
        }
    }

    public async Task<TicketResult> AddAttachmentAsync(string ticketId, TicketAttachment attachment, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await GetConnectionConfigAsync(cancellationToken);
            if (config == null)
                return ErrorResult("No Jira SM integration configuration found");

            ConfigureAuth(config);
            var baseUrl = NormalizeBaseUrl(config.Server);

            var url = $"{baseUrl}/rest/api/3/issue/{ticketId}/attachments";

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(attachment.Content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(attachment.ContentType);
            form.Add(fileContent, "file", attachment.FileName);

            // Jira requires this header to acknowledge the experimental attachment API
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            request.Headers.Add("X-Atlassian-Token", "no-check");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to attach file to {TicketId}: {StatusCode} {Body}", ticketId, response.StatusCode, responseBody);
                return ErrorResult($"Attachment upload failed: {ExtractErrorMessage(responseBody)}");
            }

            _logger.LogInformation("Attached {FileName} to Jira issue {TicketId}", attachment.FileName, ticketId);

            return new TicketResult
            {
                Success = true,
                TicketId = ticketId,
                TicketUrl = $"{baseUrl}/browse/{ticketId}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to attach file to Jira issue {TicketId}", ticketId);
            return ErrorResult($"Exception attaching file: {ex.Message}");
        }
    }

    public async Task<TicketInfo> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await GetConnectionConfigAsync(cancellationToken);
            if (config == null)
                return new TicketInfo { Found = false, ErrorMessage = "No Jira SM integration configuration found" };

            ConfigureAuth(config);
            var baseUrl = NormalizeBaseUrl(config.Server);

            var url = $"{baseUrl}/rest/api/3/issue/{ticketId}?fields=summary,status,priority,created,updated,resolutiondate";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new TicketInfo { TicketId = ticketId, Found = false };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get Jira issue {TicketId}: {StatusCode} {Body}", ticketId, response.StatusCode, responseBody);
                return new TicketInfo { TicketId = ticketId, Found = false, ErrorMessage = ExtractErrorMessage(responseBody) };
            }

            var issue = JsonSerializer.Deserialize<JsonNode>(responseBody, JsonOptions);
            var fields = issue?["fields"];

            return new TicketInfo
            {
                TicketId = issue?["key"]?.GetValue<string>() ?? ticketId,
                TicketUrl = $"{baseUrl}/browse/{issue?["key"]?.GetValue<string>() ?? ticketId}",
                Title = fields?["summary"]?.GetValue<string>() ?? string.Empty,
                Status = fields?["status"]?["name"]?.GetValue<string>() ?? string.Empty,
                Priority = fields?["priority"]?["name"]?.GetValue<string>() ?? string.Empty,
                CreatedAt = ParseJiraDate(fields?["created"]?.GetValue<string>()),
                UpdatedAt = ParseNullableJiraDate(fields?["updated"]?.GetValue<string>()),
                ResolvedAt = ParseNullableJiraDate(fields?["resolutiondate"]?.GetValue<string>()),
                Found = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Jira issue {TicketId}", ticketId);
            return new TicketInfo { TicketId = ticketId, Found = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = await GetConnectionConfigAsync(cancellationToken);
            if (config == null)
                return false;

            ConfigureAuth(config);
            var baseUrl = NormalizeBaseUrl(config.Server);

            var url = $"{baseUrl}/rest/api/3/myself";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Jira SM connection test succeeded");
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Jira SM connection test failed: {StatusCode} {Body}", response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jira SM connection test threw exception");
            return false;
        }
    }

    /// <summary>
    /// Loads the Jira SM connection settings from IItsmConnectionProvider.
    /// Server = Jira Cloud base URL, UserName = email, Token = API token,
    /// Password = project key.
    /// </summary>
    private async Task<ItsmConnectionInfo?> GetConnectionConfigAsync(CancellationToken cancellationToken)
    {
        return await _connectionProvider.GetConnectionAsync("JiraSM", cancellationToken);
    }

    private void ConfigureAuth(ItsmConnectionInfo config)
    {
        // Jira Cloud requires Basic auth: base64(email:apiToken)
        var decryptedToken = _credentialProtector.Decrypt(config.Token);
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{config.UserName}:{decryptedToken}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    /// <summary>
    /// Builds an Atlassian Document Format (ADF) document for the issue description.
    /// ADF is required by Jira REST API v3 for description and comment body fields.
    /// </summary>
    private static JsonNode BuildAdfDescription(TicketRequest request)
    {
        var contentNodes = new JsonArray();

        // Main description paragraph
        contentNodes.Add(CreateAdfParagraph(request.Description));

        // Device list section
        if (request.AffectedDevices.Count > 0)
        {
            contentNodes.Add(CreateAdfHeading("Affected Devices", 3));

            var listItems = new JsonArray();
            foreach (var device in request.AffectedDevices)
            {
                listItems.Add(new JsonObject
                {
                    ["type"] = "listItem",
                    ["content"] = new JsonArray
                    {
                        CreateAdfParagraph(device)
                    }
                });
            }

            contentNodes.Add(new JsonObject
            {
                ["type"] = "bulletList",
                ["content"] = listItems
            });
        }

        // Location info
        if (!string.IsNullOrWhiteSpace(request.LocationName))
        {
            contentNodes.Add(CreateAdfHeading("Location", 3));
            contentNodes.Add(CreateAdfParagraph(request.LocationName));
        }

        // Floorplan link
        if (!string.IsNullOrWhiteSpace(request.FloorplanUrl))
        {
            contentNodes.Add(CreateAdfHeading("Floorplan", 3));
            contentNodes.Add(new JsonObject
            {
                ["type"] = "paragraph",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = "View Floorplan",
                        ["marks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "link",
                                ["attrs"] = new JsonObject
                                {
                                    ["href"] = request.FloorplanUrl
                                }
                            }
                        }
                    }
                }
            });
        }

        // Category and reference metadata
        if (!string.IsNullOrWhiteSpace(request.Category) || !string.IsNullOrWhiteSpace(request.CustomerReference))
        {
            contentNodes.Add(CreateAdfHeading("Details", 3));
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Category))
                details.Add($"Category: {request.Category}");
            if (!string.IsNullOrWhiteSpace(request.CustomerReference))
                details.Add($"Customer Reference: {request.CustomerReference}");
            contentNodes.Add(CreateAdfParagraph(string.Join("\n", details)));
        }

        return new JsonObject
        {
            ["type"] = "doc",
            ["version"] = 1,
            ["content"] = contentNodes
        };
    }

    /// <summary>
    /// Wraps a plain text string in a single-paragraph ADF document for comment bodies.
    /// </summary>
    private static JsonNode BuildAdfParagraph(string text)
    {
        return new JsonObject
        {
            ["type"] = "doc",
            ["version"] = 1,
            ["content"] = new JsonArray
            {
                CreateAdfParagraph(text)
            }
        };
    }

    private static JsonObject CreateAdfParagraph(string text)
    {
        return new JsonObject
        {
            ["type"] = "paragraph",
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };
    }

    private static JsonObject CreateAdfHeading(string text, int level)
    {
        return new JsonObject
        {
            ["type"] = "heading",
            ["attrs"] = new JsonObject { ["level"] = level },
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };
    }

    /// <summary>
    /// Searches the available transitions for one that resolves or closes the issue.
    /// Jira workflows vary, so we check multiple common transition names.
    /// </summary>
    private static string? FindResolveTransition(JsonArray? transitions)
    {
        if (transitions == null) return null;

        var resolveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Done", "Resolve", "Resolved", "Close", "Closed", "Complete", "Completed",
            "Resolve this issue", "Mark as Done"
        };

        foreach (var transition in transitions)
        {
            var name = transition?["name"]?.GetValue<string>();
            if (name != null && resolveNames.Contains(name))
            {
                return transition?["id"]?.GetValue<string>();
            }
        }

        // Fallback: check the target status category for "done" (green category in Jira)
        foreach (var transition in transitions)
        {
            var statusCategory = transition?["to"]?["statusCategory"]?["key"]?.GetValue<string>();
            if (string.Equals(statusCategory, "done", StringComparison.OrdinalIgnoreCase))
            {
                return transition?["id"]?.GetValue<string>();
            }
        }

        return null;
    }

    private static string BuildResolutionComment(ItsmResolutionInfo resolution)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Resolved by: {resolution.ResolvedBy}");
        if (resolution.AutoResolved)
            sb.AppendLine("Resolution type: Auto-resolved by Sentinel");
        sb.AppendLine($"Resolution: {resolution.ResolutionNote}");
        if (!string.IsNullOrWhiteSpace(resolution.ActionsTaken))
            sb.AppendLine($"Actions taken: {resolution.ActionsTaken}");
        return sb.ToString();
    }

    /// <summary>
    /// Sanitizes a string for use as a Jira label (no spaces, lowercase, alphanumeric + hyphens only).
    /// </summary>
    private static string SanitizeLabel(string input)
    {
        return new string(input.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');
    }

    private static string NormalizeBaseUrl(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
            return "https://localhost";

        var url = server.TrimEnd('/');
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        return url;
    }

    private static string ExtractErrorMessage(string responseBody)
    {
        try
        {
            var node = JsonSerializer.Deserialize<JsonNode>(responseBody, JsonOptions);

            // Jira returns errors in multiple formats
            var errorMessages = node?["errorMessages"]?.AsArray();
            if (errorMessages != null && errorMessages.Count > 0)
                return string.Join("; ", errorMessages.Select(e => e?.GetValue<string>()));

            var errors = node?["errors"]?.AsObject();
            if (errors != null && errors.Count > 0)
                return string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value}"));

            var message = node?["message"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(message))
                return message;
        }
        catch
        {
            // Response wasn't valid JSON
        }

        // Truncate raw body for logging safety
        return responseBody.Length > 200 ? responseBody[..200] + "..." : responseBody;
    }

    private static DateTime ParseJiraDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return DateTime.MinValue;

        return DateTime.TryParse(dateStr, out var dt) ? dt.ToUniversalTime() : DateTime.MinValue;
    }

    private static DateTime? ParseNullableJiraDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        return DateTime.TryParse(dateStr, out var dt) ? dt.ToUniversalTime() : null;
    }

    private static JsmConnectionConfig? TryParseJsmConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        try
        {
            var config = JsonSerializer.Deserialize<JsmConnectionConfig>(configJson, JsonOptions);
            return string.IsNullOrEmpty(config?.ServiceDeskId) ? null : config;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a plain text description for the JSM Service Desk API, which does not use ADF.
    /// </summary>
    private static string BuildPlainTextDescription(TicketRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(request.Description);

        if (request.AffectedDevices.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Affected Devices:");
            foreach (var device in request.AffectedDevices)
                sb.AppendLine($"  - {device}");
        }

        if (!string.IsNullOrWhiteSpace(request.LocationName))
        {
            sb.AppendLine();
            sb.AppendLine($"Location: {request.LocationName}");
        }

        if (!string.IsNullOrWhiteSpace(request.FloorplanUrl))
        {
            sb.AppendLine($"Floorplan: {request.FloorplanUrl}");
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            sb.AppendLine($"Category: {request.Category}");
        }

        if (!string.IsNullOrWhiteSpace(request.CustomerReference))
        {
            sb.AppendLine($"Customer Reference: {request.CustomerReference}");
        }

        return sb.ToString().TrimEnd();
    }

    private static TicketResult ErrorResult(string message)
    {
        return new TicketResult { Success = false, ErrorMessage = message };
    }
}
