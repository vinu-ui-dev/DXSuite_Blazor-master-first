namespace DXSuite.Director.Services;

/// <summary>
/// A start and end time of day that recurs every day. Start inclusive, end exclusive.
///
/// Two exceptions, and they are mutually exclusive by construction: a start later than the end
/// wraps midnight, and an end at or after 23:59 runs through the end of the day because the
/// authoring UI is minute-granular and cannot express 23:59:59. Wrapping requires a start later
/// than the end, which no end at or after 23:59 permits, so the two can never interact.
///
/// A missing start means 00:00, a missing end means end of day, and both missing means always open.
///
/// It is pure: it reads no clock and takes no time zone. The caller decides which zone the local
/// time it passes came from, which is what lets one rule serve the server-side evaluation of every
/// schedule surface without a clock seam.
/// </summary>
public readonly record struct DailyWindow(TimeOnly? Start, TimeOnly? End)
{
    private static readonly TimeOnly EndOfDayThreshold = new(23, 59);

    public bool Contains(TimeOnly localTime)
    {
        var start = Start ?? TimeOnly.MinValue;
        var end = End ?? TimeOnly.MaxValue;

        if (start > end) return localTime >= start || localTime < end;
        if (end >= EndOfDayThreshold) return localTime >= start;
        return localTime >= start && localTime < end;
    }
}
