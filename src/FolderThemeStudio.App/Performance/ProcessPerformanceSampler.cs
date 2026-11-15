using System.Diagnostics;

namespace FolderThemeStudio.App.Performance;

public sealed record ProcessResourceReading(int Id, long WorkingSetBytes, long PrivateBytes, TimeSpan CpuTime);
public sealed record SoftwarePerformanceSnapshot(int ProcessCount, long WorkingSetBytes, long PrivateBytes, double? CpuPercent);

/// <summary>Read-only process accounting. Separate the delta calculation from native process access.</summary>
public sealed class ProcessPerformanceSampler(int logicalProcessors)
{
    private Dictionary<int, TimeSpan> previous = [];
    private double? previousSeconds;

    public SoftwarePerformanceSnapshot Read()
    {
        var readings = new List<ProcessResourceReading>();
        using var current = Process.GetCurrentProcess();
        var path = Environment.ProcessPath;
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                try
                {
                    // Same executable path excludes unrelated processes and old installations.
                    if (process.Id != current.Id && !string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) continue;
                    readings.Add(new(process.Id, process.WorkingSet64, process.PrivateMemorySize64, process.TotalProcessorTime));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                { /* A process can exit between enumeration and reading. */ }
            }
        }
        return Calculate(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, readings);
    }

    public SoftwarePerformanceSnapshot Calculate(double seconds, IReadOnlyList<ProcessResourceReading> readings)
    {
        double? cpu = null;
        if (previousSeconds is { } prior && seconds > prior)
        {
            var delta = readings.Sum(item => previous.TryGetValue(item.Id, out var old) ? Math.Max(0, (item.CpuTime - old).TotalSeconds) : 0);
            cpu = Math.Clamp(delta / (seconds - prior) / Math.Max(1, logicalProcessors) * 100, 0, 100);
        }
        previous = readings.ToDictionary(item => item.Id, item => item.CpuTime); previousSeconds = seconds;
        return new(readings.Count, readings.Sum(item => item.WorkingSetBytes), readings.Sum(item => item.PrivateBytes), cpu);
    }

    public void Reset() { previous.Clear(); previousSeconds = null; }
}
