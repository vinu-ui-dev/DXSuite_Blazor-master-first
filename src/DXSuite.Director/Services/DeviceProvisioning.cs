using DXSuite.Director.Entities;

namespace DXSuite.Director.Services;

/// <summary>
/// The one place a new tblDevice row gets its time zone. Four paths create devices, in two
/// assemblies, and every one of them routes through here: seeding inline would be four places to
/// forget and a fifth path would be born wrong.
///
/// tblDevice.TimeZone has no database default on purpose, so a path that skips this helper throws
/// on insert and surfaces in development rather than quietly stamping a wrong zone on a real
/// player.
/// </summary>
public static class DeviceProvisioning
{
    /// <summary>
    /// Written when the host's zone cannot be expressed as an IANA identifier, which is what
    /// happens under invariant globalization. It is a literal that does not resolve, so the device
    /// plays unfiltered and is visibly wrong, rather than being given a plausible zone nobody
    /// chose. Throwing instead would mean a player that cannot register at all.
    /// </summary>
    public const string UnresolvedTimeZone = "UNSET";

    /// <summary>The only writer of this provenance value in the solution.</summary>
    private const string SeededSource = "Seeded";

    /// <summary>
    /// Written by every path except the seed helper: the DevicesTab cell, the bulk script, and the
    /// upgrade backfill. Counting the backfill as confirming is deliberate, since those are
    /// single-zone deployments where a human did choose the value.
    /// </summary>
    public const string OperatorSource = "Operator";

    /// <summary>
    /// Whether a device's zone is confirmed enough to send to it. A seeded zone is the Director
    /// host's, which nobody looked at, and pushing it would replace one wrong zone with another and
    /// spend a reboot doing it.
    /// </summary>
    public static bool IsOperatorConfirmed(string? timeZoneSource)
        => string.Equals(timeZoneSource, OperatorSource, StringComparison.Ordinal);

    /// <summary>
    /// Stamps the zone columns on a newly constructed device and returns it, so a creation site
    /// reads as one expression and cannot half-apply the seed.
    /// </summary>
    public static TblDevice Seed(TblDevice device)
    {
        device.TimeZone = HostIanaTimeZone();
        device.TimeZoneSource = SeededSource;
        return device;
    }

    /// <summary>
    /// The Director host's own zone as IANA. Not TimeZoneInfo.Local.Id directly, which on Windows
    /// yields a Windows identifier such as "Eastern Standard Time": the eight tblDisplay rows that
    /// held one are the evidence that this leak has happened once before.
    /// </summary>
    private static string HostIanaTimeZone()
        => TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var ianaId)
            ? ianaId
            : UnresolvedTimeZone;
}
