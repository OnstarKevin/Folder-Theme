using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

/// <summary>Routes messages only to disposable test HWNDs; never moves the cursor or injects system input.</summary>
public sealed class IslandNativeRoutingTests
{
    [Fact]
    public async Task CrossProcessNativeRoutingPassesClickDoubleClickWheel_AndEditingReclaimsHitTest()
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-File",
                     Path.Combine(AppContext.BaseDirectory, "TestSupport", "IslandInputReceiver.ps1") }) start.ArgumentList.Add(argument);
        using var receiver = Process.Start(start)!;
        nint receiverHandle = 0;
        try
        {
            var ready = await receiver.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.StartsWith("READY ", ready);
            receiverHandle = new nint(long.Parse(ready![6..]));
            GetWindowThreadProcessId(receiverHandle, out var process);
            Assert.Equal(receiver.Id, (int)process);
            GetWindowRect(receiverHandle, out var bounds);
            WpfTestHost.Invoke(() =>
            {
                var settings = new IslandSettings { ShowCpu = false, ShowMemory = false, ShowMusic = false, ShowQuotes = false,
                    ShowTime = false, StretchMotion = false, HoverDetails = false, Left = bounds.Left + 16, Top = bounds.Top + 16 };
                var island = new DynamicIslandWindow(settings, clipboardFactory: (_, _) => new SilentClipboard());
                try
                {
                    island.Show(); island.UpdateLayout();
                    island.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    // Receiver coordinates are physical pixels; WPF uses DIP. Query
                    // on this DPI-aware UI thread, then use its screen transform.
                    GetWindowRect(receiverHandle, out bounds);
                    var target = island.PointFromScreen(new Point(bounds.Left + 32, bounds.Top + 32));
                    island.Left += target.X; island.Top += target.Y;
                    island.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var handle = new WindowInteropHelper(island).Handle;
                    // Empty padding must also become draggable while editing a layered window.
                    var point = island.PointToScreen(new Point(island.ActualWidth - 8, 13));
                    var nativePoint = new NativePoint { X = (int)point.X, Y = (int)point.Y };
                    var routed = GetAncestor(WindowFromPoint(nativePoint), 2);
                    GetWindowThreadProcessId(routed, out var routedProcess);
                    Assert.True(receiverHandle == routed,
                        $"Expected receiver {receiverHandle}, got HWND {routed} PID {routedProcess} at {point}; receiver bounds {bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}.");
                    // Explicitly deliver only to the HWND selected by Windows' native hit test.
                    foreach (var message in new[] { 0x201, 0x203, 0x20A })
                    {
                        SendMessage(routed, 0x8001, new nint(message), 0);
                        SendMessage(routed, message, message == 0x20A ? new nint(120 << 16) : new nint(1), new nint(0x00110013));
                        Assert.Equal("INPUT " + message, receiver.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult());
                        if (message != 0x20A) SendMessage(routed, 0x202, 0, new nint(0x00110013));
                    }
                    island.SetEditMode(true);
                    island.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    WaitForNativeRoute(nativePoint, handle);
                    var editedRoute = GetAncestor(WindowFromPoint(nativePoint), 2);
                    GetWindowRect(handle, out var editBounds);
                    Assert.True(handle == editedRoute, $"Edit expected {handle}, actual {editedRoute}, receiver {receiverHandle}; point {point}; island rect {editBounds.Left},{editBounds.Top},{editBounds.Right},{editBounds.Bottom}; pixel alpha fill {((System.Windows.Media.SolidColorBrush)((System.Windows.Controls.Border)island.FindName("IslandSurface")).Background).Color.A}");
                    island.SetEditMode(false);
                    island.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Assert.Equal(receiverHandle, GetAncestor(WindowFromPoint(nativePoint), 2));

                    var native = typeof(DynamicIslandWindow).GetField("nativeInteraction", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(island)!;
                    var unobscured = native.GetType().GetMethod("IsUnobscuredAt");
                    Assert.NotNull(unobscured);
                    Assert.True((bool)unobscured.Invoke(native, [point])!);
                    island.ApplySettings(settings with { HoverDetails = true });
                    var observe = typeof(DynamicIslandWindow).GetMethod("ObserveMouse", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var dwell = (System.Windows.Threading.DispatcherTimer)typeof(DynamicIslandWindow).GetField("hoverDwell", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(island)!;
                    observe.Invoke(island, [point]);
                    Assert.True(dwell.IsEnabled);
                    // Raise our owned receiver over the Island to represent another covering topmost window.
                    SetWindowPos(receiverHandle, new nint(-1), 0, 0, 0, 0, 0x13);
                    Assert.False((bool)unobscured.Invoke(native, [point])!);
                    observe.Invoke(island, [point]);
                    Assert.False(dwell.IsEnabled);
                }
                finally { island.CloseFromController(); }
            });
        }
        finally
        {
            if (receiverHandle != 0) PostMessage(receiverHandle, 0x10, 0, 0);
            if (!receiver.WaitForExit(3000)) receiver.Kill(entireProcessTree: true);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    private static void WaitForNativeRoute(NativePoint point, nint expected)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var until = Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            if (GetAncestor(WindowFromPoint(point), 2) == expected || until.Elapsed > TimeSpan.FromSeconds(1)) frame.Continue = false;
        };
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, int flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    private sealed class SilentClipboard : IIslandClipboardListener { public void SetEnabled(bool enabled) { } public void Dispose() { } }
}
