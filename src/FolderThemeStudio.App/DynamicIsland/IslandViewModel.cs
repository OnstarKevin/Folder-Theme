using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FolderThemeStudio.App.DynamicIsland;

public sealed record IslandHookEvent(string Name, object Payload);

/// <summary>只负责显示数据；Win32/WinRT 都经接口注入，测试不需要更改用户的剪贴板或播放器。</summary>
public sealed class IslandViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISystemMetricsSource metrics;
    private readonly IIslandMediaSource media;
    private readonly IIslandLyricsSource lyrics;
    private readonly IIslandNotificationSource notifications;
    private readonly IIslandQuoteSource quotes;
    private CancellationTokenSource quoteStop = new();
    private bool quoteRefreshing;
    private DateTimeOffset nextQuoteAt = DateTimeOffset.MinValue;
    private DateTimeOffset clipboardUntil;
    private IslandQuote? quote;
    private readonly CancellationTokenSource stop = new();
    private IReadOnlyList<LyricLine> lyricLines = [];
    private string loadedSong = string.Empty;
    private bool refreshing;
    private bool refreshingNotifications;
    private bool disposed;
    private int settingsRevision;
    private IslandSettings settings;
    private SystemMetrics snapshot = new(null, null, 0, 0);
    private IslandMedia? currentSong;
    private bool hasNotification, hasClipboard;
    private int summarySlot;
    private readonly Func<DateTimeOffset> utcNow;
    private DateTimeOffset? commandFeedbackUntil;
    private readonly IslandNotificationQueue messageQueue;
    private readonly Dictionary<string, (string Text, string Details, DateTimeOffset Until, long Order)> extensions = new(StringComparer.Ordinal);
    private long extensionOrder;
    private string? lastCompactText, lastCompactDetails;
    private string? lastHoverTitle, lastHoverBody;
    private string lastMediaEvent = "", lastNotificationEvent = "";
    private bool controllingMedia;
    public event EventHandler<IslandHookEvent>? HookEvent;
    public IslandViewModel(IslandSettings settings, ISystemMetricsSource? metrics = null, IIslandMediaSource? media = null,
        IIslandLyricsSource? lyrics = null, IIslandNotificationSource? notifications = null, Func<DateTimeOffset>? utcNow = null, IIslandQuoteSource? quotes = null)
    {
        this.settings = settings.Normalize();
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        messageQueue = new(this.utcNow());
        this.metrics = metrics ?? new WindowsSystemMetricsSource();
        this.media = media ?? new WindowsMediaSource();
        this.lyrics = lyrics ?? new LyricsCoordinator(this.settings);
        this.notifications = notifications ?? new IslandNotificationHub(this.settings);
        this.quotes = quotes ?? new HitokotoQuoteSource();
    }
    public IslandSettings Settings => settings;
    public bool ShowTime => settings.ShowTime;
    public bool ShowCpu => settings.ShowCpu;
    public bool ShowMemory => settings.ShowMemory;
    public bool ShowMetrics => ShowCpu || ShowMemory;
    public bool ShowClipboard => settings.ShowClipboard;
    public bool ShowMusic => settings.ShowMusic;
    public bool ShowNotifications => settings.ShowNotifications;
    public string TimeText { get; private set; } = DateTime.Now.ToString("HH:mm:ss");
    public string CpuText => snapshot.CpuPercent is { } value ? $"{value:0}%" : "—";
    public string MemoryText => snapshot.MemoryPercent is { } value ? $"{value:0}%" : "—";
    public double CpuValue => snapshot.CpuPercent ?? 0;
    public double MemoryValue => snapshot.MemoryPercent ?? 0;
    public string MemoryDetail => snapshot.TotalMemoryBytes > 0 ? $"{snapshot.UsedMemoryBytes / 1073741824d:0.#} / {snapshot.TotalMemoryBytes / 1073741824d:0.#} GB" : "正在采样";
    public string MetricsText => string.Join("  ", new[] { ShowCpu ? $"CPU {CpuText}" : null, ShowMemory ? $"内存 {MemoryText}" : null }.Where(x => x is not null));
    public string MetricsDetail => MetricsText + (ShowMemory ? " · " + MemoryDetail : "");
    public bool HasMediaSession => ShowMusic && currentSong is { Status.Length: 0 } song && !string.IsNullOrWhiteSpace(song.Title);
    public bool IsMusicPlaying => HasMediaSession && currentSong!.Playing;
    public bool MediaControlsEnabled => HasMediaSession && !controllingMedia;
    public string PlaybackGlyph => IsMusicPlaying ? "\uE769" : "\uE768";
    public string PlaybackLabel => IsMusicPlaying ? "暂停" : "播放";
    public double MusicProgress => currentSong is { Duration.TotalSeconds: > 0 } song ? Math.Clamp(song.Position.TotalSeconds / song.Duration.TotalSeconds, 0, 1) : 0;
    public string MusicTimeline => currentSong is { } song ? $"{Math.Max(0, (int)song.Position.TotalMinutes)}:{Math.Max(0, song.Position.Seconds):00} / {Math.Max(0, (int)song.Duration.TotalMinutes)}:{Math.Max(0, song.Duration.Seconds):00}" : "";
    public string MusicSourceDetail => currentSong is { } song ? string.Join(" · ", new[] { song.Album, song.SourceApp }.Where(x => !string.IsNullOrWhiteSpace(x))) : "";
    public string ClipboardText { get; private set; } = "开启后显示最新文本";
    public string MusicTitle { get; private set; } = "等待音乐播放";
    public string MusicArtist { get; private set; } = "播放器需启用系统媒体控制";
    public string LyricText { get; private set; } = "可在设置中选择 LRC 歌词文件夹";
    public string LyricTranslation { get; private set; } = "";
    public string NotificationTitle { get; private set; } = "系统通知";
    public string NotificationBody { get; private set; } = "需要 MSIX 身份与通知读取授权";
    public string NotificationSource { get; private set; } = "";
    public string NotificationBadge => !hasNotification ? "●" : System.Globalization.StringInfo.GetNextTextElement(
        string.IsNullOrWhiteSpace(NotificationSource) ? "通" : NotificationSource);
    public string NotificationQueueDetail => messageQueue.PendingCount > 0 ? $"另有 {messageQueue.PendingCount} 条待显示" : "";
    public string StatusText { get; private set; } = string.Empty;
    /// <summary>迷你胶囊只显示一行；换行详情供主动悬停查看，不影响窗口尺寸。</summary>
    public string CompactDetails
    {
        get
        {
            if (ShowNotifications && hasNotification) return $"{NotificationSource} · {NotificationTitle}\n{ClipboardPreview.Create(NotificationBody)}";
            if (ShowClipboard && hasClipboard && utcNow() < clipboardUntil) return "剪贴板 · 预览保留 10 秒\n" + ClipboardText;
            if (ShowMusic && currentSong is { Playing: true, Status.Length: 0 } song && !string.IsNullOrWhiteSpace(song.Title))
                return $"{MusicTitle} · {MusicArtist}\n{song.Position:mm\\:ss} / {song.Duration:mm\\:ss}\n{ClipboardPreview.Create(LyricText)}";
            if (ActiveExtension() is { } extension) return extension.Details.Length > 0 ? extension.Text + "\n" + extension.Details : extension.Text;
            if (settings.ShowQuotes && quote is not null) return quote.Text + "\n" + quote.Credit;
            return CompactText;
        }
    }
    public string CompactText
    {
        get
        {
            // 短暂事件优先；金句只填充空闲时间，不能遮挡消息或播放中的歌词。
            if (ShowNotifications && hasNotification) return ClipboardPreview.Create($"{NotificationSource} · {NotificationTitle} {NotificationBody}");
            if (ShowClipboard && hasClipboard && utcNow() < clipboardUntil) return "复制 · " + ClipboardText;
            if (StatusText.Length > 0 && commandFeedbackUntil is not null) return ClipboardPreview.Create(StatusText);
            if (ShowMusic && currentSong is { Playing: true, Status.Length: 0 } song && !string.IsNullOrWhiteSpace(song.Title))
                return ClipboardPreview.Create("♫ " + (settings.ShowLyrics && lyricLines.Count > 0 && !string.IsNullOrWhiteSpace(LyricText) ? LyricText : MusicTitle));
            if (StatusText.Length > 0) return ClipboardPreview.Create(StatusText);
            if (ActiveExtension() is { } extension) return extension.Text;
            if (settings.ShowQuotes) return quote?.Text ?? "留一点时间，给美好的事情。";
            var items = new List<string>();
            if (ShowTime) items.Add(TimeText);
            if (items.Count == 0) return ShowMetrics ? "系统状态" : "通过托盘打开设置";
            return ClipboardPreview.Create(items[summarySlot % items.Count]);
        }
    }
    public string HoverTitle => ShowNotifications && hasNotification ? $"{NotificationSource} · {NotificationTitle}"
        : ShowClipboard && hasClipboard && utcNow() < clipboardUntil ? "剪贴板 · 10 秒预览"
        : HasMediaSession ? $"{MusicTitle} · {MusicArtist}"
        : ActiveExtension() is not null ? "DIY 插件"
        : settings.ShowQuotes && quote is not null ? "一言 · " + quote.Credit : "桌面灵动岛";
    public string HoverBody => ShowNotifications && hasNotification ? ClipboardPreview.Create(NotificationBody)
        : ShowClipboard && hasClipboard && utcNow() < clipboardUntil ? ClipboardText
        : HasMediaSession ? (IsMusicPlaying ? "" : "已暂停 · ") + ClipboardPreview.Create(LyricText)
        : ActiveExtension() is { } extension ? extension.Text + (extension.Details.Length > 0 ? "\n" + extension.Details : "")
        : settings.ShowQuotes && quote is not null ? quote.Text : CompactText;
    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<IslandHookEvent> GetHookSnapshot()
    {
        var events = new List<IslandHookEvent>();
        if (ShowMusic && currentSong is { } song) events.Add(new("media.changed", new { title = song.Title, artist = song.Artist, playing = song.Playing, positionMs = song.Position.TotalMilliseconds, durationMs = song.Duration.TotalMilliseconds }));
        if (ShowClipboard && hasClipboard) events.Add(new("clipboard.changed", new { text = ClipboardText }));
        if (ShowNotifications && hasNotification) events.Add(new("notification.received", new { source = NotificationSource, title = ClipboardPreview.Create(NotificationTitle), body = ClipboardPreview.Create(NotificationBody) }));
        return events;
    }

    private (string Text, string Details, DateTimeOffset Until, long Order)? ActiveExtension() =>
        extensions.Values.Where(x => x.Until > utcNow()).OrderByDescending(x => x.Order).Select(x => ((string Text, string Details, DateTimeOffset Until, long Order)?)x).FirstOrDefault();
    public void SetExtensionContent(string pluginId, string text, string details, int ttlSeconds = 10)
    {
        if (disposed || string.IsNullOrWhiteSpace(pluginId)) return;
        var compact = ClipboardPreview.Create(text ?? "");
        if (compact.Length > 80)
        {
            var end = 79;
            if (char.IsHighSurrogate(compact[end - 1])) end--;
            compact = compact[..end] + "…";
        }
        if (compact.Length == 0) { ClearExtensionContent(pluginId); return; }
        extensions[pluginId] = (compact, ClipboardPreview.Create(details ?? ""), utcNow().AddSeconds(Math.Clamp(ttlSeconds, 1, 60)), ++extensionOrder);
        Notify(nameof(CompactText)); Notify(nameof(CompactDetails));
    }
    public void ClearExtensionContent(string pluginId)
    {
        if (!extensions.Remove(pluginId)) return;
        Notify(nameof(CompactText)); Notify(nameof(CompactDetails));
    }

    public void ApplySettings(IslandSettings next)
    {
        if (settings.ShowMusic && !next.ShowMusic) (media as IDisposable)?.Dispose();
        if (settings.ShowQuotes != next.ShowQuotes || settings.AutoRefreshQuotes != next.AutoRefreshQuotes)
        {
            quoteStop.Cancel(); quoteStop.Dispose(); quoteStop = new();
            nextQuoteAt = DateTimeOffset.MinValue;
        }
        var clearMessages = settings.ShowNotifications != next.ShowNotifications || settings.BlockedNotificationSources != next.BlockedNotificationSources;
        settings = next.Normalize();
        if (clearMessages) { messageQueue.Clear(utcNow()); hasNotification = false; }
        settingsRevision++;
        loadedSong = string.Empty;
        lyricLines = [];
        LyricTranslation = "";
        if (lyrics is IIslandSettingsAware configurableLyrics) configurableLyrics.ApplySettings(settings);
        if (notifications is IIslandSettingsAware configurableNotifications) configurableNotifications.ApplySettings(settings);
        if (!ShowClipboard) { ClipboardText = string.Empty; hasClipboard = false; clipboardUntil = default; }
        if (!settings.ShowLyrics) { LyricText = string.Empty; Notify(nameof(LyricText)); }
        if (!ShowNotifications) { notifications.Dispose(); NotificationTitle = "系统通知"; NotificationBody = "通知模块已关闭"; }
        UpdateNotificationPresentation();
        foreach (var name in new[] { nameof(ShowTime), nameof(ShowCpu), nameof(ShowMemory), nameof(ShowMetrics), nameof(ShowClipboard), nameof(ShowMusic), nameof(ShowNotifications), nameof(ClipboardText) }) Notify(name);
        Notify(nameof(NotificationTitle)); Notify(nameof(NotificationBody));
    }
    public void TickClock()
    {
        foreach (var key in extensions.Where(x => x.Value.Until <= utcNow()).Select(x => x.Key).ToArray()) extensions.Remove(key);
        TimeText = DateTime.Now.ToString("HH:mm:ss");
        summarySlot = (int)(utcNow().ToUnixTimeSeconds() / 5 % int.MaxValue);
        if (commandFeedbackUntil is { } until && utcNow() >= until) ReportStatus("");
        messageQueue.Advance(utcNow());
        if (ShowNotifications) UpdateNotificationPresentation();
        Notify(nameof(TimeText)); Notify(nameof(CompactText)); Notify(nameof(CompactDetails));
    }
    public void SetClipboard(string text)
    {
        hasClipboard = ShowClipboard && !string.IsNullOrWhiteSpace(text);
        clipboardUntil = hasClipboard ? utcNow().AddSeconds(10) : default;
        ClipboardText = !ShowClipboard ? string.Empty : string.IsNullOrEmpty(text) ? "剪贴板没有文本" : ClipboardPreview.Create(text);
        if (ShowClipboard) HookEvent?.Invoke(this, new("clipboard.changed", new { text = ClipboardText }));
        Notify(nameof(ClipboardText));
    }
    /// <summary>与系统采样分开计时；慢网络不阻塞时间、剪贴板和媒体刷新。失败保留上次结果并退避 30 秒。</summary>
    public async Task RefreshQuoteAsync()
    {
        if (disposed || !settings.ShowQuotes || quoteRefreshing || utcNow() < nextQuoteAt || (!settings.AutoRefreshQuotes && quote is not null)) return;
        quoteRefreshing = true;
        var cancellation = quoteStop.Token;
        nextQuoteAt = utcNow().AddSeconds(30);
        try
        {
            var next = await quotes.ReadAsync(cancellation);
            if (disposed || cancellation.IsCancellationRequested || !settings.ShowQuotes) return;
            if (next is not null) quote = next;
            Notify(nameof(CompactText)); Notify(nameof(CompactDetails));
        }
        catch (Exception e) when (e is OperationCanceledException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or System.IO.IOException) { }
        finally { quoteRefreshing = false; }
    }
    public void ReportStatus(string text) { commandFeedbackUntil = null; StatusText = text; Notify(nameof(StatusText)); }
    private void ReportCommandFeedback(string text)
    {
        StatusText = text; commandFeedbackUntil = utcNow().AddSeconds(4); Notify(nameof(StatusText));
    }
    public Task<string> RequestNotificationAccessAsync() => notifications.RequestAccessAsync();

    /// <summary>消息使用独立的单飞刷新，避免媒体查询或慢歌词文件阻塞优先提醒。</summary>
    public async Task RefreshNotificationsAsync()
    {
        if (disposed || !ShowNotifications || refreshingNotifications) return;
        refreshingNotifications = true;
        var revision = settingsRevision;
        try
        {
            IReadOnlyList<IslandNotification> messages;
            if (notifications is IIslandNotificationBatchSource batch) messages = await batch.ReadRecentAsync(stop.Token);
            else { var latest = await notifications.ReadLatestAsync(stop.Token); messages = latest is null ? [] : [latest]; }
            if (disposed || revision != settingsRevision) return;
            messageQueue.Enqueue(messages, utcNow(), settings.BlockedNotificationSources);
            UpdateNotificationPresentation();
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException)
        { if (!disposed) { NotificationBody = "消息暂不可用，请检查通知访问权限"; Notify(nameof(NotificationBody)); } }
        finally { refreshingNotifications = false; }
    }

    public async Task RefreshAsync(bool includeNotifications = true)
    {
        if (disposed || refreshing) return;
        refreshing = true;
        var revision = settingsRevision;
        try
        {
            if (includeNotifications) await RefreshNotificationsAsync();
            if (disposed || revision != settingsRevision) return;
            if (ShowMetrics)
            {
                var next = await Task.Run(() => metrics.Read(ShowCpu, ShowMemory), stop.Token);
                if (disposed || revision != settingsRevision) return;
                snapshot = next;
                foreach (var name in new[] { nameof(CpuText), nameof(MemoryText), nameof(CpuValue), nameof(MemoryValue), nameof(MemoryDetail) }) Notify(name);
            }
            if (ShowMusic)
            {
                var song = await media.ReadAsync(stop.Token);
                if (disposed || revision != settingsRevision) return;
                currentSong = song;
                var mediaEventKey = $"{song?.Title}\0{song?.Artist}\0{song?.Playing}\0{song?.SourceApp}";
                if (mediaEventKey != lastMediaEvent)
                {
                    lastMediaEvent = mediaEventKey;
                    HookEvent?.Invoke(this, new("media.changed", new { title = song?.Title ?? "", artist = song?.Artist ?? "", playing = song?.Playing ?? false, positionMs = song?.Position.TotalMilliseconds ?? 0, durationMs = song?.Duration.TotalMilliseconds ?? 0 }));
                }
                if (song is null && lyrics is LyricsCoordinator idleLyrics) idleLyrics.CancelPending();
                MusicTitle = string.IsNullOrWhiteSpace(song?.Title) ? "等待音乐播放" : song.Title;
                MusicArtist = song is null ? "播放器需启用系统媒体控制" : song.Artist;
                if (settings.ShowLyrics)
                {
                var key = $"{song?.Title}\0{song?.Artist}\0{song?.Duration}\0{song?.SourceApp}\0{settings.LyricsDirectory}";
                var lyricReadFailed = false;
                if (loadedSong != key || lyricLines.Count == 0)
                {
                    try
                    {
                        lyricLines = song is null ? [] : lyrics is IContextualLyricsSource contextual
                            ? await contextual.ReadAsync(song, settings.LyricsDirectory, stop.Token)
                            : await lyrics.ReadAsync(song.Title, song.Artist, settings.LyricsDirectory, stop.Token);
                        loadedSong = key;
                    }
                    catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
                    {
                        // 歌词文件被占用/失去权限时只降级歌词，继续发布歌曲元数据和刷新通知。
                        // 不缓存失败的 key，文件恢复可读后下一次采样自动重试。
                        lyricLines = [];
                        lyricReadFailed = true;
                    }
                }
                if (disposed || revision != settingsRevision) return;
                var line = lyricReadFailed ? "歌词读取暂不可用，请检查文件权限" : song is null ? "可在设置中选择 LRC 歌词文件夹" : lyricLines.Count == 0
                    ? lyrics is LyricsCoordinator coordinator ? coordinator.Status : "未找到匹配的 LRC 歌词" : LrcLyricsSource.CurrentLine(lyricLines, song.Position);
                LyricTranslation = song is null ? "" : lyricLines.LastOrDefault(item => item.At <= song.Position)?.Translation ?? "";
                if (line != LyricText) { LyricText = line; Notify(nameof(LyricText)); }
                }
                else { lyricLines = []; LyricText = LyricTranslation = string.Empty; Notify(nameof(LyricText)); Notify(nameof(LyricTranslation)); }
                Notify(nameof(MusicTitle)); Notify(nameof(MusicArtist));
            }
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { if (!disposed) ReportStatus("部分信息暂不可用，请检查播放器或歌词目录"); }
        finally { refreshing = false; if (disposed) stop.Dispose(); }
    }
    public async Task ControlMediaAsync(IslandMediaCommand command)
    {
        if (disposed || !ShowMusic || controllingMedia) return;
        controllingMedia = true; Notify(nameof(MediaControlsEnabled));
        try
        {
            var accepted = media is IIslandMediaControls controls && await controls.ExecuteAsync(command, stop.Token);
            if (disposed) return;
            if (accepted) ReportStatus(""); else ReportCommandFeedback("播放器当前不支持此控制");
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.IO.IOException or InvalidOperationException or UnauthorizedAccessException)
        { if (!disposed) ReportCommandFeedback("播放器暂未响应，请稍后再试。"); }
        finally { controllingMedia = false; if (!disposed) Notify(nameof(MediaControlsEnabled)); }
    }
    private void UpdateNotificationPresentation()
    {
        var message = ShowNotifications ? messageQueue.Current : null;
        hasNotification = message is not null;
        NotificationTitle = message?.Title ?? "系统通知";
        NotificationBody = message?.Body ?? (ShowNotifications ? notifications.Status : "通知模块已关闭");
        NotificationSource = message?.Source ?? "";
        var notificationKey = message is null ? "" : $"{message.Source}\0{message.Title}\0{message.Body}\0{message.CreatedAt}";
        if (notificationKey != lastNotificationEvent)
        {
            lastNotificationEvent = notificationKey;
            if (message is not null) HookEvent?.Invoke(this, new("notification.received", new { source = message.Source, title = ClipboardPreview.Create(message.Title), body = ClipboardPreview.Create(message.Body) }));
        }
        Notify(nameof(NotificationTitle)); Notify(nameof(NotificationBody));
        Notify(nameof(NotificationSource)); Notify(nameof(NotificationBadge)); Notify(nameof(NotificationQueueDetail));
    }
    private void Notify([CallerMemberName] string? name = null)
    {
        if (name is nameof(CpuText) or nameof(MemoryText) or nameof(ShowCpu) or nameof(ShowMemory))
        { PropertyChanged?.Invoke(this, new(nameof(MetricsText))); PropertyChanged?.Invoke(this, new(nameof(MetricsDetail))); }
        if (name is nameof(MusicTitle) or nameof(ShowMusic))
            foreach (var derived in new[] { nameof(HasMediaSession), nameof(IsMusicPlaying), nameof(MediaControlsEnabled), nameof(PlaybackGlyph), nameof(PlaybackLabel), nameof(MusicProgress), nameof(MusicTimeline), nameof(MusicSourceDetail) })
                PropertyChanged?.Invoke(this, new(derived));
        if (name == nameof(CompactText)) { var value = CompactText; if (lastCompactText == value) return; lastCompactText = value; }
        if (name == nameof(CompactDetails))
        {
            var title = HoverTitle; var body = HoverBody;
            if (lastHoverTitle != title) { lastHoverTitle = title; PropertyChanged?.Invoke(this, new(nameof(HoverTitle))); }
            if (lastHoverBody != body) { lastHoverBody = body; PropertyChanged?.Invoke(this, new(nameof(HoverBody))); }
            var value = CompactDetails; if (lastCompactDetails == value) return; lastCompactDetails = value;
        }
        PropertyChanged?.Invoke(this, new(name));
        if (name is nameof(ShowMusic) or nameof(ShowClipboard) or nameof(ShowNotifications) or nameof(MusicTitle)
            or nameof(MusicArtist) or nameof(LyricText) or nameof(ClipboardText) or nameof(NotificationTitle)
            or nameof(NotificationBody) or nameof(StatusText) or nameof(CpuText) or nameof(MemoryText))
        {
            Notify(nameof(CompactText)); Notify(nameof(CompactDetails));
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; extensions.Clear(); HookEvent = null; quoteStop.Cancel(); quoteStop.Dispose(); stop.Cancel(); messageQueue.Clear(utcNow()); notifications.Dispose(); (media as IDisposable)?.Dispose(); (lyrics as IDisposable)?.Dispose(); if (!refreshing) stop.Dispose(); }
}

