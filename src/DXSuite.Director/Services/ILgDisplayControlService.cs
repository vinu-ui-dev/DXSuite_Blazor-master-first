namespace DXSuite.Director.Services;

/// <summary>
/// Service for controlling LG commercial displays via TCP (RS-232 over IP)
/// Uses port 9761 with standard LG display protocol commands
/// </summary>
public interface ILgDisplayControlService
{
    /// <summary>
    /// Sends a power on command to the display
    /// </summary>
    Task<LgCommandResult> PowerOnAsync(string ipAddress, int setId = 1, int timeoutMs = 5000);

    /// <summary>
    /// Sends a power off command to the display
    /// </summary>
    Task<LgCommandResult> PowerOffAsync(string ipAddress, int setId = 1, int timeoutMs = 5000);

    /// <summary>
    /// Queries the current power status of the display
    /// </summary>
    Task<LgCommandResult> GetPowerStatusAsync(string ipAddress, int setId = 1, int timeoutMs = 5000);

    /// <summary>
    /// Performs a power cycle: RS-232 power off -> Wake-on-LAN -> RS-232 power on.
    /// Uses WoL to bring the NIC back up after standby kills the network stack.
    /// </summary>
    Task<LgCommandResult> PowerCycleAsync(string ipAddress, int setId = 1, int delayMs = 5000, int timeoutMs = 5000, string? macAddress = null);

    /// <summary>
    /// Sends a raw command to the display
    /// </summary>
    Task<LgCommandResult> SendCommandAsync(string ipAddress, string command, int timeoutMs = 5000);

    /// <summary>
    /// Tests connectivity to the display on port 9761
    /// </summary>
    Task<bool> TestConnectionAsync(string ipAddress, int timeoutMs = 3000);
}

/// <summary>
/// Result of an LG display command
/// </summary>
public class LgCommandResult
{
    public bool Success { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
    public int? PowerState { get; set; }  // 0 = off, 1 = on (for power status queries)
    public long ElapsedMs { get; set; }
}
