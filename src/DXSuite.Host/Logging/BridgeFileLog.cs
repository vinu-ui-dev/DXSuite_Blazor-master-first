namespace DXSuite.Host.Logging;

/// <summary>
/// Appends editor-bridge steps straight to logs/bridge-yyyyMMdd.log, independent of ILogger levels
/// and providers (which have not shown these lines). Falls back to the temp folder if the app folder
/// is not writable, and never throws.
/// </summary>
public static class BridgeFileLog
{
    private static readonly object Gate = new();

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
                    File.AppendAllText(Path.Combine(dir, $"bridge-{DateTime.Now:yyyyMMdd}.log"), line);
                }
                return;
            }
            catch { /* try the next location */ }
        }
    }
}
