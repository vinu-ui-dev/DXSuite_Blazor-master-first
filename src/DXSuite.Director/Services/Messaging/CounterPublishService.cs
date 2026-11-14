using DXSuite.Director.Authorization;
using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// Persists CUPPS counter publish/close actions to the real counter-publish schema
/// (tblCounterPublish / tblCounterPublishTarget / tblCounterState), which existed in the database
/// unused until this service — CuppsAgentScreens previously kept this state in memory only. Enforces
/// that the acting user (named explicitly, not inferred from an HTTP identity — see
/// <see cref="Controllers.CounterPublishController"/> for how that name is verified) is allowed to act
/// for the given airline, the same scoping <see cref="IAirlineAccessScope"/> applies to the read
/// endpoints.
/// </summary>
public interface ICounterPublishService
{
    Task<CounterPublishResult> PublishAsync(CounterPublishRequest request, CancellationToken ct = default);
    Task<CounterPublishResult> CloseAsync(string counterId, string actingUsername, CancellationToken ct = default);
}

public sealed record CounterPublishRequest(
    string CounterId,
    int AirlineId,
    string ActingUsername,
    int? CounterTemplateId = null,
    string? CounterTemplateKind = null,
    string? CounterTemplateName = null,
    string? Destination = null,
    bool? Sunflower = null,
    int? PageShowSeconds = null);

public sealed record CounterPublishResult(bool Success, string? Error, long? PublishId);

internal sealed class CounterPublishService(IDirectorDBContext db, IAirlineAccessScope accessScope) : ICounterPublishService
{
    public async Task<CounterPublishResult> PublishAsync(CounterPublishRequest request, CancellationToken ct = default)
    {
        var denied = await CheckAccessAsync(request.ActingUsername, request.AirlineId, ct);
        if (denied is not null) return denied;

        var user = await db.TblUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserPrincipalName == request.ActingUsername, ct);

        var publish = new TblCounterPublish
        {
            SentUtc = DateTime.UtcNow,
            ActorKind = "user",
            UserId = user?.UserId,
            ActorName = user?.FriendlyName ?? request.ActingUsername,
            ActingForAirlineId = request.AirlineId,
            StationCounterId = request.CounterId,
            CounterTemplateId = request.CounterTemplateId,
            CounterTemplateKind = request.CounterTemplateKind,
            CounterTemplateName = request.CounterTemplateName,
            Sunflower = request.Sunflower,
            Destination = request.Destination,
            PageShowSeconds = request.PageShowSeconds,
        };
        db.TblCounterPublish.Add(publish);
        await db.SaveChangesAsync(ct);

        db.TblCounterPublishTargets.Add(new TblCounterPublishTarget
        {
            PublishId = publish.PublishId,
            CounterId = request.CounterId,
            DisplayId = 0,
            DisplayName = request.CounterId,
            Half = "full",
        });

        var state = await db.TblCounterStates.FirstOrDefaultAsync(s => s.CounterId == request.CounterId, ct);
        if (state is null)
        {
            state = new TblCounterState { CounterId = request.CounterId };
            db.TblCounterStates.Add(state);
        }
        state.HolderAirlineId = request.AirlineId;
        state.Open = true;
        state.CounterTemplateId = request.CounterTemplateId;
        state.CounterTemplateKind = request.CounterTemplateKind;
        state.Sunflower = request.Sunflower;
        state.Destination = request.Destination;
        state.LastPublishId = publish.PublishId;

        await db.SaveChangesAsync(ct);
        return new CounterPublishResult(true, null, publish.PublishId);
    }

    public async Task<CounterPublishResult> CloseAsync(string counterId, string actingUsername, CancellationToken ct = default)
    {
        var state = await db.TblCounterStates.FirstOrDefaultAsync(s => s.CounterId == counterId, ct);
        var airlineId = state?.HolderAirlineId;

        // A counter with no recorded state (never published to through this path) has nothing airline-
        // scoped to check — allow the close through rather than block on a check that can't resolve.
        if (airlineId is not null)
        {
            var denied = await CheckAccessAsync(actingUsername, airlineId.Value, ct);
            if (denied is not null) return denied;
        }

        if (state is not null)
        {
            state.Open = false;
            state.HolderAirlineId = null;
            state.CounterTemplateId = null;
            state.CounterTemplateKind = null;
            state.Destination = null;
            await db.SaveChangesAsync(ct);
        }

        return new CounterPublishResult(true, null, state?.LastPublishId);
    }

    private async Task<CounterPublishResult?> CheckAccessAsync(string actingUsername, int airlineId, CancellationToken ct)
    {
        var allowed = await accessScope.GetAllowedAirlineIdsForUserAsync(actingUsername, ct);
        if (allowed is not null && !allowed.Contains(airlineId))
            return new CounterPublishResult(false, "Not authorized to publish for this airline.", null);
        return null;
    }
}
