using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.App.DynamicIsland.Plugins;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandVisualProbeTests
{
    [Fact]
    public void OptInOwnedWindowVisualsAndIdleResourceSample()
    {
        if (Environment.GetEnvironmentVariable("FTS_CAPTURE_BETA23") != "1") return;
        var temporary = Path.Combine(Path.GetTempPath(), "fts-visual-" + Guid.NewGuid().ToString("N"));
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-beta23"));
        Directory.CreateDirectory(output);
        using var manager = new IslandPluginManager(temporary);
        try
        {
            WpfTestHost.Invoke(() =>
            {
                var preferences = new IslandSettings { ShowTime = false, ShowCpu = false, ShowMemory = false, ShowMusic = false, ShowQuotes = false, StretchMotion = false };
                var island = new DynamicIslandWindow(preferences, clipboardFactory: (_, _) => new SilentClipboard());
                var settings = new DynamicIslandSettingsWindow(preferences, false, (_, _) => Task.CompletedTask, () => Task.FromResult(""));
                var plugins = new IslandPluginManagerWindow(manager, () => false);
                var hover = new IslandHoverWindow { DataContext = island.Model };
                try
                {
                    island.Show(); island.Model.SetExtensionContent("demo", "专注 24:59", "慢一点，把注意力放回眼前。", 60);
                    settings.Show(); plugins.Show(); hover.Show();
                    foreach (var pair in new[] { (Window: (Window)island, Name: "transparent-island"), (Window: (Window)settings, Name: "settings"), (Window: (Window)plugins, Name: "plugins"), (Window: (Window)hover, Name: "hover-card") })
                    {
                        pair.Window.UpdateLayout();
                        var root = (FrameworkElement)pair.Window.Content;
                        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(root.ActualWidth * 2)), Math.Max(1, (int)Math.Ceiling(root.ActualHeight * 2)), 192, 192, PixelFormats.Pbgra32);
                        var visual = new DrawingVisual();
                        using (var drawing = visual.RenderOpen())
                        {
                            drawing.DrawRectangle(System.Windows.Media.Brushes.WhiteSmoke, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                            drawing.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                        }
                        bitmap.Render(visual);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(output, pair.Name + ".png")); encoder.Save(stream);
                    }
                    settings.Close(); plugins.Close(); hover.Close();
                    using var process = Process.GetCurrentProcess();
                    Pump(TimeSpan.FromSeconds(1.5)); // Drain Loaded, layout, rendering and closed-dialog work before measuring idle.
                    var samples = new List<object>();
                    var redraws = 0; island.Model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IslandViewModel.CompactText)) redraws++; };
                    for (var sample = 0; sample < 3; sample++)
                    {
                        process.Refresh(); var cpu = process.TotalProcessorTime; var started = Stopwatch.StartNew();
                        var before = process.Threads.Cast<ProcessThread>().ToDictionary(t => t.Id, t => t.TotalProcessorTime);
                        Pump(TimeSpan.FromSeconds(1.5)); process.Refresh();
                        var cpuPercentOfOneCore = (process.TotalProcessorTime - cpu).TotalMilliseconds / started.Elapsed.TotalMilliseconds * 100;
                        var threads = process.Threads.Cast<ProcessThread>().Where(t => before.ContainsKey(t.Id)).Select(t => new { t.Id, CpuMs = (t.TotalProcessorTime - before[t.Id]).TotalMilliseconds }).OrderByDescending(t => t.CpuMs).Take(4).ToArray();
                        samples.Add(new { Sample = sample + 1, SampleSeconds = started.Elapsed.TotalSeconds, CpuPercentOfOneCore = cpuPercentOfOneCore, Threads = threads });
                    }
                    File.WriteAllText(Path.Combine(output, "idle-sample.json"), JsonSerializer.Serialize(new { Samples = samples, CompactTextChanges = redraws, WorkingSetBytes = process.WorkingSet64, Scope = "warmed isolated WPF test process, collectors disabled, no plugin worker; not an installed-app benchmark" }));
                    Assert.False(island.IsEditMode);
                }
                finally { island.CloseFromController(); if (settings.IsVisible) settings.Close(); if (plugins.IsVisible) plugins.Close(); if (hover.IsVisible) hover.Close(); }
            });
        }
        finally { manager.Dispose(); if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
    private sealed class SilentClipboard : IIslandClipboardListener { public void SetEnabled(bool enabled) { } public void Dispose() { } }
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false; timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
}
