using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using FolderThemeStudio.App.DynamicIsland;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var application = new Application();
        var settings = new IslandSettings { ShowTime = false, ShowCpu = false, ShowMemory = false, ShowMusic = false, ShowQuotes = false, ShowClipboard = false, ShowNotifications = false, StretchMotion = false };
        var island = new DynamicIslandWindow(settings, clipboardFactory: (_, _) => new SilentClipboard());
        island.Model.SetExtensionContent("probe", "专注 24:59", "隔离性能采样", 60);
        island.ContentRendered += async (_, _) =>
        {
            try
            {
                await Task.Delay(4000); // Let first layout/JIT/render settle without loading test or settings UI.
                using var process = Process.GetCurrentProcess();
                var samples = new List<object>(); var redraws = 0;
                island.Model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IslandViewModel.CompactText)) redraws++; };
                for (var sample = 0; sample < 3; sample++)
                {
                    process.Refresh(); var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
                    await Task.Delay(2000); process.Refresh();
                    samples.Add(new { Sample = sample + 1, Seconds = watch.Elapsed.TotalSeconds, CpuPercentOfOneCore = (process.TotalProcessorTime - cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds * 100 });
                }
                var output = Path.GetFullPath(args.FirstOrDefault() ?? "island-performance.json");
                File.WriteAllText(output, JsonSerializer.Serialize(new { Samples = samples, WorkingSetBytes = process.WorkingSet64, CompactTextChanges = redraws, Scope = "standalone Island process; collectors disabled; hover observer enabled; no xUnit or plugin worker" }));
            }
            finally { island.CloseFromController(); application.Shutdown(); }
        };
        application.Run(island);
    }
    private sealed class SilentClipboard : IIslandClipboardListener { public void SetEnabled(bool enabled) { } public void Dispose() { } }
}
