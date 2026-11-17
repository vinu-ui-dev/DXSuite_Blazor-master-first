namespace DXSuite.Host.Logging;

/// <summary>
/// Appends every Editor-bridge (Director SSO) step to its own file, logs/bridge-yyyyMMdd.log, independent
/// of ILogger providers and levels. Tries the app folder first and falls back to the temp folder if the
/// app folder is not writable (common under IIS). Never throws, and never receives secrets: callers log
/// presence/length/status only, not codes, verifiers, states or the client secret.
/// </summary>
public static class BridgeFileLog
{
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
                    var file = Path.Combine(dir, $"bridge-{DateTime.Now:yyyyMMdd}.log");
                    if (_lastDir != dir)
                    {
                        _lastDir = dir;
                        File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [BRIDGE] log file: {file}{Environment.NewLine}");
                    }
                    File.AppendAllText(file, line);
                }
                return;
            }
            catch { /* try the next location */ }
        }
    }
}
