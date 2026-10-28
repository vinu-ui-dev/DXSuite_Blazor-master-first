using System.Runtime.InteropServices;
using DXSuite.Sentinel.Contracts;

namespace DXSuite.SentinelGateway;

/// <summary>
/// Gathers the agent host's own resource metrics for the "self-monitor" role. Cross-platform:
/// memory and CPU use Win32 (kernel32) on Windows and /proc on Linux, with graceful null fallbacks
/// elsewhere. Disk/uptime/OS come from the BCL. Pure read-only sampling — no external dependencies.
/// </summary>
public static class HostMetricsCollector
{
    public static async Task<HostMetrics> CollectAsync(CancellationToken ct = default)
    {
        var m = new HostMetrics
        {
            MachineName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            UptimeSeconds = Environment.TickCount64 / 1000,
            Disks = ReadDisks()
        };

        var (total, available, loadPercent) = ReadMemory();
        if (total > 0)
        {
            m.MemoryTotalBytes = total;
            m.MemoryAvailableBytes = available;
            m.MemoryUsedPercent = loadPercent ?? Math.Round((1 - (double)available / total) * 100, 1);
        }

        m.CpuPercent = await ReadCpuPercentAsync(ct);
        return m;
    }

    private static List<DiskInfo> ReadDisks()
    {
        var disks = new List<DiskInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType != DriveType.Fixed || d.TotalSize <= 0) continue;
                var used = d.TotalSize - d.TotalFreeSpace;
                disks.Add(new DiskInfo
                {
                    Name = d.Name,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.TotalFreeSpace,
                    UsedPercent = Math.Round((double)used / d.TotalSize * 100, 1)
                });
            }
            catch { /* a drive can drop offline between enumeration and query — skip it */ }
        }
        return disks;
    }

    // Returns (totalBytes, availableBytes, loadPercent?) — loadPercent is set only where the OS reports it directly.
    private static (long total, long available, double? loadPercent) ReadMemory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref status))
                    return ((long)status.ullTotalPhys, (long)status.ullAvailPhys, status.dwMemoryLoad);
            }
            else if (OperatingSystem.IsLinux())
            {
                long total = 0, available = 0;
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:")) total = ParseMemInfoKb(line);
                    else if (line.StartsWith("MemAvailable:")) available = ParseMemInfoKb(line);
                    if (total > 0 && available > 0) break;
                }
                if (total > 0) return (total, available, null);
            }
        }
        catch { /* fall through to fallback */ }

        // Last resort: runtime's view of total memory; available unknown.
        var info = GC.GetGCMemoryInfo();
        return info.TotalAvailableMemoryBytes > 0 ? (info.TotalAvailableMemoryBytes, 0, null) : (0, 0, null);
    }

    private static long ParseMemInfoKb(string line)
    {
        // "MemTotal:       16327012 kB"
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var kb) ? kb * 1024 : 0;
    }

    private static async Task<double?> ReadCpuPercentAsync(CancellationToken ct)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return await WindowsCpuAsync(ct);
            if (OperatingSystem.IsLinux()) return await LinuxCpuAsync(ct);
        }
        catch { /* metric is best-effort */ }
        return null;
    }

    private static async Task<double?> WindowsCpuAsync(CancellationToken ct)
    {
        if (!GetSystemTimes(out var idle0, out var kernel0, out var user0)) return null;
        await Task.Delay(500, ct);
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return null;

        var idle = ToTicks(idle1) - ToTicks(idle0);
        // On Windows, kernel time already includes idle time, so total = kernel + user.
        var total = (ToTicks(kernel1) - ToTicks(kernel0)) + (ToTicks(user1) - ToTicks(user0));
        if (total <= 0) return null;
        return Math.Round((1 - (double)idle / total) * 100, 1);
    }

    private static async Task<double?> LinuxCpuAsync(CancellationToken ct)
    {
        var (idle0, total0) = ReadProcStat();
        await Task.Delay(500, ct);
        var (idle1, total1) = ReadProcStat();
        var totalDelta = total1 - total0;
        if (totalDelta <= 0) return null;
        return Math.Round((1 - (double)(idle1 - idle0) / totalDelta) * 100, 1);
    }

    private static (long idle, long total) ReadProcStat()
    {
        // First line: "cpu  user nice system idle iowait irq softirq steal guest guest_nice"
        var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
        if (line is null) return (0, 0);
        var nums = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).Select(s => long.TryParse(s, out var v) ? v : 0).ToArray();
        if (nums.Length < 5) return (0, 0);
        var idle = nums[3] + (nums.Length > 4 ? nums[4] : 0); // idle + iowait
        var total = nums.Sum();
        return (idle, total);
    }

    private static ulong ToTicks(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);
}
