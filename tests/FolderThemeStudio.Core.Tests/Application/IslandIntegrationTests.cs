using System.Net;
using System.Net.Http;
using System.Text;
using FolderThemeStudio.App.DynamicIsland;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandIntegrationTests
{
    [Fact]
    public async Task LocalMessages_RequireTokenRejectBrowserOriginsAndReleasePort()
    {
        var secret = new string('A', 48);
        var bridge = new LocalNotificationBridge(0, secret);
        Assert.True(bridge.Start());
        var port = bridge.Port;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            var uri = $"http://127.0.0.1:{port}/api/notifications";
            using var rejected = await http.PostAsync(uri, new StringContent("{\"title\":\"test\"}"));
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            using var browserRequest = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent("{}") };
            browserRequest.Headers.TryAddWithoutValidation("Origin", "https://unrelated.example");
            browserRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + secret);
            using var browserReply = await http.SendAsync(browserRequest);
            Assert.Equal(HttpStatusCode.Forbidden, browserReply.StatusCode);
            using var acceptedRequest = new HttpRequestMessage(HttpMethod.Post, uri)
            { Content = new StringContent("{\"title\":\"收到消息\",\"body\":\"正文\"}", Encoding.UTF8, "application/json") };
            acceptedRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + secret);
            using var accepted = await http.SendAsync(acceptedRequest);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal("收到消息", bridge.ReadLatest()!.Title);
            Assert.Equal("正文", bridge.ReadLatest()!.Body);
            // 刷新途中保存设置可能放弃这一批；重读必须仍有消息，不能提前消费。
            Assert.Single(bridge.ReadRecent());
            Assert.Single(bridge.ReadRecent());
        }
        finally { bridge.Dispose(); }
        using var reopened = new LocalNotificationBridge(port, secret);
        Assert.True(reopened.Start());
        Assert.Null(reopened.ReadLatest());
    }

    [Fact]
    public async Task InvalidLyricsResponses_DoNotCrashOrBecomeLyrics()
    {
        var source = new OnlineLyricsSource(new HttpClient(new FakeHttp(_ => "not JSON")));
        Assert.Empty((await source.FetchAsync(new("Song", "Artist", TimeSpan.Zero, true), CancellationToken.None)).Lines);
    }

    [Fact]
    public async Task LyricsCoordinator_CancelsPreviousTrackAndDoesNotShowItsResult()
    {
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttp(request => request.RequestUri!.AbsolutePath.EndsWith("/lyric")
            ? "{\"data\":{\"lrc\":\"[00:01]second song line\"}}"
            : "{\"data\":[{\"song\":\"Second\",\"singer\":\"Artist\",\"id\":2}]}", release);
        using var coordinator = new LyricsCoordinator(new() { OnlineLyrics = true }, new OnlineLyricsSource(new HttpClient(handler)));
        await coordinator.ReadAsync(new("First", "Artist", TimeSpan.Zero, true), "", CancellationToken.None);
        for (var i = 0; i < 100 && handler.Requests.Count == 0; i++) await Task.Delay(10);
        var second = new IslandMedia("Second", "Artist", TimeSpan.Zero, true);
        await coordinator.ReadAsync(second, "", CancellationToken.None);
        for (var i = 0; i < 100 && handler.Cancelled == 0; i++) await Task.Delay(10);
        Assert.True(handler.Cancelled > 0);
        release.SetResult("release");
        IReadOnlyList<LyricLine> lines = [];
        for (var i = 0; i < 100 && lines.Count == 0; i++) { await Task.Delay(10); lines = await coordinator.ReadAsync(second, "", CancellationToken.None); }
        Assert.Equal("second song line", Assert.Single(lines).Text);
    }

    [Fact]
    public async Task DisabledMusic_DoesNotExecuteCommands()
    {
        var media = new ControlledMedia();
        using var model = new IslandViewModel(new() { ShowMusic = false, ShowCpu = false, ShowMemory = false },
            media: media, lyrics: new LrcLyricsSource());
        await model.ControlMediaAsync(IslandMediaCommand.Next);
        Assert.Equal(0, media.Commands);
        model.ApplySettings(model.Settings with { ShowMusic = true });
        await model.ControlMediaAsync(IslandMediaCommand.Next);
        Assert.Equal(1, media.Commands);
        Assert.Empty(model.StatusText);
    }
    [Fact]
    public async Task OnlineLyrics_FallsBackAndRejectsWrongSong()
    {
        var handler = new FakeHttp(request => request.RequestUri!.Host == "lrclib.net"
            ? "{\"trackName\":\"Test Song\",\"artistName\":\"Artist\",\"syncedLyrics\":\"[00:01]matching line\",\"duration\":180}"
            : "{\"data\":[{\"song\":\"Wrong Song\",\"singer\":\"Artist\",\"id\":1}]}" );
        var source = new OnlineLyricsSource(new HttpClient(handler));
        var result = await source.FetchAsync(new("Test Song", "Artist", TimeSpan.Zero, true) { Duration = TimeSpan.FromSeconds(180) }, CancellationToken.None);
        Assert.Equal("matching line", Assert.Single(result.Lines).Text);
        Assert.Equal("LRCLIB", result.Provider);
        Assert.DoesNotContain(handler.Requests, uri => uri.Contains("/lyric?"));
    }

    [Fact]
    public async Task OnlineLyrics_RejectsSameNameWrongDurationBeforeFetchingLyrics()
    {
        var handler = new FakeHttp(request => request.RequestUri!.Host == "lrclib.net"
            ? "{\"trackName\":\"Song\",\"artistName\":\"Artist\",\"duration\":180,\"syncedLyrics\":\"[00:01]correct\"}"
            : "{\"data\":[{\"song\":\"Song\",\"singer\":\"Artist\",\"id\":1,\"interval\":120}]}" );
        var result = await new OnlineLyricsSource(new HttpClient(handler)).FetchAsync(
            new("Song", "Artist", TimeSpan.Zero, true) { Duration = TimeSpan.FromSeconds(180) }, CancellationToken.None);
        Assert.Equal("LRCLIB", result.Provider);
        Assert.DoesNotContain(handler.Requests, uri => uri.Contains("/lyric?"));
    }

    [Fact]
    public async Task SwitchingToVideo_CancelsPendingMusicLookup()
    {
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttp(_ => "{}", release);
        using var coordinator = new LyricsCoordinator(new() { OnlineLyrics = true }, new OnlineLyricsSource(new HttpClient(handler)));
        await coordinator.ReadAsync(new("Song", "Artist", TimeSpan.Zero, true), "", CancellationToken.None);
        for (var i = 0; i < 100 && handler.Requests.Count == 0; i++) await Task.Delay(10);
        await coordinator.ReadAsync(new("Video", "", TimeSpan.Zero, true), "", CancellationToken.None);
        for (var i = 0; i < 100 && handler.Cancelled == 0; i++) await Task.Delay(10);
        Assert.True(handler.Cancelled > 0);
    }

    [Fact]
    public async Task UnsupportedCommand_FeedbackExpiresAndSummaryReturns()
    {
        var now = DateTimeOffset.UtcNow;
        using var model = new IslandViewModel(new() { ShowCpu = false, ShowMemory = false }, media: new ControlledMedia { Accepted = false },
            lyrics: new LrcLyricsSource(), utcNow: () => now);
        await model.ControlMediaAsync(IslandMediaCommand.Next);
        Assert.Contains("不支持", model.CompactText);
        now = now.AddSeconds(6);
        model.TickClock();
        Assert.Empty(model.StatusText);
        Assert.Contains("Song", model.CompactText);
    }

    [Fact]
    public async Task LyricsCoordinator_NetworkDoesNotBlockAndCachesFinishedRequest()
    {
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttp(request => request.RequestUri!.AbsolutePath.EndsWith("/lyric")
            ? "{\"data\":{\"lrc\":\"[00:01]cached line\"}}"
            : "{\"data\":[{\"song\":\"Test Song\",\"singer\":\"Artist\",\"id\":1}]}", release);
        using var coordinator = new LyricsCoordinator(new() { OnlineLyrics = true }, new OnlineLyricsSource(new HttpClient(handler)));
        var song = new IslandMedia("Test Song", "Artist", TimeSpan.Zero, true);
        Assert.Empty(await coordinator.ReadAsync(song, "", CancellationToken.None));
        Assert.Contains("获取", coordinator.Status);
        release.SetResult("{\"data\":[{\"song\":\"Test Song\",\"singer\":\"Artist\",\"id\":1}],\"lrc\":\"[00:01]cached line\"}");
        IReadOnlyList<LyricLine> lines = [];
        for (var i = 0; i < 100 && lines.Count == 0; i++) { await Task.Delay(10); lines = await coordinator.ReadAsync(song, "", CancellationToken.None); }
        Assert.Equal("cached line", Assert.Single(lines).Text);
        var count = handler.Requests.Count;
        await coordinator.ReadAsync(song, "", CancellationToken.None);
        Assert.Equal(count, handler.Requests.Count);
    }

    [Fact]
    public async Task DisabledOnlineLyricsAndVideos_NeverSendSearchRequests()
    {
        var handler = new FakeHttp(_ => "{}");
        using var coordinator = new LyricsCoordinator(new() { OnlineLyrics = false }, new OnlineLyricsSource(new HttpClient(handler)));
        await coordinator.ReadAsync(new("Song", "Artist", TimeSpan.Zero, true), "", CancellationToken.None);
        coordinator.ApplySettings(new() { OnlineLyrics = true });
        await coordinator.ReadAsync(new("Video title", "", TimeSpan.Zero, true), "", CancellationToken.None);
        Assert.Empty(handler.Requests);
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, string> response, TaskCompletionSource<string>? delay = null) : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        internal int Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request.RequestUri!.AbsoluteUri);
            if (delay is not null) try { await delay.Task.WaitAsync(cancellationToken); } catch (OperationCanceledException) { Interlocked.Increment(ref Cancelled); throw; }
            var body = response(request);
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class ControlledMedia : IIslandMediaSource, IIslandMediaControls
    {
        internal int Commands;
        internal bool Accepted = true;
        public Task<IslandMedia?> ReadAsync(CancellationToken token) => Task.FromResult<IslandMedia?>(new("Song", "Artist", TimeSpan.Zero, true));
        public Task<bool> ExecuteAsync(IslandMediaCommand command, CancellationToken token) { Commands++; return Task.FromResult(Accepted); }
    }
}
