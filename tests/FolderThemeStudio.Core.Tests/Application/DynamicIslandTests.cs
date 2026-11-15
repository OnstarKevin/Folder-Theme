using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FolderThemeStudio.App;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class DynamicIslandTests
{
    [Fact]
    public void ExitDuringPendingClosePreservesDisabledPreference()
    {
        var root = Path.Combine(Path.GetTempPath(), "fts-close-" + Guid.NewGuid().ToString("N"));
        try
        {
            WpfTestHost.Invoke(() =>
            {
                var store = new FirstPendingCloseStore(); DynamicIslandWindow? overlay = null;
                using var controller = new DynamicIslandController(store, () => false, _ => Task.CompletedTask, () => { },
                    settings => overlay = new DynamicIslandWindow(settings, Model(settings), (_, _) => new TestClipboard()),
                    () => new FolderThemeStudio.App.DynamicIsland.Plugins.IslandPluginManager(root));
                controller.ShowAsync().GetAwaiter().GetResult();
                var menu = ((Border)overlay!.FindName("IslandSurface")).ContextMenu;
                ((MenuItem)menu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.True(store.CloseStarted);
                controller.Dispose();
                Assert.False(store.Settings.Enabled);
                Assert.False(overlay.IsVisible);
                store.CompleteClose.SetResult();
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task DisabledLyrics_StopsLyricsReadsWhileMusicMetadataStillUpdates()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<IslandSettings>("{\"ShowLyrics\":false,\"ShowCpu\":false,\"ShowMemory\":false}")!;
        using var model = new IslandViewModel(settings, new TestMetrics(), new TestMedia(), new UnreadableLyrics(), new TestNotifications());
        await model.RefreshAsync();
        Assert.Equal("City Lights", model.MusicTitle);
        Assert.Empty(model.LyricText);
    }
    [Theory]
    [InlineData(100, 100, 100, 50)]
    [InlineData(0, 100, 200, 100)]
    [InlineData(300, 300, 0, 0)]
    public void Cpu_UsesDeltaAndSubtractsIdle(ulong idle, ulong kernel, ulong user, double expected)
    {
        Assert.Equal(expected, CpuUsageCalculator.Calculate(new(100, 200, 300), new(100 + idle, 200 + kernel, 300 + user)));
        Assert.Null(CpuUsageCalculator.Calculate(new(1, 1, 1), new(1, 1, 1)));
    }

    [Fact]
    public void NativeMetrics_ReturnsPhysicalMemoryAndFirstCpuSampleIsUnknown()
    {
        var metrics = new WindowsSystemMetricsSource().Read();
        Assert.True(metrics.TotalMemoryBytes > 0);
        Assert.InRange(metrics.MemoryPercent!.Value, 0, 100);
        Assert.Null(metrics.CpuPercent);
    }

    [Fact]
    public async Task NativeMedia_HandlesAnAbsentOrUnsupportedSessionWithoutCrashing()
    {
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        // 只验证 API 激活/降级，不输出用户的歌名或歌手。
        var song = await new WindowsMediaSource().ReadAsync(token.Token);
        Assert.True(song is null || song.Position >= TimeSpan.Zero);
    }

    [Fact]
    public void WheelAndPosition_AreClamped()
    {
        Assert.Equal(.2, IslandWindowPolicy.WheelOpacity(.2, -120));
        Assert.Equal(1, IslandWindowPolicy.WheelOpacity(1, 120));
        Assert.Equal(.55, IslandWindowPolicy.WheelOpacity(.5, 120), 5);
        Assert.Equal(new Point(0, 0), IslandWindowPolicy.ClampPosition(new(-9999, -9999), new(0, 0, 1920, 1080), new(370, 200)));
        Assert.Equal(new Point(1550, 880), IslandWindowPolicy.ClampPosition(new(9999, 9999), new(0, 0, 1920, 1080), new(370, 200)));
    }

    [Fact]
    public void Clipboard_TruncatesAt80TextElementsWithoutSplittingEmoji()
    {
        var preview = ClipboardPreview.Create(new string('中', 79) + "😀xyz");
        Assert.EndsWith("😀…", preview);
        Assert.Equal("a b", ClipboardPreview.Create(" a\nb "));
    }

    [Fact]
    public void Lyrics_ParsesMultipleTimestampsOffsetsAndCurrentLine()
    {
        var lines = LrcLyricsSource.Parse("[offset:100]\n[00:01.50][00:03]第一句\n[00:02.001]第二句\n[99:99]无效\n[ar:歌手]");
        Assert.Equal(3, lines.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1600), lines[0].At);
        Assert.Equal("第二句", LrcLyricsSource.CurrentLine(lines, TimeSpan.FromMilliseconds(2500)));
        Assert.Empty(LrcLyricsSource.CurrentLine(lines, TimeSpan.Zero));
    }

    [Fact]
    public void Lyrics_LargeIntegerTimestampsRemainWithinTimeSpanRange()
    {
        var lines = LrcLyricsSource.Parse("[2147483647:00]超出范围\n[00:01]正常");
        Assert.Equal(2, lines.Count);
        Assert.Equal("正常", lines[0].Text);
    }

    [Fact]
    public async Task UnreadableLyrics_DoNotBlockMusicMetadataOrNotifications()
    {
        var notifications = new TestNotifications();
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false, ShowNotifications = true },
            new TestMetrics(), new TestMedia(), new UnreadableLyrics(), notifications);
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await model.RefreshAsync();
        Assert.Contains(nameof(IslandViewModel.MusicTitle), changed);
        Assert.Equal("City Lights", model.MusicTitle);
        Assert.Equal(1, notifications.Reads);
        Assert.Equal("测试通知", model.NotificationTitle);
        Assert.Contains("读取", model.LyricText);
    }

    [Fact]
    public async Task Settings_RoundTripsAndNormalizesUntrustedValues()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "island.json");
        var store = new IslandSettingsStore(path);
        await store.SaveAsync(new() { Enabled = true, Opacity = 0, RefreshSeconds = 999, Left = double.NaN, Top = 30 });
        var loaded = await store.LoadAsync();
        Assert.True(loaded.Enabled);
        Assert.Equal(.2, loaded.Opacity);
        Assert.Equal(60, loaded.RefreshSeconds);
        Assert.Null(loaded.Left);
        Assert.Equal(30, loaded.Top);
        await File.WriteAllTextAsync(path, "broken json");
        Assert.False((await store.LoadAsync()).Enabled);
    }

    [Fact]
    public void Overlay_HasRequestedWindowStyleAndDisposesClipboardListener()
    {
        WpfTestHost.Invoke(() =>
        {
            var settings = new IslandSettings { ShowClipboard = true, ShowNotifications = true };
            var clipboard = new TestClipboard();
            var model = Model(settings);
            var window = new DynamicIslandWindow(settings, model, (_, changed) => { clipboard.Changed = changed; return clipboard; });
            try
            {
                window.Show();
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.True(window.Topmost);
                Assert.True(window.AllowsTransparency);
                Assert.False(window.ShowInTaskbar);
                Assert.True(clipboard.Enabled);
                PumpUntil(() => model.MusicTitle == "City Lights");
                window.UpdateLayout();
                Assert.Equal("同步歌词示例", model.LyricText);
                Assert.Equal("设计资料已整理完成", model.ClipboardText);
                Assert.Equal("测试通知", model.NotificationTitle);
                Assert.InRange(window.ActualWidth, IslandWidthSpring.Minimum, IslandWidthSpring.Maximum);
                Assert.InRange(window.ActualHeight, 36, 37);
                var message = (TextBlock)window.FindName("CompactMessage");
                Assert.Equal(TextWrapping.NoWrap, message.TextWrapping);
                Assert.Equal(TextTrimming.CharacterEllipsis, message.TextTrimming);
                model.SetClipboard(new string('长', 2000) + "\n换行消息");
                window.UpdateLayout();
                Assert.InRange(window.ActualHeight, 36, 37);
                Assert.DoesNotContain('\n', model.CompactText);
                Capture(window);
                window.ApplySettings(settings with { ShowClipboard = false });
                Assert.False(clipboard.Enabled);
                Assert.Empty(model.ClipboardText);
            }
            finally { window.Close(); }
            Assert.True(clipboard.Disposed);
        });
    }

    [Fact]
    public void Controller_ReusesOneWindowAndPreservesEnabledStateOnApplicationExit()
    {
        WpfTestHost.Invoke(() =>
        {
            var store = new MemoryStore();
            var creations = 0;
            DynamicIslandWindow? overlay = null;
            var controller = new DynamicIslandController(store, () => false, _ => Task.CompletedTask, () => { }, settings =>
            {
                creations++;
                return overlay = new DynamicIslandWindow(settings, Model(settings), (_, _) => new TestClipboard());
            });
            controller.ShowAsync().GetAwaiter().GetResult();
            controller.ShowAsync().GetAwaiter().GetResult();
            Assert.Equal(1, creations);
            overlay!.Opacity = .65;
            controller.Dispose();
            Assert.True(store.Settings.Enabled);
            Assert.Equal(.65, store.Settings.Opacity);
            Assert.False(overlay.IsVisible);
        });
    }

    [Fact]
    public void Controller_CloseMenuDisablesAutomaticRestore()
    {
        WpfTestHost.Invoke(() =>
        {
            var store = new MemoryStore();
            DynamicIslandWindow? overlay = null;
            using var controller = new DynamicIslandController(store, () => false, _ => Task.CompletedTask, () => { }, settings =>
                overlay = new DynamicIslandWindow(settings, Model(settings), (_, _) => new TestClipboard()));
            controller.ShowAsync().GetAwaiter().GetResult();
            var surface = (Border)overlay!.FindName("IslandSurface");
            ((MenuItem)surface.ContextMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(store.Settings.Enabled);
            Assert.False(overlay.IsVisible);
        });
    }

    [Fact]
    public void Controller_SettingsQueuedDuringCloseCannotReenableIsland()
    {
        WpfTestHost.Invoke(() =>
        {
            // Test host 不运行 Application.Run；显式安装 UI 同步上下文以模拟真实消息循环。
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try
            {
            var store = new DelayedCloseStore();
            DynamicIslandWindow? overlay = null;
            using var controller = new DynamicIslandController(store, () => false, _ => Task.CompletedTask, () => { }, settings =>
                overlay = new DynamicIslandWindow(settings, Model(settings), (_, _) => new TestClipboard()));
            controller.ShowAsync().GetAwaiter().GetResult();
            var menu = ((Border)overlay!.FindName("IslandSurface")).ContextMenu;
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var panel = System.Windows.Application.Current.Windows.OfType<DynamicIslandSettingsWindow>().Single();
            ((MenuItem)menu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(store.CloseStarted);
            overlay.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent });
            var debounceDue = DateTime.UtcNow.AddMilliseconds(650);
            PumpUntil(() => DateTime.UtcNow >= debounceDue);
            ((Button)panel.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            store.CompleteClose.SetResult();
            PumpUntil(() => !overlay.IsVisible && ((Button)panel.FindName("SaveButton")).IsEnabled);
            Assert.False(store.Settings.Enabled);
            Assert.Equal(1, store.EnabledWrites);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        });
    }

    [Fact]
    public async Task DisabledModules_DoNotReadDataSources()
    {
        var metrics = new TestMetrics();
        var media = new TestMedia();
        var notifications = new TestNotifications();
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false, ShowMusic = false }, metrics, media, new TestLyrics(), notifications);
        await model.RefreshAsync();
        Assert.Equal(0, metrics.Reads);
        Assert.Equal(0, media.Reads);
        Assert.Equal(0, notifications.Reads);
    }

    [Fact]
    public void Home_ProvidesBuiltInDynamicIslandEntry()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = new MainWindow(new MainViewModelFixture().CreateViewModel());
            try { Assert.IsType<Button>(window.FindName("DynamicIslandButton")); }
            finally { window.RequestExplicitExit(); }
        });
    }

    [Fact]
    public void Controller_MasterSwitchStopsWindowAndClipboardRetainingModulePreferences()
    {
        WpfTestHost.Invoke(() =>
        {
            var store = new MemoryStore { Settings = new() { Enabled = true, ShowClipboard = true, ShowMusic = false, ShowLyrics = false } };
            var created = new List<(DynamicIslandWindow Window, TestClipboard Clipboard)>();
            using var controller = new DynamicIslandController(store, () => false, _ => Task.CompletedTask, () => { }, settings =>
            {
                var clipboard = new TestClipboard();
                var window = new DynamicIslandWindow(settings, Model(settings), (_, _) => clipboard);
                created.Add((window, clipboard));
                return window;
            });
            controller.RestoreAsync().GetAwaiter().GetResult();
            Assert.True(created[0].Window.IsVisible);
            controller.SetFeatureEnabledAsync(false).GetAwaiter().GetResult();
            Assert.False(created[0].Window.IsVisible);
            Assert.True(created[0].Clipboard.Disposed);
            controller.ShowAsync().GetAwaiter().GetResult();
            Assert.Single(created);
            Assert.True(store.Settings.Enabled);
            Assert.False(store.Settings.ShowMusic);
            Assert.False(store.Settings.ShowLyrics);
            controller.SetFeatureEnabledAsync(true).GetAwaiter().GetResult();
            Assert.Equal(2, created.Count);
            Assert.True(created[1].Window.IsVisible);
        });
    }
    [Fact]
    public async Task DisabledCpuModule_RequestsOnlyMemorySampling()
    {
        var metrics = new SelectiveMetrics();
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = true, ShowMusic = false }, metrics, new TestMedia(), new TestLyrics(), new TestNotifications());
        await model.RefreshAsync();
        Assert.Equal((false, true), metrics.Requested);
    }

    private sealed class SelectiveMetrics : ISystemMetricsSource
    {
        internal (bool Cpu, bool Memory)? Requested;
        public SystemMetrics Read() => new(null, null, 0, 0);
        public SystemMetrics Read(bool cpu, bool memory) { Requested = (cpu, memory); return new(null, null, 0, 0); }
    }
    private static IslandViewModel Model(IslandSettings settings) => new(settings, new TestMetrics(), new TestMedia(), new TestLyrics(), new TestNotifications());
    private static void PumpUntil(Func<bool> done)
    {
        var frame = new DispatcherFrame();
        var end = DateTime.UtcNow.AddSeconds(4);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow > end) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Assert.True(done());
    }
    private static void Capture(Window window)
    {
        if (Environment.GetEnvironmentVariable("FTS_CAPTURE_UI") != "1") return;
        var root = VisualTreeHelper.GetParent((FrameworkElement)window.Content) as FrameworkElement ?? (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-beta18"));
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, "dynamic-island.png")); encoder.Save(stream);
    }
    private sealed class TestMetrics : ISystemMetricsSource { internal int Reads; public SystemMetrics Read() { Reads++; return new(24, 48, 8UL * 1073741824, 16UL * 1073741824); } }
    private sealed class TestMedia : IIslandMediaSource { internal int Reads; public Task<IslandMedia?> ReadAsync(CancellationToken token) { Reads++; return Task.FromResult<IslandMedia?>(new("City Lights", "示例歌手", TimeSpan.FromSeconds(12), true)); } }
    private sealed class TestLyrics : IIslandLyricsSource { public Task<IReadOnlyList<LyricLine>> ReadAsync(string title, string artist, string directory, CancellationToken token) => Task.FromResult<IReadOnlyList<LyricLine>>([new(TimeSpan.Zero, "同步歌词示例")]); }
    private sealed class UnreadableLyrics : IIslandLyricsSource { public Task<IReadOnlyList<LyricLine>> ReadAsync(string title, string artist, string directory, CancellationToken token) => throw new IOException("模拟歌词被占用"); }
    private sealed class TestNotifications : IIslandNotificationSource
    {
        internal int Reads;
        public string Status => "测试已授权";
        public Task<string> RequestAccessAsync() => Task.FromResult(Status);
        public Task<IslandNotification?> ReadLatestAsync(CancellationToken token) { Reads++; return Task.FromResult<IslandNotification?>(new("测试通知", "提醒：下午整理设计稿。")); }
        public void Dispose() { }
    }
    private sealed class TestClipboard : IIslandClipboardListener
    {
        internal Action<string>? Changed;
        internal bool Enabled, Disposed;
        public void SetEnabled(bool value) { Enabled = value; Changed?.Invoke(value ? "设计资料已整理完成" : string.Empty); }
        public void Dispose() => Disposed = true;
    }
    private sealed class MemoryStore : IIslandSettingsStore
    {
        internal IslandSettings Settings = new();
        public Task<IslandSettings> LoadAsync() => Task.FromResult(Settings);
        public Task SaveAsync(IslandSettings settings) { Settings = settings; return Task.CompletedTask; }
    }
    private sealed class FirstPendingCloseStore : IIslandSettingsStore
    {
        internal IslandSettings Settings = new(); internal bool CloseStarted;
        internal TaskCompletionSource CompleteClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IslandSettings> LoadAsync() => Task.FromResult(Settings);
        public async Task SaveAsync(IslandSettings settings)
        {
            if (!settings.Enabled && !CloseStarted) { CloseStarted = true; await CompleteClose.Task.ConfigureAwait(false); }
            Settings = settings;
        }
    }
    private sealed class DelayedCloseStore : IIslandSettingsStore
    {
        internal IslandSettings Settings = new();
        internal bool CloseStarted;
        internal int EnabledWrites;
        internal TaskCompletionSource CompleteClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IslandSettings> LoadAsync() => Task.FromResult(Settings);
        public async Task SaveAsync(IslandSettings settings)
        {
            if (!settings.Enabled && !CompleteClose.Task.IsCompleted) { CloseStarted = true; await CompleteClose.Task.ConfigureAwait(false); }
            if (settings.Enabled) EnabledWrites++;
            Settings = settings;
        }
    }
}



