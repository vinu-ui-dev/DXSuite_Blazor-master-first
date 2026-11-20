using System.Data.Common;

namespace DXSuite.Shared.Services;

/// <summary>
/// The sentence a screen shows for a failure. A database failure's own text names tables, columns and servers, which a
/// user must never see: it is replaced by the plain sentence the caller gives, and the real error stays in the log.
/// Any other failure's message is the app's own sentence and is shown as it is.
/// </summary>
public static class UserFacingError
{
    private static readonly string[] DatabaseMarkers =
    [
        "invalid object name", "invalid column name", "sqlexception", "sqlite", "sql server", "connection string",
        "network-related", "login failed", "deadlock", "violation of", "cannot insert", "foreign key", "unique index",
        "dbupdateexception", "entity framework", "an error occurred while saving", "an error occurred while updating",
        "transport-level", "timeout expired", "tbl",
    ];

    /// <summary>The message to show: <paramref name="fallback"/> when <paramref name="ex"/> is a database failure or its text mentions the database.</summary>
    public static string From(Exception ex, string fallback)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is DbException) return fallback;

        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message)) return fallback;
        foreach (var marker in DatabaseMarkers)
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase)) return fallback;
        return message;
    }
}
