using System.Text;

namespace DXSuite.Shared.Logging;

/// <summary>
/// Appends every airline create step (the request payload, the server's answer and, for a failure, the full exception
/// chain with the database's own message) to <c>logs/airline-log.log</c>, independent of ILogger providers and levels.
/// Tries the app folder first and falls back to the temp folder if that is not writable. Never throws.
/// </summary>
public static class AirlineFileLog
{
    public const string FileName = "airline-log.log";

    private static readonly object Gate = new();
    private static string? _lastDir;

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        foreach (var dir in new[] { Path.Combine(AppContext.BaseDirectory, "logs"), Path.Combine(Path.GetTempPath(), "dxsuite-logs") })
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, FileName);
                    if (_lastDir != dir)
                    {
                        _lastDir = dir;
                        File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [AIRLINE] log file: {file}{Environment.NewLine}");
                    }
                    File.AppendAllText(file, line);
                }
                return;
            }
            catch { /* try the next location */ }
        }
    }

    /// <summary>The exception and every inner exception, one per line, so a database error's real message is not lost.</summary>
    public static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (sb.Length > 0) sb.Append("  <- caused by: ");
            sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        return sb.ToString();
    }

    /// <summary>Cuts long text (a response body) so one line cannot flood the file.</summary>
    public static string Trim(string? text, int max = 2000) =>
        string.IsNullOrEmpty(text) ? "" : text.Length > max ? text[..max] + "...(cut)" : text;
}
