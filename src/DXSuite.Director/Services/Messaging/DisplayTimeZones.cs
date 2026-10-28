using DXSuite.Director.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DXSuite.Director.Services.Messaging;

/// <summary>
/// The clock each display reads a scheduled Message's wall-clock times against (MSG-16, MSG-17). A display is not a
/// place; its device is (time zone scheduling spec, section 2), so a display takes its device's <c>tblDevice.TimeZone</c>.
/// A display with no device, such as GTAA's reference displays, or whose device's zone does not resolve, takes
/// <see cref="MessagingOptions.DefaultTimeZone"/> (A-15).
/// </summary>
/// <remarks>
/// A display whose devices sit in different zones takes the zone of its first device by DeviceId that resolves: Messages
/// are arbitrated and delivered per display, so one display has one clock. No 1.1 site has such a display.
/// </remarks>
public sealed class DisplayTimeZones
{
    private readonly ILogger<DisplayTimeZones> _logger;

    public DisplayTimeZones(IOptions<MessagingOptions> options, ILogger<DisplayTimeZones> logger)
    {
        _logger = logger;
        var id = options.Value.DefaultTimeZone;
        if (string.IsNullOrWhiteSpace(id))
        {
            Default = TimeZoneInfo.Local;
            ConfigurationProblem = $"Messaging:DefaultTimeZone is not set, so displays with no device use this server's time zone ({Default.Id}).";
        }
        else if (TryFind(id.Trim(), out var zone))
        {
            Default = zone;
        }
        else
        {
            Default = TimeZoneInfo.Local;
            ConfigurationProblem = $"Messaging:DefaultTimeZone \"{id}\" is not a known time zone, so displays with no device use this server's time zone ({Default.Id}).";
        }

        if (ConfigurationProblem is not null) _logger.LogWarning("{Problem}", ConfigurationProblem);
    }

    /// <summary>The zone of a display with no device.</summary>
    public TimeZoneInfo Default { get; }

    /// <summary>Why <see cref="MessagingOptions.DefaultTimeZone"/> could not be used, or null when it was.</summary>
    public string? ConfigurationProblem { get; }

    /// <summary>Each display's zone, read now from its devices; every id asked for is in the result.</summary>
    public Task<IReadOnlyDictionary<int, TimeZoneInfo>> ForDisplaysAsync(DirectorDbContext db, IReadOnlyCollection<int> displayIds,
        CancellationToken ct) =>
        ForDisplaysAsync(db.TblDevices, displayIds, ct);

    /// <summary>
    /// Each Activation's status at <paramref name="nowUtc"/> (<see cref="ActivationState.Of"/>), read on the clocks of
    /// its frozen screens; <paramref name="screens"/> maps an Activation id to its screens' display ids. Only the
    /// Activations that are active or scheduled read any clock.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, ActivationStatus>> StatusesAsync(IQueryable<TblDevice> devices,
        IReadOnlyCollection<TblMessageActivation> activations, ILookup<int, int> screens, DateTime nowUtc, CancellationToken ct)
    {
        var activeOrScheduled = activations.Where(a => ActivationState.IsActiveOrScheduled(a, nowUtc)).ToList();
        var timeZones = activeOrScheduled.Count == 0
            ? new Dictionary<int, TimeZoneInfo>().AsReadOnly()
            : await ForDisplaysAsync(devices, activeOrScheduled.SelectMany(a => screens[a.MessageActivationId]).Distinct().ToList(), ct);

        return activations.ToDictionary(a => a.MessageActivationId, a => ActivationState.Of(a, nowUtc,
            screens[a.MessageActivationId].Distinct().Where(timeZones.ContainsKey).Select(id => timeZones[id]).Distinct().ToList()));
    }

    /// <summary>As the other overload, for Activations loaded with their <c>Targets</c>.</summary>
    public Task<IReadOnlyDictionary<int, ActivationStatus>> StatusesAsync(IQueryable<TblDevice> devices,
        IReadOnlyCollection<TblMessageActivation> activations, DateTime nowUtc, CancellationToken ct) =>
        StatusesAsync(devices, activations,
            activations.SelectMany(a => a.Targets.Where(t => t.DisplayId != null).Select(t => (a.MessageActivationId, DisplayId: t.DisplayId!.Value)))
                .ToLookup(s => s.MessageActivationId, s => s.DisplayId),
            nowUtc, ct);

    private async Task<IReadOnlyDictionary<int, TimeZoneInfo>> ForDisplaysAsync(IQueryable<TblDevice> devices, IReadOnlyCollection<int> displayIds,
        CancellationToken ct)
    {
        var ids = displayIds.Distinct().ToList();
        var deviceZones = await devices.AsNoTracking()
            .Where(d => d.DisplayId != null && ids.Contains(d.DisplayId.Value))
            .Select(d => new { DisplayId = d.DisplayId!.Value, d.DeviceId, d.TimeZone })
            .ToListAsync(ct);

        var result = new Dictionary<int, TimeZoneInfo>();
        foreach (var id in ids)
        {
            var zone = Default;
            foreach (var device in deviceZones.Where(d => d.DisplayId == id).OrderBy(d => d.DeviceId, StringComparer.Ordinal))
            {
                if (TryFind(device.TimeZone, out var found))
                {
                    zone = found;
                    break;
                }

                _logger.LogWarning("Device {DeviceId} carries time zone '{TimeZoneId}', which does not resolve; its display's Messages are not read on it.",
                    device.DeviceId, device.TimeZone);
            }

            result[id] = zone;
        }

        return result;
    }

    private static bool TryFind(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
