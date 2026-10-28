using Microsoft.Extensions.Logging;

namespace DXSuite.Director.Services;

/// <summary>
/// Turns an instant into a device's local wall clock, given the IANA identifier stored on its row.
///
/// This is the only place an identifier is resolved, and so the only place the failure is caught.
/// Nothing validates the column, by decision: the schema carries no CHECK constraints and a list of
/// allowed identifiers would rot as the IANA database changes. An identifier that does not resolve
/// is therefore expected rather than exceptional, and it arrives here.
///
/// It never throws, and it decides nothing. An unresolved identifier comes back as false, leaving
/// the choice of what to serve with the caller that knows its own default: a playlist serves
/// unfiltered, a display serves its DefaultContent. Duplicating this catch would double the chance
/// one copy is silent, which matters more than usual here because the product has no log shipping.
/// </summary>
public static class DeviceLocalTime
{
    /// <summary>
    /// Resolves <paramref name="instant"/> into the wall clock of <paramref name="timeZoneId"/>.
    /// Returns false for a missing, unknown or corrupt identifier, including the seed sentinel.
    /// </summary>
    public static bool TryResolve(
        DateTimeOffset instant, string? timeZoneId, out DateTime localTime, ILogger? logger = null)
    {
        localTime = default;

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            logger?.LogWarning("A device carries no time zone identifier, so its schedules cannot be resolved.");
            return false;
        }

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            localTime = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger?.LogWarning(
                "Time zone '{TimeZoneId}' does not resolve, so schedules for the devices carrying it "
                + "are not evaluated against a local clock.", timeZoneId);
            return false;
        }
    }
}
