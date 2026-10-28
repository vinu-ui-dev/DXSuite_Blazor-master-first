using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace DXSuite.Director.Services;

/// <summary>
/// Controls LG commercial displays via TCP (RS-232 over IP) on port 9761
///
/// Protocol format: [Command][SetID][Data][CR]
/// - Command: 2-character command code (e.g., "ka" for power)
/// - SetID: 2-character hex (00 = all, 01-FF = specific display)
/// - Data: Command-specific value
/// - CR: Carriage return (0x0D)
///
/// Response format: [Command][SetID][OK/NG][Data]
/// - OK = success, NG = failure
///
/// Common commands:
/// - ka XX 00 = Power off
/// - ka XX 01 = Power on
/// - ka XX FF = Query power status
/// </summary>
public class LgDisplayControlService : ILgDisplayControlService
{
    private const int TCP_PORT = 9761;
    private readonly ILogger<LgDisplayControlService> _logger;

    public LgDisplayControlService(ILogger<LgDisplayControlService> logger)
    {
        _logger = logger;
    }

    public async Task<LgCommandResult> PowerOnAsync(string ipAddress, int setId = 1, int timeoutMs = 5000)
    {
        var command = FormatCommand("ka", setId, "01");
        return await SendCommandAsync(ipAddress, command, timeoutMs);
    }

    public async Task<LgCommandResult> PowerOffAsync(string ipAddress, int setId = 1, int timeoutMs = 5000)
    {
        var command = FormatCommand("ka", setId, "00");
        return await SendCommandAsync(ipAddress, command, timeoutMs);
    }

    public async Task<LgCommandResult> GetPowerStatusAsync(string ipAddress, int setId = 1, int timeoutMs = 5000)
    {
        var command = FormatCommand("ka", setId, "FF");
        var result = await SendCommandAsync(ipAddress, command, timeoutMs);

        // Parse power state from response
        // Response format: "a 01 OK01" (on) or "a 01 OK00" (off)
        if (result.Success && !string.IsNullOrEmpty(result.Response))
        {
            if (result.Response.Contains("OK01") || result.Response.Contains("OK1"))
            {
                result.PowerState = 1; // On
            }
            else if (result.Response.Contains("OK00") || result.Response.Contains("OK0"))
            {
                result.PowerState = 0; // Off
            }
        }

        return result;
    }

    public async Task<LgCommandResult> PowerCycleAsync(string ipAddress, int setId = 1, int delayMs = 5000, int timeoutMs = 5000, string? macAddress = null)
    {
        var stopwatch = Stopwatch.StartNew();

        // Stage 1: Power off via RS-232
        _logger.LogInformation("PowerCycle {IP}: sending RS-232 power off", ipAddress);
        var offResult = await PowerOffAsync(ipAddress, setId, timeoutMs);
        if (!offResult.Success)
        {
            _logger.LogWarning("PowerCycle {IP}: power off failed — {Error}", ipAddress, offResult.ErrorMessage);
            return new LgCommandResult
            {
                Success = false,
                Command = "power_cycle",
                ErrorMessage = $"Power off failed: {offResult.ErrorMessage}",
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }

        _logger.LogInformation("PowerCycle {IP}: power off acknowledged — {Response}", ipAddress, offResult.Response);

        // Stage 2: Wait for NIC to drop
        _logger.LogDebug("PowerCycle {IP}: waiting 5s for NIC to drop", ipAddress);
        await Task.Delay(5000);

        if (!string.IsNullOrEmpty(macAddress))
        {
            // Stage 3: Send WoL waves at T+10s, T+15s, T+20s, T+25s post-off
            var wolWaves = new[] { 10, 15, 20, 25 };
            var elapsedSinceOff = 5;

            foreach (var targetSec in wolWaves)
            {
                var waitMs = (targetSec - elapsedSinceOff) * 1000;
                if (waitMs > 0) await Task.Delay(waitMs);
                elapsedSinceOff = targetSec;

                var (sent, iface) = SendWakeOnLan(macAddress, ipAddress);
                _logger.LogInformation("PowerCycle {IP}: WoL wave at T+{Sec}s — {Sent} packets via {Iface}",
                    ipAddress, targetSec, sent, iface);
            }

            // Stage 4: Poll for ping recovery — 5s intervals, 30s max
            _logger.LogDebug("PowerCycle {IP}: polling for ping recovery", ipAddress);
            var recovered = false;
            for (int i = 0; i < 6; i++)
            {
                await Task.Delay(5000);
                if (await PingAsync(ipAddress))
                {
                    _logger.LogInformation("PowerCycle {IP}: ping recovered after {Sec}s",
                        ipAddress, stopwatch.ElapsedMilliseconds / 1000);
                    recovered = true;
                    break;
                }
            }

            if (!recovered)
            {
                _logger.LogWarning("PowerCycle {IP}: display never became pingable after WoL", ipAddress);
                return new LgCommandResult
                {
                    Success = false,
                    Command = "power_cycle",
                    ErrorMessage = "Display powered off but never responded to WoL",
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                };
            }

            // Wait for RS-232 port to come up after NIC recovery
            await Task.Delay(5000);
        }
        else
        {
            _logger.LogWarning("PowerCycle {IP}: no MAC address, skipping WoL", ipAddress);
            await Task.Delay(2000);
        }

        // Stage 5: RS-232 power on for confirmation
        var retryDelays = new[] { 0, 3000, 5000 };
        foreach (var retryDelay in retryDelays)
        {
            if (retryDelay > 0) await Task.Delay(retryDelay);

            var onResult = await PowerOnAsync(ipAddress, setId, timeoutMs);
            if (onResult.Success)
            {
                _logger.LogInformation("PowerCycle {IP}: complete in {Sec}s",
                    ipAddress, stopwatch.ElapsedMilliseconds / 1000);
                return new LgCommandResult
                {
                    Success = true,
                    Command = "power_cycle",
                    Response = onResult.Response,
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        // Final check — if pingable, WoL worked even if RS-232 port isn't responding
        if (await PingAsync(ipAddress))
        {
            _logger.LogInformation("PowerCycle {IP}: pingable but RS-232 unresponsive — treating as success", ipAddress);
            return new LgCommandResult
            {
                Success = true,
                Command = "power_cycle",
                Response = "WoL OK, RS-232 unresponsive",
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }

        return new LgCommandResult
        {
            Success = false,
            Command = "power_cycle",
            ErrorMessage = "Display powered off but could not confirm power on",
            ElapsedMs = stopwatch.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// ICMP ping with 3-second timeout.
    /// </summary>
    private static async Task<bool> PingAsync(string ipAddress, int timeoutMs = 3000)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends WoL magic packets via the network interface whose subnet contains the target device IP.
    /// Falls back to all interfaces if no subnet match is found.
    /// Returns (packetsSent, interfaceDescription).
    /// </summary>
    private (int Sent, string Interface) SendWakeOnLan(string macAddress, string targetIp)
    {
        var macBytes = macAddress.Split(':', '-')
            .Select(b => Convert.ToByte(b, 16))
            .ToArray();

        var payload = new byte[102];
        for (int i = 0; i < 6; i++) payload[i] = 0xFF;
        for (int i = 0; i < 16; i++) Array.Copy(macBytes, 0, payload, 6 + (i * 6), 6);

        var targetBytes = IPAddress.Parse(targetIp).GetAddressBytes();
        var subnetBroadcast = $"{targetBytes[0]}.{targetBytes[1]}.{targetBytes[2]}.255";
        var broadcasts = new[] { subnetBroadcast, "255.255.255.255" };
        var ports = new[] { 9, 7, 0 };

        // Find the interface whose subnet contains the target IP
        var matchedInterface = FindMatchingInterface(targetBytes);

        if (matchedInterface != null)
        {
            var (ni, localAddr) = matchedInterface.Value;
            var sent = SendWolFromAddress(localAddr, payload, broadcasts, ports);
            return (sent, $"{ni.Name} ({localAddr})");
        }

        // Fallback: send on all interfaces
        _logger.LogWarning("WoL: no subnet match for {TargetIP}, sending on all interfaces", targetIp);
        int totalSent = 0;
        var names = new List<string>();

        foreach (var ni in GetActiveIPv4Interfaces())
        {
            foreach (var addr in GetIPv4Addresses(ni))
            {
                totalSent += SendWolFromAddress(addr, payload, broadcasts, ports);
                names.Add(ni.Name);
            }
        }

        return (totalSent, $"all ({string.Join(", ", names.Distinct())})");
    }

    /// <summary>
    /// Finds the network interface whose subnet contains the target IP address.
    /// </summary>
    private static (NetworkInterface Ni, IPAddress LocalAddr)? FindMatchingInterface(byte[] targetBytes)
    {
        foreach (var ni in GetActiveIPv4Interfaces())
        {
            var ipProps = ni.GetIPProperties();
            foreach (var unicast in ipProps.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var localBytes = unicast.Address.GetAddressBytes();
                var maskBytes = unicast.IPv4Mask.GetAddressBytes();

                // Check if target is on the same subnet: (target & mask) == (local & mask)
                bool match = true;
                for (int i = 0; i < 4; i++)
                {
                    if ((targetBytes[i] & maskBytes[i]) != (localBytes[i] & maskBytes[i]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return (ni, unicast.Address);
            }
        }

        return null;
    }

    private static IEnumerable<NetworkInterface> GetActiveIPv4Interfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
    }

    private static IEnumerable<IPAddress> GetIPv4Addresses(NetworkInterface ni)
    {
        return ni.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address);
    }

    /// <summary>
    /// Sends WoL packets from a specific local address to all broadcast/port combinations.
    /// </summary>
    private int SendWolFromAddress(IPAddress localAddr, byte[] payload, string[] broadcasts, int[] ports)
    {
        int sent = 0;
        foreach (var bcast in broadcasts)
        {
            foreach (var port in ports)
            {
                try
                {
                    using var udp = new UdpClient(new IPEndPoint(localAddr, 0));
                    udp.EnableBroadcast = true;
                    udp.Send(payload, payload.Length, new IPEndPoint(IPAddress.Parse(bcast), port));
                    sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "WoL send failed from {Local} to {Bcast}:{Port}", localAddr, bcast, port);
                }
            }
        }
        return sent;
    }

    public async Task<LgCommandResult> SendCommandAsync(string ipAddress, string command, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new LgCommandResult { Command = command };

        try
        {
            using var client = new TcpClient();

            // Connect with timeout
            var connectTask = client.ConnectAsync(ipAddress, TCP_PORT);
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
            {
                result.ErrorMessage = $"Connection timeout after {timeoutMs}ms";
                _logger.LogWarning("TCP connection to {IpAddress}:{Port} timed out", ipAddress, TCP_PORT);
                return result;
            }

            await connectTask; // Ensure any exception is thrown

            using var stream = client.GetStream();
            stream.ReadTimeout = timeoutMs;
            stream.WriteTimeout = timeoutMs;

            // Send command
            var commandBytes = Encoding.ASCII.GetBytes(command);
            await stream.WriteAsync(commandBytes);
            _logger.LogDebug("Sent command to {IpAddress}: {Command}", ipAddress, command.TrimEnd('\r'));

            // Read response
            var buffer = new byte[256];
            var bytesRead = await stream.ReadAsync(buffer);
            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

            result.Response = response;
            result.Success = response.Contains("OK");

            if (!result.Success && response.Contains("NG"))
            {
                result.ErrorMessage = $"Display returned error: {response}";
            }

            _logger.LogDebug("Received response from {IpAddress}: {Response}", ipAddress, response);
        }
        catch (SocketException ex)
        {
            result.ErrorMessage = $"Socket error: {ex.Message}";
            _logger.LogWarning(ex, "Socket error communicating with display at {IpAddress}", ipAddress);
        }
        catch (IOException ex)
        {
            result.ErrorMessage = $"IO error: {ex.Message}";
            _logger.LogWarning(ex, "IO error communicating with display at {IpAddress}", ipAddress);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
            _logger.LogError(ex, "Unexpected error communicating with display at {IpAddress}", ipAddress);
        }
        finally
        {
            stopwatch.Stop();
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
        }

        return result;
    }

    public async Task<bool> TestConnectionAsync(string ipAddress, int timeoutMs = 3000)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ipAddress, TCP_PORT);

            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask)
            {
                await connectTask; // Ensure connected or throw
                _logger.LogDebug("Successfully connected to {IpAddress}:{Port}", ipAddress, TCP_PORT);
                return true;
            }

            _logger.LogDebug("Connection test to {IpAddress}:{Port} timed out", ipAddress, TCP_PORT);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Connection test to {IpAddress}:{Port} failed", ipAddress, TCP_PORT);
            return false;
        }
    }

    /// <summary>
    /// Formats a command string in LG protocol format
    /// </summary>
    private static string FormatCommand(string cmd, int setId, string data)
    {
        // Format: [cmd] [setId hex] [data]\r
        return $"{cmd} {setId:X2} {data}\r";
    }
}
