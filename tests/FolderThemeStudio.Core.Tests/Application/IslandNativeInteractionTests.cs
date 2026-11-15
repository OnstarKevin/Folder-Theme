using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandNativeInteractionTests
{
    [Fact]
    public void NewSettingsEnableHoverPassThroughAndOriginalImageBackground()
    {
        var settings = new IslandSettings();
        Assert.True(settings.ImageBackground);
        Assert.True((bool)typeof(IslandSettings).GetProperty("ClickThrough")!.GetValue(settings)!);
        Assert.True((bool)typeof(IslandSettings).GetProperty("HoverDetails")!.GetValue(settings)!);
    }

    [Fact]
    public void NormalWindowIsTransparentAndNativeClickThrough_EditRestoresInput_ClosingReleasesObserver()
    {
        WpfTestHost.Invoke(() =>
        {
            var settings = new IslandSettings { ShowCpu = false, ShowMemory = false, ShowMusic = false, ImageBackground = false };
            var window = new DynamicIslandWindow(settings, clipboardFactory: (_, _) => new SilentClipboard());
            try
            {
                window.Show();
                var handle = new WindowInteropHelper(window).Handle;
                Assert.Equal(0x08000020L, GetWindowLongPtr(handle, -20).ToInt64() & 0x08000020L);
                Assert.InRange(window.Height, 36, 37);
                Assert.InRange(window.Width, 1, 302.3623);
                var surface = (Border)window.FindName("IslandSurface");
                Assert.Equal(0, ((SolidColorBrush)surface.Background).Color.A);
                Assert.Equal(0, surface.BorderThickness.Left);
                Assert.Null(((TextBlock)window.FindName("CompactMessage")).ToolTip);
                var edit = typeof(DynamicIslandWindow).GetMethod("SetEditMode");
                Assert.NotNull(edit);
                edit.Invoke(window, [true]);
                Assert.Equal(0, GetWindowLongPtr(handle, -20).ToInt64() & 0x20);
                edit.Invoke(window, [false]);
                Assert.Equal(0x20, GetWindowLongPtr(handle, -20).ToInt64() & 0x20);
                var configurable = settings with { };
                typeof(IslandSettings).GetProperty("ClickThrough")!.SetValue(configurable, false);
                typeof(IslandSettings).GetProperty("HoverDetails")!.SetValue(configurable, false);
                window.ApplySettings(configurable);
                Assert.Equal(0, GetWindowLongPtr(handle, -20).ToInt64() & 0x20);
                var observer = typeof(DynamicIslandWindow).GetField("nativeInteraction", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                Assert.False((bool)observer.GetType().GetProperty("IsObserving")!.GetValue(observer)!);
                window.ApplySettings(settings);
                Assert.True((bool)observer.GetType().GetProperty("IsObserving")!.GetValue(observer)!);
            }
            finally { window.CloseFromController(); }
            var native = typeof(DynamicIslandWindow).GetField("nativeInteraction", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            Assert.False((bool)native.GetType().GetProperty("IsObserving")!.GetValue(native)!);
            foreach (var name in new[] { "hoverDwell", "hoverDismiss", "editTimeout" })
            {
                var timer = (System.Windows.Threading.DispatcherTimer)typeof(DynamicIslandWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                Assert.False(timer.IsEnabled);
            }
        });
    }

    [Fact]
    public void HoverCardBoundsLongContentAndNeverAcceptsInput()
    {
        WpfTestHost.Invoke(() =>
        {
            var type = typeof(DynamicIslandWindow).Assembly.GetType("FolderThemeStudio.App.DynamicIsland.IslandHoverWindow");
            Assert.NotNull(type);
            var card = (System.Windows.Window)Activator.CreateInstance(type!)!;
            try
            {
                card.DataContext = new { CompactText = new string('标', 600), CompactDetails = string.Join('\n', Enumerable.Repeat(new string('摘', 600), 30)) };
                card.Show(); card.UpdateLayout();
                Assert.InRange(card.ActualWidth, 1, 328);
                Assert.InRange(card.ActualHeight, 1, 200);
                Assert.Equal(0x08000000L, GetWindowLongPtr(new WindowInteropHelper(card).Handle, -20).ToInt64() & 0x08000020L);
            }
            finally { card.Close(); }
        });
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    private sealed class SilentClipboard : IIslandClipboardListener
    {
        public void SetEnabled(bool enabled) { }
        public void Dispose() { }
    }
}
