using System.Net.Http.Json;
using System.Text.Json;
using DXSuite.Shared.Authorization;
using DXSuite.Shared.Logging;
using DXSuite.Shared.Services.Identity;
using Microsoft.AspNetCore.Components;

namespace DXSuite.Aviation.Services.Cupps;

public sealed record AirlineLookupItemDto(int Id, string Name);

public sealed record AirlineLookupsDto(
    IReadOnlyList<AirlineLookupItemDto> Alliances,
    IReadOnlyList<AirlineLookupItemDto> DcsProviders,
    IReadOnlyList<AirlineLookupItemDto> Terminals,
    IReadOnlyList<AirlineLookupItemDto> MemberRoles)
{
    public static AirlineLookupsDto Empty { get; } = new([], [], [], []);
}

/// <summary>One airline as the Airlines tab shows it (the airline account plus approval and CUPPS options).</summary>
public sealed record AirlineDetailDto(
    int AirlineId, string Code, string NameEnglish, string NameFrench, string? Icao, string CuppsName,
    bool IsCanada, bool IsUnitedStates, bool IsInternational, string BackgroundColour,
    bool Retired,
    int? AllianceId, string? AllianceName,
    int? DcsProviderId, string? DcsProviderName,
    int UserGroupId, int MemberCount,
    string ApprovalStatus, string? RequestedBy, DateTime? RequestedUtc, string? DecidedBy, DateTime? DecidedUtc, string? DecisionNote,
    bool SunflowerOption, bool AslVideos, bool DestinationZone,
    IReadOnlyList<int> TerminalZoneIds)
{
    public bool IsApproved => string.Equals(ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase);
    public bool IsPending => string.Equals(ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase);
    public bool IsDeclined => string.Equals(ApprovalStatus, "Declined", StringComparison.OrdinalIgnoreCase);
}

public sealed record AirlineUpdateDto(
    string NameEnglish,
    string NameFrench,
    string CuppsName,
    bool IsCanada,
    bool IsUnitedStates,
    bool IsInternational,
    string StandardBackgroundColour,
    int? AllianceId,
    int? DcsProviderId,
    int[]? TerminalZoneIds,
    bool SunflowerOption,
    bool AslVideos,
    bool DestinationZone);

public sealed record AirlineMemberDto(int UserId, string Name, string Account, string Role);

/// <summary>The outcome of a call: success, or the message the server gave (never an HTML page).</summary>
public sealed record AirlineApiResult(bool Success, string? Error)
{
    public static AirlineApiResult Ok { get; } = new(true, null);
}

/// <summary>
/// Calls Director's airline admin endpoints (api/cupps/airlines/...) over the internal loopback, signed with the
/// short-lived token that carries the session's user and role. Nothing is kept here: the server is the source of truth.
/// </summary>
public sealed class AirlineAdminApi(
    IHttpClientFactory httpClientFactory,
    NavigationManager navigationManager,
    ISessionState sessionState,
    IInternalCallerTokenService tokenService)
{
    private Uri Url(string path) => new(new Uri(navigationManager.BaseUri), "api/cupps/airlines" + path);

    private HttpClient Client()
    {
        var client = httpClientFactory.CreateClient();
        if (sessionState.UserName is { } username)
            client.DefaultRequestHeaders.Add("X-Internal-Caller-Token", tokenService.IssueToken(username, sessionState.Role));
        return client;
    }

    public async Task<AirlineLookupsDto> GetLookupsAsync(CancellationToken ct = default)
    {
        try
        {
            return await httpClientFactory.CreateClient().GetFromJsonAsync<AirlineLookupsDto>(Url("/lookups"), ct) ?? AirlineLookupsDto.Empty;
        }
        catch (Exception ex)
        {
            AirlineFileLog.Write($"[AIRLINE-CLIENT] lookups failed: {AirlineFileLog.Describe(ex)}");
            return AirlineLookupsDto.Empty;
        }
    }

    /// <summary>All airlines for an Admin or Architect (retired and waiting ones too); the allowed ones for anyone else.</summary>
    public async Task<IReadOnlyList<AirlineDetailDto>> GetAirlinesAsync(CancellationToken ct = default)
    {
        using var response = await Client().GetAsync(Url("/admin"), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ErrorAsync(response, "The airline list could not be loaded.", ct));
        return await response.Content.ReadFromJsonAsync<List<AirlineDetailDto>>(cancellationToken: ct) ?? [];
    }

    public Task<AirlineApiResult> UpdateAsync(int airlineId, AirlineUpdateDto body, CancellationToken ct = default) =>
        SendAsync(c => c.PutAsJsonAsync(Url($"/{airlineId}"), body, ct), "update", ct);

    public Task<AirlineApiResult> RetireAsync(int airlineId, CancellationToken ct = default) =>
        SendAsync(c => c.PostAsync(Url($"/{airlineId}/retire"), null, ct), "retire", ct);

    public Task<AirlineApiResult> ReactivateAsync(int airlineId, CancellationToken ct = default) =>
        SendAsync(c => c.PostAsync(Url($"/{airlineId}/reactivate"), null, ct), "reactivate", ct);

    public Task<AirlineApiResult> ApproveAsync(int airlineId, string? note = null, CancellationToken ct = default) =>
        SendAsync(c => c.PostAsJsonAsync(Url($"/{airlineId}/approve"), new { note }, ct), "approve", ct);

    public Task<AirlineApiResult> DeclineAsync(int airlineId, string note, CancellationToken ct = default) =>
        SendAsync(c => c.PostAsJsonAsync(Url($"/{airlineId}/decline"), new { note }, ct), "decline", ct);

    public async Task<IReadOnlyList<AirlineMemberDto>> GetMembersAsync(int airlineId, CancellationToken ct = default)
    {
        using var response = await Client().GetAsync(Url($"/{airlineId}/members"), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ErrorAsync(response, "The members could not be loaded.", ct));
        return await response.Content.ReadFromJsonAsync<List<AirlineMemberDto>>(cancellationToken: ct) ?? [];
    }

    public async Task<IReadOnlyList<AirlineMemberDto>> SearchCandidatesAsync(int airlineId, string query, CancellationToken ct = default)
    {
        using var response = await Client().GetAsync(Url($"/{airlineId}/members/candidates?q={Uri.EscapeDataString(query)}"), ct);
        if (!response.IsSuccessStatusCode) return [];
        return await response.Content.ReadFromJsonAsync<List<AirlineMemberDto>>(cancellationToken: ct) ?? [];
    }

    public Task<AirlineApiResult> AddMemberAsync(int airlineId, int userId, string? roleName, CancellationToken ct = default) =>
        SendAsync(c => c.PostAsJsonAsync(Url($"/{airlineId}/members"), new { userId, roleName }, ct), "add member", ct);

    public Task<AirlineApiResult> RemoveMemberAsync(int airlineId, int userId, CancellationToken ct = default) =>
        SendAsync(c => c.DeleteAsync(Url($"/{airlineId}/members/{userId}"), ct), "remove member", ct);

    private async Task<AirlineApiResult> SendAsync(Func<HttpClient, Task<HttpResponseMessage>> call, string what, CancellationToken ct)
    {
        if (sessionState.UserName is null) return new AirlineApiResult(false, "Not signed in.");
        try
        {
            using var response = await call(Client());
            if (response.IsSuccessStatusCode) return AirlineApiResult.Ok;

            var error = await ErrorAsync(response, $"Could not {what}.", ct);
            AirlineFileLog.Write($"[AIRLINE-CLIENT] {what} -> {(int)response.StatusCode}: {error}");
            return new AirlineApiResult(false, error);
        }
        catch (Exception ex)
        {
            AirlineFileLog.Write($"[AIRLINE-CLIENT] {what} failed: {AirlineFileLog.Describe(ex)}");
            return new AirlineApiResult(false, $"Could not {what}. The server could not be reached.");
        }
    }

    /// <summary>The server's JSON message, or a plain sentence: an HTML body is an app page, never shown.</summary>
    private static async Task<string> ErrorAsync(HttpResponseMessage response, string fallback, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body) || body.TrimStart().StartsWith('<'))
            return $"{fallback} ({(int)response.StatusCode} {response.ReasonPhrase})";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString()))
                return message.GetString()!.Trim();
        }
        catch (JsonException) { }
        return $"{fallback} ({(int)response.StatusCode} {response.ReasonPhrase})";
    }
}
