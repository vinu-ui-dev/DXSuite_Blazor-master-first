namespace DXSuite.Director.Services;

/// <summary>
/// What a device's reported time zone fingerprint says about the zone DXSuite intended for it.
/// Derived on read, never stored: Director has no device fault table, and a mismatch is an
/// observation rather than an event.
/// </summary>
public enum TimeZoneVerificationState
{
    /// <summary>Nothing to judge: no fingerprint, or one missing the year it was taken in.</summary>
    NotReported,

    /// <summary>The bootloader has not applied the intended literal yet. Nothing is wrong.</summary>
    Pending,

    /// <summary>The intended literal was applied and the zone's rules agree with it.</summary>
    Verified,

    /// <summary>The intended literal was applied and the rules disagree. This is the result.</summary>
    Mismatch
}

/// <summary>
/// What the player reports about the zone it is actually running, on the normal heartbeat.
/// </summary>
/// <param name="OffsetJanuaryHours">UTC offset in force at 15 January of <paramref name="Year"/>.</param>
/// <param name="OffsetJulyHours">UTC offset in force at 15 July of <paramref name="Year"/>.</param>
/// <param name="Year">The year those two instants were taken in, reported rather than assumed.</param>
/// <param name="PosixString">The literal in force on the player.</param>
/// <param name="AppliedLiteral">The literal the bootloader last applied, from engage/tz_applied.</param>
public readonly record struct TimeZoneFingerprint(
    decimal? OffsetJanuaryHours,
    decimal? OffsetJulyHours,
    int? Year,
    string? PosixString,
    string? AppliedLiteral);

/// <summary>
/// Verifies a pushed time zone by comparing the zone's rules rather than its name.
///
/// A name is not available: under a POSIX-prefixed zone, which is the only form DXSuite sends, the
/// player's Intl reports no zone identity at all. ICU holds the offset rules and nothing else. The
/// clock is correct throughout, so the rules are what there is to compare.
///
/// Two instants rather than one, because January alone cannot separate Phoenix from Denver: both
/// sit at -7 in winter, so a single sample passes the Arizona error for four months a year. Do not
/// optimise this back down to one instant; a vector exists whose only job is to fail when somebody
/// does.
/// </summary>
public static class TimeZoneVerification
{
    public static TimeZoneVerificationState Evaluate(
        string? intendedIanaId, string? intendedSetTimeZoneArgument, TimeZoneFingerprint? reported)
    {
        if (reported is not { } fingerprint) return TimeZoneVerificationState.NotReported;

        // A missing year lands the device in "nothing to compare against yet" rather than being
        // judged against a guess. A hardcoded year would rot as the server computes past the app's
        // release, and deriving it on both sides races at the December boundary.
        if (fingerprint.Year is not { } year) return TimeZoneVerificationState.NotReported;
        if (fingerprint.OffsetJanuaryHours is not { } reportedJanuary) return TimeZoneVerificationState.NotReported;
        if (fingerprint.OffsetJulyHours is not { } reportedJuly) return TimeZoneVerificationState.NotReported;

        // Pending versus mismatch is decided by what the bootloader says it applied, not by a
        // timeout: tblDevice has no change history, so DXSuite cannot date the push. The player can.
        if (!string.Equals(fingerprint.AppliedLiteral, intendedSetTimeZoneArgument, StringComparison.Ordinal))
            return TimeZoneVerificationState.Pending;

        if (!TryGetOffsets(intendedIanaId, year, out var intendedJanuary, out var intendedJuly))
            return TimeZoneVerificationState.NotReported;

        return reportedJanuary == intendedJanuary && reportedJuly == intendedJuly
            ? TimeZoneVerificationState.Verified
            : TimeZoneVerificationState.Mismatch;
    }

    private static bool TryGetOffsets(string? ianaId, int year, out decimal january, out decimal july)
    {
        january = 0;
        july = 0;

        if (string.IsNullOrWhiteSpace(ianaId)) return false;

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            january = (decimal)zone.GetUtcOffset(new DateTime(year, 1, 15, 12, 0, 0, DateTimeKind.Utc)).TotalHours;
            july = (decimal)zone.GetUtcOffset(new DateTime(year, 7, 15, 12, 0, 0, DateTimeKind.Utc)).TotalHours;
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // An intended zone that will not resolve leaves nothing to compare against, which is
            // the same honest answer as a device that has reported nothing.
            return false;
        }
    }
}
