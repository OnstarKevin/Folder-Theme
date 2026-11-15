using FolderThemeStudio.App.DynamicIsland;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandPriorityTests
{
    [Fact]
    public async Task MessagesPreemptClipboardAndLyricsThenResumeTheCurrentActivity()
    {
        var now = DateTimeOffset.UtcNow;
        var media = new Media();
        var messages = new Messages();
        using var model = Create(media, messages, () => now);
        await model.RefreshQuoteAsync(); await model.RefreshAsync();
        Assert.Contains("第一句歌词", model.CompactText);
        model.SetClipboard("复制内容");
        Assert.Contains("复制内容", model.CompactText);
        messages.Latest = new("新消息", "朋友发来的内容") { Source = "QQ", CreatedAt = now };
        await model.RefreshAsync();
        Assert.Contains("QQ", model.CompactText);
        Assert.Contains("新消息", model.CompactText);
        now = now.AddSeconds(6); model.TickClock();
        Assert.Contains("复制内容", model.CompactText);
        now = now.AddSeconds(4); model.TickClock();
        Assert.Contains("第一句歌词", model.CompactText);
        media.Song = media.Song! with { Playing = false }; await model.RefreshAsync();
        Assert.Equal("兜底金句", model.CompactText);
    }

    [Fact]
    public async Task PlayingWithoutCurrentLyricsUsesTitleButPausedOrInvalidMediaDoesNotHideQuote()
    {
        var media = new Media { Song = new("歌曲名", "歌手", TimeSpan.Zero, true) };
        using var model = Create(media, new Messages(), () => DateTimeOffset.UtcNow);
        await model.RefreshQuoteAsync(); await model.RefreshAsync();
        Assert.Contains("歌曲名", model.CompactText);
        media.Song = media.Song with { Position = TimeSpan.FromSeconds(12) }; await model.RefreshAsync();
        Assert.Contains("第二句歌词", model.CompactText);
        model.ApplySettings(model.Settings with { ShowLyrics = false }); await model.RefreshAsync();
        Assert.Contains("歌曲名", model.CompactText);
        model.ApplySettings(model.Settings with { ShowMusic = false });
        Assert.Equal("兜底金句", model.CompactText);
        model.ApplySettings(model.Settings with { ShowMusic = true });
        media.Song = new("媒体不可用", "", TimeSpan.Zero, true, "Error"); await model.RefreshAsync();
        Assert.Equal("兜底金句", model.CompactText);
        media.Song = null; await model.RefreshAsync();
        Assert.Equal("兜底金句", model.CompactText);
    }

    [Fact]
    public async Task MessagesRefreshIndependentlyWhileMediaReadIsPending()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new Messages();
        var pending = new PendingMedia();
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false, ShowNotifications = true },
            media: pending, notifications: messages, quotes: new Quotes(), utcNow: () => now);
        var refresh = model.RefreshAsync();
        messages.Latest = new("即时消息", "内容") { Source = "QQ", CreatedAt = now };
        await model.RefreshNotificationsAsync();
        Assert.False(refresh.IsCompleted);
        Assert.Contains("即时消息", model.CompactText);
        pending.Result.SetResult(null); await refresh;
        model.ApplySettings(model.Settings with { ShowNotifications = false });
        var reads = messages.Reads;
        await model.RefreshNotificationsAsync(); Assert.Equal(reads, messages.Reads);
        Assert.DoesNotContain("即时消息", model.CompactText);
    }

    private static IslandViewModel Create(Media media, Messages messages, Func<DateTimeOffset> now) => new(
        new() { ShowCpu = false, ShowMemory = false, ShowClipboard = true, ShowNotifications = true },
        media: media, lyrics: new Lyrics(), notifications: messages, utcNow: now, quotes: new Quotes());
    private sealed class Media : IIslandMediaSource
    {
        public IslandMedia? Song = new("歌曲名", "歌手", TimeSpan.FromSeconds(5), true);
        public Task<IslandMedia?> ReadAsync(CancellationToken token) => Task.FromResult(Song);
    }
    private sealed class PendingMedia : IIslandMediaSource
    {
        public TaskCompletionSource<IslandMedia?> Result = new();
        public Task<IslandMedia?> ReadAsync(CancellationToken token) => Result.Task;
    }
    private sealed class Lyrics : IIslandLyricsSource
    {
        public Task<IReadOnlyList<LyricLine>> ReadAsync(string title, string artist, string directory, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<LyricLine>>([new(TimeSpan.FromSeconds(1), "第一句歌词"), new(TimeSpan.FromSeconds(10), "第二句歌词")]);
    }
    private sealed class Messages : IIslandNotificationSource
    {
        public IslandNotification? Latest;
        public int Reads;
        public string Status => "已连接";
        public Task<string> RequestAccessAsync() => Task.FromResult(Status);
        public Task<IslandNotification?> ReadLatestAsync(CancellationToken token) { Reads++; return Task.FromResult(Latest); }
        public void Dispose() { }
    }
    private sealed class Quotes : IIslandQuoteSource
    {
        public Task<IslandQuote?> ReadAsync(CancellationToken token) => Task.FromResult<IslandQuote?>(new("兜底金句", "", "", ""));
    }
}
