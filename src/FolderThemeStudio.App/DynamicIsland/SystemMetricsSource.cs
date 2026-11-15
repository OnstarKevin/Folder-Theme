using System.Runtime.InteropServices;

namespace FolderThemeStudio.App.DynamicIsland;

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
public sealed record SystemMetrics(double? CpuPercent, double? MemoryPercent, ulong UsedMemoryBytes, ulong TotalMemoryBytes);
public interface ISystemMetricsSource
{
    SystemMetrics Read();
    SystemMetrics Read(bool cpu, bool memory) => Read();
}

/// <summary>GetSystemTimes 的 Kernel 已包含 Idle；CPU 用两个采样间的增量计算，而不是开机累计值。</summary>
public static class CpuUsageCalculator
{
    public static double? Calculate(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User) return null;
        var idle = current.Idle - previous.Idle;
        var total = (double)(current.Kernel - previous.Kernel) + (current.User - previous.User);
        return total <= 0 || idle > total ? null : Math.Clamp((total - idle) / total * 100, 0, 100);
    }
}

/// <summary>使用 Win32 获取全机 CPU/物理内存，不依赖性能计数器或管理员权限。</summary>
public sealed class WindowsSystemMetricsSource : ISystemMetricsSource
{
    private CpuTimes? previous;
    public SystemMetrics Read() => Read(true, true);
    public SystemMetrics Read(bool includeCpu, bool includeMemory)
    {
        double? cpu = null, memory = null;
        if (!includeCpu) previous = null;
        if (includeCpu && GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var next = new CpuTimes(idle.Value, kernel.Value, user.Value);
            if (previous is { } last) cpu = CpuUsageCalculator.Calculate(last, next);
            previous = next;
        }
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        ulong used = 0, total = 0;
        if (includeMemory && GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0)
        {
            total = status.TotalPhysical;
            used = total - Math.Min(total, status.AvailablePhysical);
            memory = used / (double)total * 100;
        }
        return new(cpu, memory, used, total);
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileTime
    {
        internal uint Low, High;
        internal readonly ulong Value => ((ulong)High << 32) | Low;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        internal uint Length, Load;
        internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
