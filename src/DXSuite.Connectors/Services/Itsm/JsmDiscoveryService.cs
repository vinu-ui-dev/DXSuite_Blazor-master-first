using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace DXSuite.Connectors.Services.Itsm;

/// <summary>
/// Discovery service for JSM Cloud API. Used during connection setup to test credentials,
/// enumerate service desks, and fetch request types. Takes explicit credentials rather than
/// reading from DB, since the connection may not be saved yet.
/// </summary>
public class JsmDiscoveryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JsmDiscoveryService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public JsmDiscoveryService(HttpClient httpClient, ILogger<JsmDiscoveryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Validates credentials by calling /rest/api/3/myself.
    /// </summary>
    public async Task<bool> TestConnectionAsync(string baseUrl, string email, string apiToken, CancellationToken cancellationToken = default)
    {
        try
        {
            ConfigureAuth(email, apiToken);
            var url = $"{NormalizeBaseUrl(baseUrl)}/rest/api/3/myself";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("JSM connection test succeeded for {BaseUrl}", baseUrl);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("JSM connection test failed: {StatusCode} {Body}", response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSM connection test threw exception for {BaseUrl}", baseUrl);
            return false;
        }
    }

    /// <summary>
    /// Fetches all service desks from the JSM instance, paginating via isLastPage.
    /// </summary>
    public async Task<List<JsmServiceDesk>> GetServiceDesksAsync(string baseUrl, string email, string apiToken, CancellationToken cancellationToken = default)
    {
        var desks = new List<JsmServiceDesk>();

        try
        {
            ConfigureAuth(email, apiToken);
            var normalizedUrl = NormalizeBaseUrl(baseUrl);
            var start = 0;
            var limit = 50;
            bool isLastPage = false;

            while (!isLastPage)
            {
                var url = $"{normalizedUrl}/rest/servicedeskapi/servicedesk?start={start}&limit={limit}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to fetch service desks: {StatusCode} {Body}", response.StatusCode, body);
                    break;
                }

                var node = JsonSerializer.Deserialize<JsonNode>(body, JsonOptions);
                var values = node?["values"]?.AsArray();

                if (values != null)
                {
                    foreach (var v in values)
                    {
                        desks.Add(new JsmServiceDesk
                        {
                            Id = v?["id"]?.ToString() ?? string.Empty,
                            ProjectId = v?["projectId"]?.ToString() ?? string.Empty,
                            ProjectName = v?["projectName"]?.GetValue<string>() ?? string.Empty,
                            ProjectKey = v?["projectKey"]?.GetValue<string>() ?? string.Empty
                        });
                    }
                }

                isLastPage = node?["isLastPage"]?.GetValue<bool>() ?? true;
                start += limit;
            }

            _logger.LogInformation("Discovered {Count} service desks from {BaseUrl}", desks.Count, baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch service desks from {BaseUrl}", baseUrl);
        }

        return desks;
    }

    /// <summary>
    /// Fetches request types for a specific service desk, paginating via isLastPage.
    /// </summary>
    public async Task<List<JsmRequestType>> GetRequestTypesAsync(string baseUrl, string email, string apiToken, string serviceDeskId, CancellationToken cancellationToken = default)
    {
        var types = new List<JsmRequestType>();

        try
        {
            ConfigureAuth(email, apiToken);
            var normalizedUrl = NormalizeBaseUrl(baseUrl);
            var start = 0;
            var limit = 50;
            bool isLastPage = false;

            while (!isLastPage)
            {
                var url = $"{normalizedUrl}/rest/servicedeskapi/servicedesk/{serviceDeskId}/requesttype?start={start}&limit={limit}";
                var response = await _httpClient.GetAsync(url, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to fetch request types for desk {DeskId}: {StatusCode} {Body}", serviceDeskId, response.StatusCode, body);
                    break;
                }

                var node = JsonSerializer.Deserialize<JsonNode>(body, JsonOptions);
                var values = node?["values"]?.AsArray();

                if (values != null)
                {
                    foreach (var v in values)
                    {
                        var groupIds = new List<string>();
                        var groups = v?["groupIds"]?.AsArray();
                        if (groups != null)
                        {
                            foreach (var g in groups)
                            {
                                if (g != null) groupIds.Add(g.ToString());
                            }
                        }

                        types.Add(new JsmRequestType
                        {
                            Id = v?["id"]?.ToString() ?? string.Empty,
                            Name = v?["name"]?.GetValue<string>() ?? string.Empty,
                            Description = v?["description"]?.GetValue<string>() ?? string.Empty,
                            HelpText = v?["helpText"]?.GetValue<string>() ?? string.Empty,
                            IssueTypeId = v?["issueTypeId"]?.ToString() ?? string.Empty,
                            GroupIds = groupIds
                        });
                    }
                }

                isLastPage = node?["isLastPage"]?.GetValue<bool>() ?? true;
                start += limit;
            }

            _logger.LogInformation("Discovered {Count} request types for desk {DeskId} from {BaseUrl}", types.Count, serviceDeskId, baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch request types for desk {DeskId} from {BaseUrl}", serviceDeskId, baseUrl);
        }

        return types;
    }

    private void ConfigureAuth(string email, string apiToken)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{email}:{apiToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        return url;
    }
}
