using System.Text;
using System.Text.RegularExpressions;
using DXSuite.Director.Entities;
using DXSuite.Sentinel.Entities;
using DXSuite.Shared.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DXSuite.Sentinel.Services.Pulse;

/// <summary>Why a Pulse device is offered as a link for a Sentinel device.</summary>
public enum PulseSuggestionReason
{
    DirectorIdIsDeviceId,
    NameIsDeviceId,
    NameIsDescription,
}

public sealed record PulseSuggestion(PulseDevice Device, PulseSuggestionReason Reason)
{
    public string ReasonText => Reason switch
    {
        PulseSuggestionReason.DirectorIdIsDeviceId => "Director id = DeviceID",
        PulseSuggestionReason.NameIsDeviceId => "name = DeviceID",
        _ => "name = Description",
    };
}

/// <summary>Create in Pulse, as the operator confirmed it.</summary>
public sealed record PulseCreateRequest(string DeviceId, string Description, int DeviceTypeId, bool Monitor);

public enum PulseBulkAction
{
    Skip,
    Link,
    Create,
}

/// <summary>One unlinked row of a bulk apply. <see cref="PulseDeviceId"/> is the suggestion for Link.</summary>
public sealed record PulseBulkRow(int DeviceInstanceId, PulseBulkAction Action, string? PulseDeviceId = null, PulseCreateRequest? Create = null);

public sealed record PulseBulkRowResult(int DeviceInstanceId, PulseBulkAction Action, PulseLinkResult Result);

/// <summary>A Director URL monitor as Monitored Devices shows it. <see cref="DeviceInstanceId"/> is its reporting device.</summary>
public sealed record PulseUrlMonitorRow(int DirectorUrlId, string MonitorId, string UrlKey, bool IsMonitored, string AlertState, int? DeviceInstanceId)
{
    public bool IsMissing => string.Equals(AlertState, "missing", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Whether Start or Stop monitoring may run now, and the heartbeat Start writes with <c>Monitor = 1</c>.</summary>
public sealed record PulseMonitorChange(bool Allowed, DateTime? HeartbeatUtc, string? RefusalReason);

/// <summary>How a link action ended, with the message the operator sees.</summary>
public sealed record PulseLinkResult(bool Success, string Message, PulseOutcome? PulseOutcome = null)
{
    public static PulseLinkResult Ok(string message) => new(true, message);
    public static PulseLinkResult Fail(string message, PulseOutcome? outcome = null) => new(false, message, outcome);
}

/// <summary>
/// The operator's Pulse actions on Monitored Devices: link, change link, unlink, Retry after a broken link,
/// Create in Pulse, snooze, start and stop monitoring, bulk apply, and attaching URL monitors to a reporting device. Links are explicit and never inferred; suggestions only rank the
/// picker. A link change or unlink deletes the device's publisher state, so the next cycle starts as a first
/// run and nothing is written to the old Pulse device. See docs/spec/sentinel-pulse-reporting.md sections 3 and 6.
/// </summary>
public sealed class PulseLinkService
{
    private static readonly Regex GuidPattern = new(
        "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ISentinelDBContext _db;
    private readonly PulseGateway _pulse;
    private readonly PulseReportingSettingsResolver _resolver;
    private readonly ITenantContext _tenant;
    private readonly IServiceProvider _services;
    private readonly ILogger<PulseLinkService> _logger;

    public PulseLinkService(
        ISentinelDBContext db,
        PulseGateway pulse,
        PulseReportingSettingsResolver resolver,
        ITenantContext tenant,
        IServiceProvider services,
        ILogger<PulseLinkService> logger)
    {
        _db = db;
        _pulse = pulse;
        _resolver = resolver;
        _tenant = tenant;
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Strong matches only, case-insensitive: the Director id equals the Pulse DeviceID, or the display name
    /// equals the DeviceID or Description. Pulse devices another Sentinel device already links are left out.
    /// </summary>
    public static IReadOnlyList<PulseSuggestion> Suggestions(
        DeviceInstance device, IEnumerable<PulseDevice> pulseDevices, IReadOnlySet<string> linkedElsewhere)
    {
        var suggestions = new List<PulseSuggestion>();
        foreach (var p in pulseDevices)
        {
            if (linkedElsewhere.Contains(p.DeviceId)) continue;
            PulseSuggestionReason? reason =
                Same(device.DirectorDeviceId, p.DeviceId) ? PulseSuggestionReason.DirectorIdIsDeviceId
                : Same(device.DisplayName, p.DeviceId) ? PulseSuggestionReason.NameIsDeviceId
                : Same(device.DisplayName, p.Description) ? PulseSuggestionReason.NameIsDescription
                : null;
            if (reason is { } r) suggestions.Add(new PulseSuggestion(p, r));
        }
        return suggestions.OrderBy(s => s.Reason).ThenBy(s => s.Device.DeviceId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The DeviceID Create in Pulse proposes: the Director id when it is not a GUID (cloud devices carry
    /// <c>cloud-{connector}-{guid}</c>, which means nothing in Pulse), otherwise an upper-case slug of the
    /// display name.
    /// </summary>
    public static string DefaultDeviceId(DeviceInstance device)
    {
        var director = device.DirectorDeviceId?.Trim();
        if (!string.IsNullOrEmpty(director) && !GuidPattern.IsMatch(director) && director.Length <= PulseGateway.MaxDeviceIdLength)
            return director;
        return Slug(device.DisplayName);
    }

    /// <summary>Letters, digits and single hyphens, upper case, capped at the DeviceID column.</summary>
    public static string Slug(string? text)
    {
        var sb = new StringBuilder();
        foreach (var c in (text ?? "").ToUpperInvariant())
        {
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > PulseGateway.MaxDeviceIdLength) slug = slug[..PulseGateway.MaxDeviceIdLength].TrimEnd('-');
        return slug;
    }

    /// <summary>The planner's own rule: Monitor may be chosen only while this yields a heartbeat.</summary>
    public async Task<(DateTime? HeartbeatUtc, string? SuppressionReason)> HeartbeatEligibilityAsync(int deviceInstanceId, CancellationToken ct = default)
    {
        var sources = await _db.DeviceSourceStates.AsNoTracking()
            .Where(s => s.DeviceInstanceId == deviceInstanceId)
            .ToListAsync(ct);
        return PulsePublishPlanner.HeartbeatEligibility(sources, TruncateToSecond(DateTime.UtcNow));
    }

    /// <summary>
    /// Stop is always allowed. Start follows Create in Pulse's Monitor rule: only while the planner would send a
    /// heartbeat now, because a monitored device whose heartbeat is stale raises a down alert on Pulse's next run.
    /// </summary>
    public static PulseMonitorChange PlanMonitorChange(bool monitor, IReadOnlyCollection<DeviceSourceState> sources, DateTime nowUtc)
    {
        if (!monitor) return new PulseMonitorChange(true, null, null);
        var (heartbeat, suppression) = PulsePublishPlanner.HeartbeatEligibility(sources, nowUtc);
        return heartbeat is null
            ? new PulseMonitorChange(false, null, suppression)
            : new PulseMonitorChange(true, heartbeat, null);
    }

    public async Task<PulseLinkResult> LinkAsync(int deviceInstanceId, string pulseDeviceId, CancellationToken ct = default)
    {
        pulseDeviceId = pulseDeviceId.Trim();
        var device = await _db.DeviceInstances.FirstOrDefaultAsync(d => d.DeviceInstanceId == deviceInstanceId, ct);
        if (device is null) return PulseLinkResult.Fail("Device not found.");
        if (string.Equals(device.PulseDeviceId, pulseDeviceId, StringComparison.OrdinalIgnoreCase))
            return PulseLinkResult.Ok($"{device.DisplayName} is already linked to {pulseDeviceId}.");

        if (await LinkedElsewhereAsync(deviceInstanceId, pulseDeviceId, ct) is { } other)
            return PulseLinkResult.Fail($"{pulseDeviceId} is already linked to {other}. Unlink it there first.");

        var usage = await _pulse.CheckDeviceIdAsync(pulseDeviceId, ct);
        if (!usage.IsSuccess) return PulseFailure("check the Pulse device", usage.Outcome, usage.Error);
        if (!usage.Value!.ExistsForCustomer)
            return PulseLinkResult.Fail($"{pulseDeviceId} does not exist under Pulse customer {CustomerId}.");

        await SetLinkAsync(device, pulseDeviceId, ct);
        return PulseLinkResult.Ok($"Linked {device.DisplayName} to {pulseDeviceId}.");
    }

    /// <summary>Leaves the Pulse device in place. If it is monitored, Pulse shows it down once heartbeats stop.</summary>
    public async Task<PulseLinkResult> UnlinkAsync(int deviceInstanceId, CancellationToken ct = default)
    {
        var device = await _db.DeviceInstances.FirstOrDefaultAsync(d => d.DeviceInstanceId == deviceInstanceId, ct);
        if (device is null) return PulseLinkResult.Fail("Device not found.");
        var old = device.PulseDeviceId;
        if (old is null) return PulseLinkResult.Ok($"{device.DisplayName} is not linked.");

        await SetLinkAsync(device, null, ct);
        return PulseLinkResult.Ok($"Unlinked {device.DisplayName}. {old} stays in Pulse.");
    }

    /// <summary>Clears the link broken mark so the next cycle tries the linked Pulse device again.</summary>
    public async Task<PulseLinkResult> RetryAsync(int deviceInstanceId, CancellationToken ct = default)
    {
        var state = await _db.SentinelPulseDeviceStates.FirstOrDefaultAsync(s => s.DeviceInstanceId == deviceInstanceId, ct);
        if (state?.LinkBrokenUtc is null) return PulseLinkResult.Ok("The link is not marked broken.");

        state.LinkBrokenUtc = null;
        state.ConsecutiveFailures = 0;
        await _db.SaveChangesAsync(ct);
        return PulseLinkResult.Ok($"Retrying {state.PulseDeviceId} on the next publish cycle.");
    }

    /// <summary>
    /// Inserts the Pulse device with every NOT NULL column explicit, then links it. Refuses a DeviceID the
    /// customer already has, and refuses Monitor unless Sentinel can vouch for the device right now: a
    /// monitored device with no heartbeat raises a Mission Critical alert within minutes.
    /// </summary>
    public async Task<PulseLinkResult> CreateInPulseAsync(int deviceInstanceId, PulseCreateRequest request, CancellationToken ct = default)
    {
        var deviceId = request.DeviceId.Trim();
        if (deviceId.Length is 0 or > PulseGateway.MaxDeviceIdLength)
            return PulseLinkResult.Fail($"A DeviceID must be 1 to {PulseGateway.MaxDeviceIdLength} characters.");

        var device = await _db.DeviceInstances.FirstOrDefaultAsync(d => d.DeviceInstanceId == deviceInstanceId, ct);
        if (device is null) return PulseLinkResult.Fail("Device not found.");
        if (device.PulseDeviceId is not null)
            return PulseLinkResult.Fail($"{device.DisplayName} is already linked to {device.PulseDeviceId}.");

        DateTime? heartbeat = null;
        if (request.Monitor)
        {
            var (eligible, suppression) = await HeartbeatEligibilityAsync(deviceInstanceId, ct);
            if (eligible is null)
                return PulseLinkResult.Fail($"Monitor is not available: Sentinel cannot vouch for {device.DisplayName} ({suppression}).");
            heartbeat = eligible;
        }

        var usage = await _pulse.CheckDeviceIdAsync(deviceId, ct);
        if (!usage.IsSuccess) return PulseFailure("check the DeviceID", usage.Outcome, usage.Error);
        if (usage.Value!.ExistsForCustomer)
            return PulseLinkResult.Fail($"{deviceId} already exists under {CustomerId}. Link to it instead.", PulseOutcome.DuplicateDeviceId);

        var description = string.IsNullOrWhiteSpace(request.Description) ? device.DisplayName : request.Description.Trim();
        var created = await _pulse.CreateDeviceAsync(
            new PulseNewDevice(deviceId, description, request.DeviceTypeId, request.Monitor, heartbeat), ct);
        if (!created.IsSuccess) return PulseFailure("create the Pulse device", created.Outcome, created.Error);

        _logger.LogInformation("Created Pulse device {PulseDeviceId} under {Customer} for Sentinel device {DeviceInstanceId}, Monitor {Monitor}",
            deviceId, CustomerId, deviceInstanceId, request.Monitor);

        await SetLinkAsync(device, deviceId, ct);
        var shared = usage.Value.OtherCustomerIds.Count > 0 ? $" It also exists under {string.Join(", ", usage.Value.OtherCustomerIds)}." : "";
        return PulseLinkResult.Ok($"Created {deviceId} in Pulse and linked {device.DisplayName}.{shared}");
    }

    /// <param name="untilUtc">The snooze end, already converted from the viewer's local time.</param>
    public async Task<PulseLinkResult> SnoozeAsync(int deviceInstanceId, DateTime untilUtc, CancellationToken ct = default)
    {
        if (untilUtc <= DateTime.UtcNow) return PulseLinkResult.Fail("Pick a snooze end in the future.");
        var linked = await LinkedPulseIdAsync(deviceInstanceId, ct);
        if (linked is null) return PulseLinkResult.Fail("The device is not linked to Pulse.");

        var result = await _pulse.SnoozeAsync(linked, untilUtc, ct);
        return result.IsSuccess
            ? PulseLinkResult.Ok($"Snoozed {linked}.")
            : PulseFailure($"snooze {linked}", result.Outcome, result.Error);
    }

    public async Task<PulseLinkResult> UnsnoozeAsync(int deviceInstanceId, CancellationToken ct = default)
    {
        var linked = await LinkedPulseIdAsync(deviceInstanceId, ct);
        if (linked is null) return PulseLinkResult.Fail("The device is not linked to Pulse.");

        var result = await _pulse.UnsnoozeAsync(linked, ct);
        return result.IsSuccess
            ? PulseLinkResult.Ok($"Unsnoozed {linked}.")
            : PulseFailure($"unsnooze {linked}", result.Outcome, result.Error);
    }

    /// <summary>
    /// Sets <c>tblDevice.Monitor</c> for the linked Pulse device. This is an operator action like Snooze; the
    /// publisher never writes <c>Monitor</c> (spec section 8, invariant 2).
    /// </summary>
    public async Task<PulseLinkResult> SetMonitorAsync(int deviceInstanceId, bool monitor, string? userName, CancellationToken ct = default)
    {
        var device = await _db.DeviceInstances.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceInstanceId == deviceInstanceId, ct);
        if (device is null) return PulseLinkResult.Fail("Device not found.");
        if (device.PulseDeviceId is not { } linked) return PulseLinkResult.Fail("The device is not linked to Pulse.");

        var sources = await _db.DeviceSourceStates.AsNoTracking()
            .Where(s => s.DeviceInstanceId == deviceInstanceId)
            .ToListAsync(ct);
        var change = PlanMonitorChange(monitor, sources, TruncateToSecond(DateTime.UtcNow));
        if (!change.Allowed)
            return PulseLinkResult.Fail($"Start monitoring is not available: Sentinel cannot vouch for {device.DisplayName} ({change.RefusalReason}).");

        var result = await _pulse.SetMonitorAsync(linked, monitor, change.HeartbeatUtc, ct);
        if (result.Outcome == PulseOutcome.PermissionDenied)
        {
            _logger.LogWarning("Pulse refused the Monitor change on {PulseDeviceId}: {Error}", linked, result.Error);
            return PulseLinkResult.Fail("The Pulse login is not allowed to change Monitor. Ask the DBA to re-apply the Sentinel grants.", result.Outcome);
        }
        if (!result.IsSuccess)
            return PulseFailure($"{(monitor ? "start" : "stop")} monitoring {linked}", result.Outcome, result.Error);

        _logger.LogInformation("{User} set Pulse Monitor = {Monitor} on {PulseDeviceId} under {Customer} for Sentinel device {DeviceInstanceId}",
            userName ?? "unknown user", monitor ? 1 : 0, linked, CustomerId, deviceInstanceId);

        return monitor
            ? PulseLinkResult.Ok($"Pulse now monitors {linked}. Its heartbeat was written at the same time, so it starts up.")
            : PulseLinkResult.Ok($"Pulse will no longer raise down alerts for {linked}. Readings and heartbeats keep flowing.");
    }

    /// <summary>Runs the rows in order. Each reports its own result; one failure does not stop the rest.</summary>
    public async Task<IReadOnlyList<PulseBulkRowResult>> BulkApplyAsync(IEnumerable<PulseBulkRow> rows, CancellationToken ct = default)
    {
        var results = new List<PulseBulkRowResult>();
        foreach (var row in rows)
        {
            PulseLinkResult result;
            try
            {
                result = row.Action switch
                {
                    PulseBulkAction.Link when row.PulseDeviceId is { } id => await LinkAsync(row.DeviceInstanceId, id, ct),
                    PulseBulkAction.Create when row.Create is { } create => await CreateInPulseAsync(row.DeviceInstanceId, create, ct),
                    PulseBulkAction.Skip => PulseLinkResult.Ok("Skipped."),
                    _ => PulseLinkResult.Fail("Nothing to apply for this row."),
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Bulk Pulse {Action} failed for device {DeviceInstanceId}", row.Action, row.DeviceInstanceId);
                result = PulseLinkResult.Fail(ex.Message);
            }
            results.Add(new PulseBulkRowResult(row.DeviceInstanceId, row.Action, result));
        }
        return results;
    }

    /// <summary>The tenant's URL monitors, or null for a tenant without Director, which has none.</summary>
    public async Task<IReadOnlyList<PulseUrlMonitorRow>?> ReadUrlMonitorsAsync(CancellationToken ct = default)
    {
        if (Director() is not { } director) return null;
        return await director.TblDirectorUrls.AsNoTracking()
            .OrderBy(u => u.MonitorId)
            .Select(u => new PulseUrlMonitorRow(u.DirectorUrlId, u.MonitorId, u.UrlKey, u.IsMonitored, u.AlertState, u.DeviceInstanceId))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Makes the device the URL's reporting device, moving it off any other. The URL reaches Pulse only while
    /// it is monitored and the device is linked; attaching to an unlinked device is allowed and waits for the link.
    /// </summary>
    public async Task<PulseLinkResult> AttachUrlAsync(int deviceInstanceId, int directorUrlId, CancellationToken ct = default)
    {
        if (Director() is not { } director) return PulseLinkResult.Fail("This tenant has no Director URL monitors.");

        var device = await _db.DeviceInstances.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceInstanceId == deviceInstanceId && d.IsEnabled, ct);
        if (device is null) return PulseLinkResult.Fail("Device not found.");

        var url = await director.TblDirectorUrls.FirstOrDefaultAsync(u => u.DirectorUrlId == directorUrlId, ct);
        if (url is null) return PulseLinkResult.Fail("URL monitor not found.");
        if (url.DeviceInstanceId == deviceInstanceId)
            return PulseLinkResult.Ok($"{url.MonitorId} already reports through {device.DisplayName}.");

        var previous = url.DeviceInstanceId is { } prevId
            ? await _db.DeviceInstances.AsNoTracking().Where(d => d.DeviceInstanceId == prevId).Select(d => d.DisplayName).FirstOrDefaultAsync(ct) ?? $"#{prevId}"
            : null;

        url.DeviceInstanceId = deviceInstanceId;
        url.LastEditDate = DateTime.UtcNow;
        await director.SaveChangesAsync(ct);

        _logger.LogInformation("URL monitor {MonitorId} now reports through Sentinel device {DeviceInstanceId} (was {Previous})",
            url.MonitorId, deviceInstanceId, previous ?? "none");

        var moved = previous is null ? "" : $" It no longer reports through {previous}.";
        var waits = url.IsMonitored ? "" : " It is not monitored, so nothing reaches Pulse until it is monitored on Director's URLs tab.";
        return PulseLinkResult.Ok($"{url.MonitorId} now reports through {device.DisplayName}.{moved}{waits}");
    }

    /// <summary>Clears the reporting device. An open URL missing issue on that device closes on the next cycle.</summary>
    public async Task<PulseLinkResult> DetachUrlAsync(int directorUrlId, CancellationToken ct = default)
    {
        if (Director() is not { } director) return PulseLinkResult.Fail("This tenant has no Director URL monitors.");

        var url = await director.TblDirectorUrls.FirstOrDefaultAsync(u => u.DirectorUrlId == directorUrlId, ct);
        if (url is null) return PulseLinkResult.Fail("URL monitor not found.");
        if (url.DeviceInstanceId is null) return PulseLinkResult.Ok($"{url.MonitorId} has no reporting device.");

        url.DeviceInstanceId = null;
        url.LastEditDate = DateTime.UtcNow;
        await director.SaveChangesAsync(ct);
        return PulseLinkResult.Ok($"Detached {url.MonitorId}. It no longer reaches Pulse.");
    }

    // Resolving the Director context throws for a tenant that does not license Director, so check first.
    private IDirectorDBContext? Director() =>
        _tenant.DirectorConnectionString is null ? null : _services.GetRequiredService<IDirectorDBContext>();

    private string CustomerId => _resolver.Resolve(_tenant.Subdomain).Settings.CustomerId;

    private async Task<string?> LinkedPulseIdAsync(int deviceInstanceId, CancellationToken ct) =>
        await _db.DeviceInstances.AsNoTracking()
            .Where(d => d.DeviceInstanceId == deviceInstanceId)
            .Select(d => d.PulseDeviceId)
            .FirstOrDefaultAsync(ct);

    // Two Sentinel devices publishing to one Pulse device would overwrite each other's heartbeat and readings.
    private async Task<string?> LinkedElsewhereAsync(int deviceInstanceId, string pulseDeviceId, CancellationToken ct) =>
        await _db.DeviceInstances.AsNoTracking()
            .Where(d => d.IsEnabled && d.DeviceInstanceId != deviceInstanceId && d.PulseDeviceId == pulseDeviceId)
            .Select(d => d.DisplayName)
            .FirstOrDefaultAsync(ct);

    private async Task SetLinkAsync(DeviceInstance device, string? pulseDeviceId, CancellationToken ct)
    {
        device.PulseDeviceId = pulseDeviceId;
        device.LastEditDate = DateTime.UtcNow;

        var id = device.DeviceInstanceId;
        _db.SentinelPulseItemStates.RemoveRange(await _db.SentinelPulseItemStates.Where(s => s.DeviceInstanceId == id).ToListAsync(ct));
        _db.SentinelPulseDeviceStates.RemoveRange(await _db.SentinelPulseDeviceStates.Where(s => s.DeviceInstanceId == id).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
    }

    private PulseLinkResult PulseFailure(string action, PulseOutcome outcome, string? error)
    {
        _logger.LogWarning("Pulse link action '{Action}' failed: {Outcome} {Error}", action, outcome, error);
        var text = outcome switch
        {
            PulseOutcome.Unreachable => "Pulse could not be reached",
            PulseOutcome.NotConfigured => "Pulse is not configured for this tenant",
            PulseOutcome.DeviceMissing => "the Pulse device no longer exists",
            PulseOutcome.DuplicateDeviceId => "that DeviceID already exists",
            PulseOutcome.PermissionDenied => "the Pulse login lacks a grant it needs",
            _ => "Pulse rejected it",
        };
        return PulseLinkResult.Fail($"Could not {action}: {text}.", outcome);
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static DateTime TruncateToSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
