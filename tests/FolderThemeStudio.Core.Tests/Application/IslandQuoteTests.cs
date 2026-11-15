using FolderThemeStudio.App.DynamicIsland;
using Xunit;
using FolderThemeStudio.Core.Tests.TestSupport;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandQuoteTests
{
    [Fact]
    public async Task DisablingQuotesCancelsPendingRequestAndRejectsLateResponse()
    {
        var source = new PendingQuotes();
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false, ShowMusic = false }, quotes: source);
        var pending = model.RefreshQuoteAsync();
        model.ApplySettings(model.Settings with { ShowQuotes = false });
        Assert.True(source.Token.IsCancellationRequested);
        source.Result.SetResult(new("不应出现", "", "", ""));
        await pending;
        Assert.DoesNotContain("不应出现", model.CompactText);
    }

    [Fact]
    public void SingleLineQuoteAndClipboardRenderWithinMaximumWidth()
    {
        WpfTestHost.Invoke(() =>
        {
            var settings = new IslandSettings { ShowTime = false, ShowCpu = false, ShowMemory = false, ShowMusic = false, ShowClipboard = true, StretchMotion = false, ImageBackground = false };
            using var model = new IslandViewModel(settings, quotes: new Quotes());
            model.RefreshQuoteAsync().GetAwaiter().GetResult();
            var window = new DynamicIslandWindow(settings, model, (_, _) => new ClipboardStub());
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.InRange(window.ActualHeight, 36, 37);
                Assert.InRange(window.Width, IslandWidthSpring.Minimum, IslandWidthSpring.Maximum);
                Assert.Equal(0, ((SolidColorBrush)((System.Windows.Controls.Border)window.FindName("IslandSurface")).Background).Color.A);
                Capture(window, "quote");
                model.SetClipboard(new string('长', 100)); window.UpdateLayout();
                Assert.InRange(window.Width, 300, IslandWidthSpring.Maximum);
                Assert.InRange(window.ActualHeight, 36, 37);
                Capture(window, "clipboard");
                window.ApplySettings(settings with { ImageBackground = false });
                Assert.Equal(0, ((SolidColorBrush)((System.Windows.Controls.Border)window.FindName("IslandSurface")).Background).Color.A);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void RestoringAdaptiveWindowPreservesItsHorizontalCenter()
    {
        WpfTestHost.Invoke(() =>
        {
            var settings = new IslandSettings { Left = 300, Top = 30, WindowWidth = 300, ShowCpu = false, ShowMemory = false, ShowMusic = false, StretchMotion = false };
            IslandSettings saved;
            using (var model = new IslandViewModel(settings, quotes: new Quotes()))
            {
                model.RefreshQuoteAsync().GetAwaiter().GetResult();
                var first = new DynamicIslandWindow(settings, model, (_, _) => new ClipboardStub());
                try { first.Show(); first.UpdateLayout(); saved = first.CaptureSettings(); }
                finally { first.Close(); }
            }
            using var restoredModel = new IslandViewModel(saved, quotes: new Quotes());
            restoredModel.RefreshQuoteAsync().GetAwaiter().GetResult();
            var restored = new DynamicIslandWindow(saved, restoredModel, (_, _) => new ClipboardStub());
            try
            {
                restored.Show(); restored.UpdateLayout();
                Assert.Equal(saved.Left!.Value + saved.WindowWidth / 2, restored.Left + restored.Width / 2, 1);
            }
            finally { restored.Close(); }
        });
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("FTS_CAPTURE_UI") != "1") return;
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.Width * 3), 126, 288, 288, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ui-beta19"));
        Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, $"island-{name}.png")); encoder.Save(file);
    }

    private sealed class ClipboardStub : IIslandClipboardListener { public void SetEnabled(bool enabled) { } public void Dispose() { } }
    private sealed class PendingQuotes : IIslandQuoteSource
    {
        public CancellationToken Token;
        public TaskCompletionSource<IslandQuote?> Result = new();
        public Task<IslandQuote?> ReadAsync(CancellationToken token) { Token = token; return Result.Task; }
    }
    [Fact]
    public async Task ClipboardExpiresAtTenSecondsAndQuotesRefreshAtThirtySeconds()
    {
        var now = DateTimeOffset.UtcNow;
        var quotes = new Quotes();
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false, ShowMusic = false, ShowClipboard = true }, utcNow: () => now, quotes: quotes);
        await model.RefreshQuoteAsync();
        Assert.Contains("金句1", model.CompactText);
        model.SetClipboard("刚复制的内容");
        now = now.AddSeconds(9.9); model.TickClock();
        Assert.Contains("刚复制", model.CompactText);
        now = now.AddSeconds(.1); model.TickClock();
        Assert.Contains("金句1", model.CompactText);
        await model.RefreshQuoteAsync(); Assert.Equal(1, quotes.Reads);
        now = now.AddSeconds(20); await model.RefreshQuoteAsync();
        Assert.Contains("金句2", model.CompactText);
        model.ApplySettings(model.Settings with { AutoRefreshQuotes = false });
        now = now.AddMinutes(1); await model.RefreshQuoteAsync(); Assert.Equal(2, quotes.Reads);
        model.ApplySettings(model.Settings with { ShowQuotes = false });
        await model.RefreshQuoteAsync(); Assert.Equal(2, quotes.Reads);
    }

    [Fact]
    public void SpringRemainsBoundedAndPreservesVelocityWhenRetargeted()
    {
        var spring = new IslandWidthSpring(160);
        spring.Target = IslandWidthSpring.Maximum;
        spring.Step(1d / 60);
        var speed = spring.Velocity;
        spring.Target = 180;
        Assert.Equal(speed, spring.Velocity);
        for (var i = 0; i < 240; i++) { spring.Step(1d / 60); Assert.InRange(spring.Value, IslandWidthSpring.Minimum, IslandWidthSpring.Maximum); }
        Assert.True(spring.IsResting);
        Assert.Equal(180, spring.Value, 2);
    }

    private sealed class Quotes : IIslandQuoteSource
    {
        public int Reads;
        public Task<IslandQuote?> ReadAsync(CancellationToken token) => Task.FromResult<IslandQuote?>(new($"金句{++Reads}", "来源", "作者", ""));
    }
}
