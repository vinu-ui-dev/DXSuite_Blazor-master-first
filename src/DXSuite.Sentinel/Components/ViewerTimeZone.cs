using Microsoft.JSInterop;

namespace DXSuite.Sentinel.Components;

/// <summary>
/// The browser's time zone, so times stored in UTC are shown and entered in the viewer's local time.
/// Blazor Server renders on the server, whose zone says nothing about the viewer, so the IANA id is read
/// from the browser once per circuit. Falls back to UTC until the browser answers or if it cannot.
/// </summary>
public sealed class ViewerTimeZone
{
    public static readonly ViewerTimeZone Utc = new(TimeZoneInfo.Utc, isFallback: true);

    private ViewerTimeZone(TimeZoneInfo zone, bool isFallback)
    {
        Zone = zone;
        IsFallback = isFallback;
    }

    public TimeZoneInfo Zone { get; }

    /// <summary>True when the browser's zone is unknown and times are shown in UTC.</summary>
    public bool IsFallback { get; }

    public string Name => IsFallback ? "UTC" : Zone.Id;

    public static async Task<ViewerTimeZone> DetectAsync(IJSRuntime js)
    {
        try
        {
            var id = await js.InvokeAsync<string>("eval", "Intl.DateTimeFormat().resolvedOptions().timeZone");
            return string.IsNullOrWhiteSpace(id) ? Utc : new ViewerTimeZone(TimeZoneInfo.FindSystemTimeZoneById(id), isFallback: false);
        }
        catch (Exception ex) when (ex is JSException or TimeZoneNotFoundException or InvalidTimeZoneException or InvalidOperationException)
        {
            return Utc;
        }
    }

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>A wall-clock time that falls in a spring-forward gap does not exist, so it moves an hour later.</summary>
    public DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    /// <summary>"today 3:40 PM", "tomorrow 8:00 AM", or "Sep 21 8:00 AM", in local time.</summary>
    public string Short(DateTime utc)
    {
        var local = ToLocal(utc);
        var today = ToLocal(DateTime.UtcNow).Date;
        var time = local.ToString("h:mm tt");
        if (local.Date == today) return $"today {time}";
        if (local.Date == today.AddDays(1)) return $"tomorrow {time}";
        return local.Year == today.Year ? $"{local:MMM d} {time}" : $"{local:MMM d, yyyy} {time}";
    }

    public string Full(DateTime utc) => $"{ToLocal(utc):yyyy-MM-dd HH:mm:ss} ({Name})";
}
