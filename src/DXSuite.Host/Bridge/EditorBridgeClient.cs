using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
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
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.DirectorBaseUrl)) return null;
        try
        {
            using var request = BuildRequest(HttpMethod.Post, path, body);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
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
