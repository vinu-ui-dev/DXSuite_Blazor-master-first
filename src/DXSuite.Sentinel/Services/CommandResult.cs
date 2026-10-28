namespace DXSuite.Sentinel.Services;

/// <summary>
/// Result of executing a command on a device, regardless of communication path.
/// Captures the outcome, timing, and routing information for audit and diagnostics.
/// </summary>
public class CommandResult
{
    public bool Success { get; set; }
    public string CommandId { get; set; } = string.Empty;
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
    public long ElapsedMs { get; set; }
    public string RoutedVia { get; set; } = string.Empty;
    public Dictionary<string, object>? ResponseData { get; set; }

    // Populated for Task / script executions (CommandId = "run-script").
    public int? ExitCode { get; set; }
    public string? Stdout { get; set; }
    public string? Stderr { get; set; }
}
