using System.IO;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandBackground25Tests
{
    [Fact]
    public void ImportedImageSurvivesSourceDeletionAndCorruptionFallsBack()
    {
        using var temp = new TemporaryDirectory();
        var source = Path.Combine(temp.Path, "source.png");
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(2400, 300, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[2400 * 300 * 4], 2400 * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var output = File.Create(source)) encoder.Save(output);
        var store = new IslandBackgroundStore(Path.Combine(temp.Path, "managed"));
        var imported = store.Import(source); File.Delete(source);
        var loaded = store.Resolve(imported);
        Assert.NotNull(loaded); Assert.InRange(loaded.PixelWidth, 1, 1600); Assert.InRange(loaded.PixelHeight, 1, 1600);
        File.WriteAllText(imported, "corrupt"); Assert.Null(store.Resolve(imported));
        Assert.ThrowsAny<Exception>(() => store.Import(imported));
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "managed"), ".import-*"));
    }
    [Fact]
    public void BackgroundCacheIsBoundedAndKeepsPersistedSelection()
    {
        using var temp = new TemporaryDirectory();
        var root = Path.Combine(temp.Path, "managed"); var store = new IslandBackgroundStore(root);
        var source = Path.Combine(temp.Path, "source.png"); string? saved = null;
        for (var i = 0; i < 15; i++)
        {
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { (byte)i, 100, 200, 255 }, 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var output = File.Create(source)) encoder.Save(output);
            var imported = store.Import(source, saved); saved ??= imported;
        }
        Assert.NotNull(store.Resolve(saved)); Assert.InRange(Directory.GetFiles(root, "*.png").Length, 1, 12);
    }

    [Fact]
    public void GradientDirectionAndStrengthHaveDistinctEndpoints()
    {
        var mask = Assert.IsType<System.Windows.Media.LinearGradientBrush>(IslandSurfaceAppearance.CreateFadeMask(new() { BackgroundFadeDirection = "Left", BackgroundFadeProfile = "Linear", BackgroundFadeStrength = .5 }));
        Assert.Equal(1, mask.StartPoint.X); Assert.Equal(0, mask.EndPoint.X);
        Assert.Equal(255, mask.GradientStops[0].Color.A); Assert.InRange(mask.GradientStops[^1].Color.A, 127, 128);
        Assert.IsType<System.Windows.Media.RadialGradientBrush>(IslandSurfaceAppearance.CreateFadeMask(new() { BackgroundFadeDirection = "Edges" }));
    }

    [Fact]
    public void MusicHeightSpringConvergesAndReturnsWithoutOvershoot()
    {
        var spring = new IslandWidthSpring(36.4, 36.4, 42) { Target = 42 };
        for (var i = 0; i < 120; i++) { spring.Step(1d / 60); Assert.InRange(spring.Value, 36.4, 42); }
        Assert.True(spring.IsResting); Assert.Equal(42, spring.Value);
        spring.Target = 36.4;
        for (var i = 0; i < 120; i++) spring.Step(1d / 60);
        Assert.Equal(36.4, spring.Value);
    }

    [Fact]
    public async Task MetricsRemainAvailableWithQuoteAndMediaCommandsReachTheSource()
    {
        var media = new FakeMedia();
        using var model = new IslandViewModel(new() { ShowQuotes = true, ShowMusic = true, ShowCpu = true, ShowMemory = true, ShowLyrics = false },
            new FakeMetrics(), media, new FakeLyrics(), new FakeNotifications(), quotes: new FakeQuotes());
        await model.RefreshQuoteAsync(); await model.RefreshAsync();
        Assert.Contains("CPU 24%", model.MetricsText); Assert.Contains("内存 48%", model.MetricsText);
        Assert.True(model.IsMusicPlaying); Assert.True(model.HasMediaSession); Assert.Equal(.25, model.MusicProgress);
        await model.ControlMediaAsync(IslandMediaCommand.Next); Assert.Equal(IslandMediaCommand.Next, media.Command);
        await model.ControlMediaAsync(IslandMediaCommand.Previous); Assert.Equal(IslandMediaCommand.Previous, media.Command);
        await model.ControlMediaAsync(IslandMediaCommand.TogglePlayPause); Assert.False(model.IsMusicPlaying); Assert.True(model.HasMediaSession);
        model.ApplySettings(model.Settings with { ShowCpu = false, ShowMemory = false, ShowMusic = false });
        Assert.Empty(model.MetricsText); Assert.False(model.HasMediaSession);
    }

    private sealed class FakeMetrics : ISystemMetricsSource { public SystemMetrics Read() => new(24, 48, 8UL * 1073741824, 16UL * 1073741824); }
    private sealed class FakeMedia : IIslandMediaSource, IIslandMediaControls
    {
        private bool playing = true;
        public bool FailCommand;
        public IslandMediaCommand? Command;
        public Task<IslandMedia?> ReadAsync(CancellationToken token) => Task.FromResult<IslandMedia?>(new("歌曲", "歌手", TimeSpan.FromSeconds(15), playing) { Duration = TimeSpan.FromSeconds(60) });
        public Task<bool> ExecuteAsync(IslandMediaCommand command, CancellationToken token) { if (FailCommand) throw new IOException("模拟播放器失败"); Command = command; if (command == IslandMediaCommand.TogglePlayPause) playing = !playing; return Task.FromResult(true); }
    }
    private sealed class FakeLyrics : IIslandLyricsSource { public Task<IReadOnlyList<LyricLine>> ReadAsync(string title, string artist, string directory, CancellationToken token) => Task.FromResult<IReadOnlyList<LyricLine>>([]); }
    private sealed class FakeNotifications : IIslandNotificationSource
    {
        public string Status => "";
        public Task<string> RequestAccessAsync() => Task.FromResult("");
        public Task<IslandNotification?> ReadLatestAsync(CancellationToken token) => Task.FromResult<IslandNotification?>(null);
        public void Dispose() { }
    }
    private sealed class FakeQuotes : IIslandQuoteSource { public Task<IslandQuote?> ReadAsync(CancellationToken token) => Task.FromResult<IslandQuote?>(new("一句金句", "", "", "1")); }
    [Fact]
    public async Task FailedMediaCommandFeedbackExpiresAfterFourSeconds()
    {
        var now = DateTimeOffset.UtcNow;
        using var model = new IslandViewModel(new() { ShowQuotes = false, ShowCpu = false, ShowMemory = false, ShowLyrics = false },
            new FakeMetrics(), new FakeMedia { FailCommand = true }, new FakeLyrics(), new FakeNotifications(), () => now, new FakeQuotes());
        await model.RefreshAsync(); await model.ControlMediaAsync(IslandMediaCommand.Next);
        Assert.NotEmpty(model.StatusText);
        now = now.AddSeconds(5); model.TickClock(); Assert.Empty(model.StatusText);
    }
    [Fact]
    public async Task BackgroundSettingsRoundTripAndNormalizeWithoutDiscardingPrivatePath()
    {
        using var temp = new TemporaryDirectory();
        var store = new IslandSettingsStore(Path.Combine(temp.Path, "island.json"));
        await store.SaveAsync(new IslandSettings { BackgroundImagePath = "local.png", BackgroundOpacity = 2,
            BackgroundLighten = double.NaN, BackgroundFadeStrength = -.2, BackgroundFadeStart = 2,
            BackgroundFadeDirection = "Left", BackgroundFadeProfile = "Linear", FlowBorder = false });
        var saved = await store.LoadAsync();
        Assert.Equal("local.png", saved.BackgroundImagePath);
        Assert.Equal(1, saved.BackgroundOpacity); Assert.True(double.IsFinite(saved.BackgroundLighten));
        Assert.Equal(0, saved.BackgroundFadeStrength); Assert.InRange(saved.BackgroundFadeStart, 0, .95);
        Assert.Equal("Left", saved.BackgroundFadeDirection); Assert.Equal("Linear", saved.BackgroundFadeProfile);
        Assert.False(saved.FlowBorder);
    }
}
