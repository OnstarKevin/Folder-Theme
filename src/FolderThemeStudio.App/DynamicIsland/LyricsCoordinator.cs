namespace FolderThemeStudio.App.DynamicIsland;

public interface IIslandSettingsAware { void ApplySettings(IslandSettings settings); }
public interface IContextualLyricsSource { Task<IReadOnlyList<LyricLine>> ReadAsync(IslandMedia song, string directory, CancellationToken token); }

/// <summary>本地优先；网络任务在后台独立执行，刷新调用仅查看缓存，不等网络。</summary>
public sealed class LyricsCoordinator : IIslandLyricsSource, IContextualLyricsSource, IIslandSettingsAware, IDisposable
{
    private readonly object sync = new();
    private readonly OnlineLyricsSource online;
    private readonly LrcLyricsSource local = new();
    private readonly Dictionary<string, (LyricsResult Result, DateTimeOffset Expires)> cache = new();
    private IslandSettings settings;
    private CancellationTokenSource? pending;
    private string pendingKey = "";
    private bool disposed;
    private int revision;
    public string Status { get; private set; } = "未找到歌词";
    public LyricsCoordinator(IslandSettings settings, OnlineLyricsSource? online = null) { this.settings = settings; this.online = online ?? new(); }
    public Task<IReadOnlyList<LyricLine>> ReadAsync(string title, string artist, string directory, CancellationToken token) => ReadAsync(new(title, artist, TimeSpan.Zero, false), directory, token);
    public async Task<IReadOnlyList<LyricLine>> ReadAsync(IslandMedia song, string directory, CancellationToken token)
    {
        var key = $"{song.Title}\0{song.Artist}\0{song.Duration.TotalSeconds:0}";
        int readRevision;
        lock (sync)
        {
            if (disposed || !settings.ShowMusic || !settings.ShowLyrics) return [];
            readRevision = revision;
            if (pendingKey != key) CancelPending();
        }
        var localLines = await local.ReadAsync(song.Title, song.Artist, directory, token);
        if (localLines.Count > 0) { Status = "本地 LRC"; return localLines; }
        lock (sync)
        {
            if (disposed || !settings.ShowMusic || !settings.ShowLyrics) return [];
            if (readRevision != revision) return [];
            if (!settings.OnlineLyrics) { Status = "在设置中开启在线歌词，或选择 LRC 文件夹"; return []; }
            if (song.Status.Length > 0 || string.IsNullOrWhiteSpace(song.Title) || string.IsNullOrWhiteSpace(song.Artist)) { Status = "该媒体没有完整歌名和歌手"; return []; }
            if (cache.TryGetValue(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow) { Status = entry.Result.Provider; return entry.Result.Lines; }
            if (pendingKey == key) { Status = "正在获取在线歌词…"; return []; }
            pending?.Cancel();
            var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            pending = request; pendingKey = key; Status = "正在获取在线歌词…";
            _ = Task.Run(() => FetchAsync(key, song, request));
            return [];
        }
    }
    private async Task FetchAsync(string key, IslandMedia song, CancellationTokenSource request)
    {
        try
        {
            var result = await online.FetchAsync(song, request.Token).ConfigureAwait(false);
            lock (sync)
            {
                if (disposed || request.IsCancellationRequested || pending != request) return;
                if (cache.Count >= 24) cache.Remove(cache.Keys.First());
                cache[key] = (result, DateTimeOffset.UtcNow.Add(result.Lines.Count > 0 ? TimeSpan.FromHours(12) : TimeSpan.FromMinutes(3)));
                Status = result.Provider;
            }
        }
        catch (OperationCanceledException) { }
        finally { lock (sync) { if (pending == request) { pending = null; pendingKey = ""; } } request.Dispose(); }
    }
    public void CancelPending() { lock (sync) { pending?.Cancel(); pending = null; pendingKey = ""; } }
    public void ApplySettings(IslandSettings next) { lock (sync) { revision++; settings = next; CancelPending(); } }
    public void Dispose() { lock (sync) { disposed = true; pending?.Cancel(); cache.Clear(); } }
}

