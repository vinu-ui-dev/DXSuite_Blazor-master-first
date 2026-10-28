using System.Globalization;

namespace DXSuite.Shared.Audit;

/// <summary>Text formatting shared by every User Event Log report and export (times stored UTC, exported as
/// ISO 8601 UTC — FSD 1.1 AUD-12).</summary>
public static class UserEventLogFilterText
{
    public static string Iso(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

/// <summary>Where the (not yet built — see the GTAA FSD 1.1 completion notes) User Event Log report lives, so
/// other reports can deep-link into it by target without a hard reference to a page that doesn't exist yet.</summary>
public static class UserEventLog
{
    public const string Route = "/reports/user-event-log";
}
