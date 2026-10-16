using System.Runtime.InteropServices;

namespace NetInspector.Core;

// Windows counters cover the whole computer, not only this process.
public sealed class SystemResources
{
    private (ulong Idle, ulong Kernel, ulong User)? previous;
    public ResourceSample Read()
    {
        double? cpu = null, memory = null;
        ulong? used = null, total = null;
        var errors = new List<string>();
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (previous is { } p) cpu = CpuUsage(p.Idle, p.Kernel, p.User, idle, kernel, user);
            previous = (idle, kernel, user);
        }
        else { previous = null; errors.Add("CPU: contador Windows indisponível"); }
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0 && status.AvailablePhysical <= status.TotalPhysical)
        {
            total = status.TotalPhysical; used = status.TotalPhysical - status.AvailablePhysical;
            memory = used.Value * 100.0 / total.Value;
        }
        else errors.Add("Memória: contador Windows indisponível");
        return new(DateTimeOffset.UtcNow, cpu, memory, used, total, errors.Count == 0 ? null : string.Join("; ", errors), ReadPower());
    }
    public static PowerSample? ReadPower()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        return DecodePower(status.Ac, status.Flags, status.Percent, status.Seconds);
    }
    public static PowerSample DecodePower(byte ac, byte flags, byte percent, uint seconds) => new(
        ac == 255 ? null : ac == 1, flags == 255 ? null : (flags & 128) == 0,
        flags == 255 || (flags & 128) != 0 || percent > 100 ? null : percent,
        flags == 255 || (flags & 128) != 0 ? null : (flags & 8) != 0,
        seconds == uint.MaxValue || seconds > int.MaxValue ? null : (int)seconds);
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte Ac, Flags, Percent, Reserved; public uint Seconds, FullSeconds; }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
    public static double? CpuUsage(ulong oldIdle, ulong oldKernel, ulong oldUser, ulong idle, ulong kernel, ulong user)
    {
        if (idle < oldIdle || kernel < oldKernel || user < oldUser) return null;
        var elapsed = (double)(kernel - oldKernel) + (user - oldUser);
        return elapsed <= 0 ? null : Math.Clamp((elapsed - (idle - oldIdle)) * 100 / elapsed, 0, 100);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

public static class ResourceSummary
{
    public static string Describe(Session session)
    {
        static string Metric(string name, IEnumerable<double?> source)
        {
            var values = source.OfType<double>().ToArray();
            return values.Length == 0 ? $"{name}: não medido" : $"{name}: média {values.Average():F1}%; pico {values.Max():F1}% ({values.Length} amostras)";
        }
        return Metric("CPU", session.Resources.Select(r => r.CpuPercent)) + " • " + Metric("Memória física", session.Resources.Select(r => r.MemoryPercent));
    }
}
