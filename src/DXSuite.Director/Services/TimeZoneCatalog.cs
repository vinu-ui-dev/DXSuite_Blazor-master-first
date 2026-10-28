namespace DXSuite.Director.Services;

/// <summary>
/// One entry of the curated time zone list: what an operator picks, what the server resolves
/// windows against, and what a player is told to be.
/// </summary>
/// <param name="Label">What an operator sees.</param>
/// <param name="IanaId">Stored on tblDevice.TimeZone and used for every server-side resolution.</param>
/// <param name="SetTimeZoneArgument">
/// The complete argument a BrightSign player's SetTimeZone takes, including the POSIX: prefix.
/// Sent verbatim, so nothing builds a string at either end and a correction is one data edit.
/// </param>
public readonly record struct TimeZoneOption(string Label, string IanaId, string SetTimeZoneArgument);

/// <summary>
/// The curated list of time zones DXSuite offers, US and Canada.
///
/// A short authored list rather than the whole IANA database: an operator picking from twenty
/// familiar names cannot pick a zone nobody meant, and every row's POSIX literal is a fact somebody
/// checked rather than a string some code assembled.
///
/// The literals are the POSIX footers tzdata ships, copied rather than derived. Three of them are
/// verified on hardware: America/Chicago, America/Denver and America/Phoenix. Arizona is why the
/// list holds a literal at all, since the plausible-looking alternatives for it are both wrong: a
/// bare MST is DST-aware and lands on Denver, and BrightSign's own documented MST1 lands in the
/// Atlantic six hours out.
/// </summary>
public static class TimeZoneCatalog
{
    public static IReadOnlyList<TimeZoneOption> Options { get; } = new List<TimeZoneOption>
    {
        new("Eastern Time (US & Canada)", "America/New_York", "POSIX:EST5EDT,M3.2.0,M11.1.0"),
        new("Eastern Time - Toronto", "America/Toronto", "POSIX:EST5EDT,M3.2.0,M11.1.0"),
        new("Eastern Time - Indiana", "America/Indiana/Indianapolis", "POSIX:EST5EDT,M3.2.0,M11.1.0"),
        new("Central Time (US & Canada)", "America/Chicago", "POSIX:CST6CDT,M3.2.0,M11.1.0"),
        new("Central Time - Winnipeg", "America/Winnipeg", "POSIX:CST6CDT,M3.2.0,M11.1.0"),
        new("Central Time - Saskatchewan (no DST)", "America/Regina", "POSIX:CST6"),
        new("Mountain Time (US & Canada)", "America/Denver", "POSIX:MST7MDT,M3.2.0,M11.1.0"),
        new("Mountain Time - Edmonton", "America/Edmonton", "POSIX:MST7MDT,M3.2.0,M11.1.0"),
        new("Mountain Time - Arizona (no DST)", "America/Phoenix", "POSIX:MST7"),
        new("Mountain Time - Yukon (no DST)", "America/Whitehorse", "POSIX:MST7"),
        new("Pacific Time (US & Canada)", "America/Los_Angeles", "POSIX:PST8PDT,M3.2.0,M11.1.0"),
        new("Pacific Time - Vancouver", "America/Vancouver", "POSIX:PST8PDT,M3.2.0,M11.1.0"),
        new("Alaska Time", "America/Anchorage", "POSIX:AKST9AKDT,M3.2.0,M11.1.0"),
        new("Hawaii Time (no DST)", "Pacific/Honolulu", "POSIX:HST10"),
        new("Atlantic Time - Halifax", "America/Halifax", "POSIX:AST4ADT,M3.2.0,M11.1.0"),
        new("Atlantic Time - Puerto Rico (no DST)", "America/Puerto_Rico", "POSIX:AST4"),
        new("Newfoundland Time", "America/St_Johns", "POSIX:NST3:30NDT,M3.2.0,M11.1.0"),
    };

    /// <summary>
    /// The SetTimeZone argument for a stored IANA identifier, or null when the identifier is not on
    /// the list. A device carrying an off-list zone is sent nothing rather than a guess.
    /// </summary>
    public static string? FindSetTimeZoneArgument(string? ianaId)
        => ianaId is null
            ? null
            : Options.FirstOrDefault(o => string.Equals(o.IanaId, ianaId, StringComparison.Ordinal))
                .SetTimeZoneArgument;
}
