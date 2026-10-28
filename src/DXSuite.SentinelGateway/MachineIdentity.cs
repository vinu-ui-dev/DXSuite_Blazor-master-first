using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace DXSuite.SentinelGateway;

/// <summary>
/// Produces a stable per-machine fingerprint used as the enrollment idempotency key, so a re-install
/// or lost local identity re-claims the same gateway rather than creating duplicates. Derived from the
/// OS machine id (Windows registry MachineGuid / Linux /etc/machine-id) plus the hostname, hashed.
/// Falls back to a hostname-only hash if the machine id can't be read.
/// </summary>
public static class MachineIdentity
{
    public static string GetFingerprint()
    {
        var machineId = ReadMachineId() ?? "no-machine-id";
        var raw = $"{machineId}:{Environment.MachineName}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string? ReadMachineId()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                    if (File.Exists(path))
                    {
                        var id = File.ReadAllText(path).Trim();
                        if (id.Length > 0) return id;
                    }
            }
            else if (OperatingSystem.IsWindows())
            {
                return ReadWindowsMachineGuid();
            }
        }
        catch { /* fall back to hostname-only */ }
        return null;
    }

    private static string? ReadWindowsMachineGuid()
    {
        // Read via reg.exe to avoid taking a Microsoft.Win32.Registry package dependency.
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = @"query ""HKLM\SOFTWARE\Microsoft\Cryptography"" /v MachineGuid",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            // Output line: "    MachineGuid    REG_SZ    xxxxxxxx-...."
            foreach (var line in output.Split('\n'))
                if (line.Contains("MachineGuid", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3) return parts[^1].Trim();
                }
        }
        catch { /* fall back to hostname-only */ }
        return null;
    }
}
