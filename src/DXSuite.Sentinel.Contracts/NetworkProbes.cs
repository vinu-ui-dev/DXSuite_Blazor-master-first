using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DXSuite.Sentinel.Contracts;

/// <summary>Outcome of a single network probe.</summary>
public record ProbeOutcome(bool Success, string Status, string Detail, Dictionary<string, object>? Data = null);

/// <summary>
/// Dependency-free network probe primitives (ICMP, TCP, latency, Wake-on-LAN) usable by
/// both the cloud probe pipeline and the edge gateway agent. Pure BCL networking — no DB,
/// no logging, no DI — so it runs anywhere with a minimal footprint. Every method returns a
/// <see cref="ProbeOutcome"/> and never throws.
/// </summary>
public static class NetworkProbes
{
    public const int DefaultTimeoutMs = 3000;

    public static async Task<ProbeOutcome> PingAsync(string host, int timeoutMs = DefaultTimeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            var ok = reply.Status == IPStatus.Success;
            return new ProbeOutcome(
                ok,
                ok ? "healthy" : "critical",
                ok ? $"Reachable ({reply.RoundtripTime}ms)" : $"Unreachable ({reply.Status})",
                new Dictionary<string, object> { ["status"] = reply.Status.ToString(), ["roundtripMs"] = reply.RoundtripTime });
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, "critical", $"Ping error: {ex.Message}");
        }
    }

    public static async Task<ProbeOutcome> TcpCheckAsync(string host, int port, int timeoutMs = DefaultTimeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask;
            if (completed && client.Connected)
                return new ProbeOutcome(true, "healthy", $"Port {port} open",
                    new Dictionary<string, object> { ["port"] = port, ["open"] = true });
            return new ProbeOutcome(false, "critical", $"Port {port} closed/timeout",
                new Dictionary<string, object> { ["port"] = port, ["open"] = false });
        }
        catch (SocketException)
        {
            return new ProbeOutcome(false, "critical", $"Port {port} refused",
                new Dictionary<string, object> { ["port"] = port, ["open"] = false });
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, "critical", $"TCP error: {ex.Message}");
        }
    }

    public static async Task<ProbeOutcome> LatencyAsync(string host, int count = 3, int timeoutMs = DefaultTimeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var rtts = new List<long>();
            for (var i = 0; i < count; i++)
            {
                var reply = await ping.SendPingAsync(host, timeoutMs);
                if (reply.Status == IPStatus.Success) rtts.Add(reply.RoundtripTime);
            }

            if (rtts.Count == 0)
                return new ProbeOutcome(false, "critical", "No response");

            var avg = (long)rtts.Average();
            return new ProbeOutcome(true, "healthy", $"{avg}ms avg ({rtts.Count}/{count} replies)",
                new Dictionary<string, object> { ["averageMs"] = avg, ["replies"] = rtts.Count });
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, "critical", $"Ping error: {ex.Message}");
        }
    }

    public static async Task<ProbeOutcome> WakeOnLanAsync(string macAddress)
    {
        try
        {
            var mac = ParseMac(macAddress);
            var packet = new byte[6 + 6 * 16];
            for (var i = 0; i < 6; i++) packet[i] = 0xFF;
            for (var i = 0; i < 16; i++) Array.Copy(mac, 0, packet, 6 + i * 6, 6);

            using var udp = new UdpClient { EnableBroadcast = true };
            await udp.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
            return new ProbeOutcome(true, "healthy", $"Wake-on-LAN sent to {macAddress}");
        }
        catch (Exception ex)
        {
            return new ProbeOutcome(false, "critical", $"Wake-on-LAN failed: {ex.Message}");
        }
    }

    private static byte[] ParseMac(string mac)
    {
        var cleaned = mac.Replace(":", "").Replace("-", "").Replace(".", "").Trim();
        if (cleaned.Length != 12)
            throw new FormatException($"MAC address must be 12 hex characters, got {cleaned.Length}");
        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        return bytes;
    }
}
