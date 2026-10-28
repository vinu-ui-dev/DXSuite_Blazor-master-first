using DXSuite.Sentinel.Entities;
using DXSuite.Sentinel.Services.Pulse;

namespace DXSuite.Sentinel.Components;

public enum PulseChipKind
{
    DownCannotVouch,
    Down,
    LinkBroken,
    MissingInPulse,
    Snoozed,
    NotMonitored,
    PulseUnavailable,
    Up,
    NotLinked,
}

/// <summary>
/// The Pulse column's status, derived for display only from Pulse's tblDevice row and Sentinel's publisher
/// state (docs/spec/sentinel-pulse-reporting.md section 6). Nothing here feeds Sentinel's health or alerting.
/// The enum order is the sort order, so devices needing attention come first.
/// </summary>
public sealed record PulseStatusChip(PulseChipKind Kind, string Label, string BadgeClass)
{
    /// <summary>Pulse's down job flags a monitored, unsnoozed device whose heartbeat is older than this.</summary>
    public static readonly TimeSpan PulseDownAfter = TimeSpan.FromMinutes(6);

    /// <param name="pulseDevices">The customer's Pulse devices, or null when the tab could not read Pulse.</param>
    public static PulseStatusChip For(
        string? pulseDeviceId,
        SentinelPulseDeviceState? state,
        IReadOnlyDictionary<string, PulseDevice>? pulseDevices,
        DateTime nowUtc,
        ViewerTimeZone zone)
    {
        if (pulseDeviceId is null) return new(PulseChipKind.NotLinked, "Not linked", "dx-badge-gray");
        if (state?.LinkBrokenUtc is not null) return new(PulseChipKind.LinkBroken, "Link broken", "dx-badge-red");
        if (pulseDevices is null) return new(PulseChipKind.PulseUnavailable, "Pulse unavailable", "dx-badge-gray");
        if (!pulseDevices.TryGetValue(pulseDeviceId, out var p)) return new(PulseChipKind.MissingInPulse, "Not found in Pulse", "dx-badge-red");
        if (!p.IsMonitored) return new(PulseChipKind.NotMonitored, "Not monitored", "dx-badge-gray");
        if (p.Snooze)
            return new(PulseChipKind.Snoozed, p.SnoozeTimeUtc is { } until ? $"Snoozed until {zone.Short(until)}" : "Snoozed", "dx-badge-blue");

        var stale = p.HeartbeatUtc is not { } hb || nowUtc - hb > PulseDownAfter;
        if (!stale) return new(PulseChipKind.Up, "Up", "dx-badge-green");
        return state?.SuppressionReason is not null
            ? new(PulseChipKind.DownCannotVouch, "Down (Sentinel cannot vouch)", "dx-badge-red")
            : new(PulseChipKind.Down, "Down", "dx-badge-red");
    }
}
