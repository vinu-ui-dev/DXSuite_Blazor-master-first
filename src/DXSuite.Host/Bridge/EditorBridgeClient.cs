using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using DXSuite.Host.Logging;
using DXSuite.Shared.Models;

namespace DXSuite.Host.Bridge;

/// <summary>
/// Calls Director's EditorAuth bridge (RadiantCoreWebApi's /api/v1/editor-auth/*) to redeem the
/// one-time code Director hands the browser, and to keep that session alive. Network/parse
/// failures never throw — callers treat a null result as "not authenticated via the bridge" and
/// fall back to the normal login page, the same way CoreApiClient treats an unreachable server.
/// </summary>
public sealed class EditorBridgeClient(HttpClient http, IOptions<EditorBridgeClientOptions> options, ILogger<EditorBridgeClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly EditorBridgeClientOptions _options = options.Value;

    /// <summary>
    /// Short, fixed-vocabulary reason for the most recent failed call on this instance (never contains
    /// secrets or user input), e.g. "http-401", "tls", "unreachable", "not-configured". Typed HttpClients
    /// are transient, so each controller instance sees only its own calls.
    /// </summary>
    public string? LastFailure { get; private set; }

    public async Task<BridgeIdentity?> RedeemAsync(string code, string verifier, CancellationToken ct) =>
        await PostAsync<BridgeIdentity>("/api/v1/editor-auth/redeem", new RedeemRequest(code, verifier), ct);

    public async Task<BridgeIdentity?> ValidateAsync(string sessionHandle, CancellationToken ct) =>
        await PostAsync<BridgeIdentity>("/api/v1/editor-auth/session/validate", new SessionRequest(sessionHandle), ct);

    public async Task EndAsync(string sessionHandle, CancellationToken ct)
    {
        try
        {
            using var request = BuildRequest(HttpMethod.Post, "/api/v1/editor-auth/session/end", new SessionRequest(sessionHandle));
            using var response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Editor bridge session/end call failed; the session will still expire on its own.");
        }
    }

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.DirectorBaseUrl))
        {
            LastFailure = !_options.Enabled ? "bridge-disabled" : "no-director-url";
            BridgeFileLog.Write($"[BRIDGE-HTTP] skipped {path}: Enabled={_options.Enabled}, DirectorBaseUrl empty={string.IsNullOrWhiteSpace(_options.DirectorBaseUrl)}");
            logger.LogWarning("Editor bridge call to {Path} skipped: Enabled={Enabled}, DirectorBaseUrl empty={Empty}.",
                path, _options.Enabled, string.IsNullOrWhiteSpace(_options.DirectorBaseUrl));
            return null;
        }
        try
        {
            using var request = BuildRequest(HttpMethod.Post, path, body);
            BridgeFileLog.Write($"[BRIDGE-HTTP] POST {request.RequestUri} clientId='{_options.ClientId}' hasSecret={!string.IsNullOrEmpty(_options.ClientSecret)} secretLength={_options.ClientSecret?.Length ?? 0}");
            using var response = await http.SendAsync(request, ct);
            BridgeFileLog.Write($"[BRIDGE-HTTP] {request.RequestUri} -> {(int)response.StatusCode} {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                LastFailure = $"http-{(int)response.StatusCode}";
                // Never log the request (code, verifier, secret); the response body only carries an error reason.
                var text = await response.Content.ReadAsStringAsync(ct);
                BridgeFileLog.Write($"[BRIDGE-HTTP] response body: {(text.Length > 300 ? text[..300] : text)}   (401 = wrong ClientId/ClientSecret, or code expired/already used, or PKCE verifier mismatch; 403 = non-https caller or access rules; 404 = EditorBridge:Enabled false on Director)");
                logger.LogWarning("Editor bridge call to {Url} returned {Status} (clientId={ClientId}). Response: {Body}",
                    request.RequestUri, (int)response.StatusCode, _options.ClientId, text.Length > 300 ? text[..300] : text);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            LastFailure = ex is HttpRequestException && ex.InnerException is System.Security.Authentication.AuthenticationException ? "tls"
                : ex is HttpRequestException ? "unreachable"
                : ex is OperationCanceledException ? "timeout" : "bad-response";
            BridgeFileLog.Write($"[BRIDGE-HTTP] EXCEPTION calling {path}: {ex.GetType().Name}: {ex.Message} (DNS/TLS/firewall from this server to DirectorBaseUrl '{_options.DirectorBaseUrl}'?)");
            logger.LogWarning(ex, "Editor bridge call to {Path} failed.", path);
            return null;
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object body)
    {
        var request = new HttpRequestMessage(method, _options.DirectorBaseUrl.TrimEnd('/') + path)
        {
            Content = JsonContent.Create(body),
        };
        // Per request, never on the HttpClient: the handler is a singleton shared across tenants.
        request.Headers.TryAddWithoutValidation("X-Editor-Client", _options.ClientId);
        request.Headers.TryAddWithoutValidation("X-Editor-Client-Secret", _options.ClientSecret);
        return request;
    }
}
