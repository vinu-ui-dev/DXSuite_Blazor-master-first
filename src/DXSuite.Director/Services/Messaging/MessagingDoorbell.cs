using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// One check of the tenant's doorbell (INK-09): has the messaging delivery version moved, or, when asked, the delivery
/// body's hash changed, since the doorbell last rang? If so it records the new pair in <c>tblMessagingDeliveryState</c>
/// and posts an empty body to <c>Messaging:Delivery:DoorbellUrl</c>, fire and forget, so the Moxie script pulls at once
/// instead of on its safety net. <see cref="MessagingDoorbellService"/> runs the checks under the tenant's lease.
/// </summary>
/// <remarks>
/// The nudge carries no data, so a lost or forged one costs at most a late or an extra pull. What the doorbell last
/// rang for lives in SQL (HOST-04 item 1), so an instance that takes the lease over carries on where the other left off.
/// </remarks>
public sealed class MessagingDoorbell
{
    public const string HttpClientName = "messaging-doorbell";

    /// <summary>How long a nudge may take before it is dropped (INK-09).</summary>
    public static readonly TimeSpan NudgeTimeout = TimeSpan.FromSeconds(2);

    private readonly IDbContextFactory<DirectorDbContext> _dbFactory;
    private readonly MessageDelivery _delivery;
    private readonly IHttpClientFactory _http;
    private readonly string? _url;
    private readonly TimeProvider _clock;
    private readonly ILogger<MessagingDoorbell> _logger;

    public MessagingDoorbell(IDbContextFactory<DirectorDbContext> dbFactory, MessageDelivery delivery, IHttpClientFactory http,
        IOptions<MessagingOptions> options, TimeProvider clock, ILogger<MessagingDoorbell> logger)
    {
        _dbFactory = dbFactory;
        _delivery = delivery;
        _http = http;
        _url = options.Value.Delivery.DoorbellOn ? options.Value.Delivery.DoorbellUrl!.Trim() : null;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc cref="MessagingDeliveryOptions.DoorbellOn"/>
    public bool IsOn => _url is not null;

    /// <summary>
    /// Rings if the version moved, or if <paramref name="recomputeHash"/> and the body's hash changed, since the last
    /// ring of any instance. Returns whether this call rang.
    /// </summary>
    public async Task<bool> CheckAsync(bool recomputeHash, CancellationToken ct)
    {
        if (!IsOn) return false;

        var seen = await ReadAsync(ct);
        if (seen is null) return false;
        var versionMoved = seen.Version != seen.NudgedVersion;
        if (!versionMoved && !recomputeHash) return false;

        var etag = (await _delivery.BuildAsync(ct)).ETag;
        if (!versionMoved && etag == seen.NudgedETag) return false;

        // A change that committed while the body was built may be missing from it. The next check sees its version and
        // rings for both, rather than ringing now and again then.
        if ((await ReadAsync(ct))?.Version != seen.Version) return false;

        if (!await TryRecordAsync(seen, etag, ct)) return false;
        Ring(_url!);
        return true;
    }

    private async Task<TblMessagingDeliveryState?> ReadAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TblMessagingDeliveryStates.AsNoTracking()
            .SingleOrDefaultAsync(s => s.MessagingDeliveryStateId == TblMessagingDeliveryState.SingleRowId, ct);
    }

    /// <summary>
    /// Records what this ring is for, only if the doorbell state is still what <paramref name="seen"/> read, so that
    /// of two instances that see the same change only one rings.
    /// </summary>
    private async Task<bool> TryRecordAsync(TblMessagingDeliveryState seen, string etag, CancellationToken ct)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var recorded = await db.TblMessagingDeliveryStates
            .Where(s => s.MessagingDeliveryStateId == TblMessagingDeliveryState.SingleRowId
                && s.NudgedVersion == seen.NudgedVersion
                && s.NudgedETag == seen.NudgedETag)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.NudgedVersion, seen.Version)
                .SetProperty(r => r.NudgedETag, etag)
                .SetProperty(r => r.NudgedUtc, nowUtc), ct);
        return recorded == 1;
    }

    // Fire and forget: the check never waits on the script, and a nudge that fails is not retried.
    private void Ring(string url) => _ = Task.Run(async () =>
    {
        try
        {
            using var client = _http.CreateClient(HttpClientName);
            using var timeout = new CancellationTokenSource(NudgeTimeout);
            using var response = await client.PostAsync(url, new ByteArrayContent([]), timeout.Token);
            _logger.LogDebug("Delivery doorbell at {Url} answered {Status}", url, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Delivery doorbell at {Url} was not answered ({Error}); the script picks the change up on its next pull",
                url, ex.GetType().Name);
        }
    });
}
