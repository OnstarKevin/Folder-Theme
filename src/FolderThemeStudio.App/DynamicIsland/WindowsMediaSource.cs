using Windows.Media.Control;

namespace FolderThemeStudio.App.DynamicIsland;

public sealed record IslandMedia(string Title, string Artist, TimeSpan Position, bool Playing, string Status = "")
{
    public TimeSpan Duration { get; init; }
    public string Album { get; init; } = "";
    public string SourceApp { get; init; } = "";
}
public interface IIslandMediaSource { Task<IslandMedia?> ReadAsync(CancellationToken token); }
public enum IslandMediaCommand { TogglePlayPause, Previous, Next }
public interface IIslandMediaControls { Task<bool> ExecuteAsync(IslandMediaCommand command, CancellationToken token); }

/// <summary>直接读取 SMTC，无需安装 Now Playing 或猜测 HTTP 端口；播放器需向 Windows 发布媒体会话。</summary>
public sealed class WindowsMediaSource : IIslandMediaSource, IIslandMediaControls, IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? observedSession;
    private GlobalSystemMediaTransportControlsSessionMediaProperties? cachedProperties;
    private int metadataRevision, loadedRevision = -1;
    private DateTimeOffset retryAfter;
    public async Task<IslandMedia?> ReadAsync(CancellationToken token)
    {
        if (DateTimeOffset.UtcNow < retryAfter) return null;
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), token);
            var session = SelectSession(manager);
            if (session is null) { DetachSession(); return null; }
            if (!ReferenceEquals(session, observedSession))
            {
                DetachSession(); observedSession = session;
                session.MediaPropertiesChanged += MetadataChanged;
            }
            // SMTC tells us when metadata changes; do not make a COM metadata request every second.
            var revision = Volatile.Read(ref metadataRevision);
            if (cachedProperties is null || loadedRevision != revision)
            {
                cachedProperties = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), token);
                loadedRevision = revision;
            }
            var properties = cachedProperties;
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var position = timeline.Position;
            // Position 是 LastUpdatedTime 时刻的进度；播放中需要补上经过的时间，暂停时不能补。
            if (playing && timeline.LastUpdatedTime.Year >= 2000)
            {
                var elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                var rate = playback.PlaybackRate ?? 1;
                if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromDays(1) && double.IsFinite(rate) && rate is > 0 and <= 4)
                    position += TimeSpan.FromSeconds(elapsed.TotalSeconds * rate);
            }
            if (timeline.EndTime > TimeSpan.Zero && position > timeline.EndTime) position = timeline.EndTime;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            return new(properties.Title, properties.Artist, position, playing)
            { Duration = timeline.EndTime > timeline.StartTime ? timeline.EndTime - timeline.StartTime : TimeSpan.Zero,
              Album = properties.AlbumTitle ?? "", SourceApp = session.SourceAppUserModelId ?? "" };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            DetachSession(); manager = null;
            retryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
            return new("媒体会话暂不可用", "请确认播放器已启用系统媒体控制", TimeSpan.Zero, false, e.GetType().Name);
        }
    }
    private void MetadataChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Interlocked.Increment(ref metadataRevision);
    private void DetachSession()
    {
        if (observedSession is not null) observedSession.MediaPropertiesChanged -= MetadataChanged;
        observedSession = null; cachedProperties = null; loadedRevision = -1;
        Interlocked.Increment(ref metadataRevision);
    }
    public void Dispose() { DetachSession(); manager = null; }
    private static GlobalSystemMediaTransportControlsSession? SelectSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var current = manager.GetCurrentSession();
        if (current?.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return current;
        return manager.GetSessions().FirstOrDefault(session => session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) ?? current;
    }
    public async Task<bool> ExecuteAsync(IslandMediaCommand command, CancellationToken token)
    {
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), token);
            var session = SelectSession(manager);
            if (session is null) return false;
            var action = command switch
            {
                IslandMediaCommand.Previous => session.TrySkipPreviousAsync(),
                IslandMediaCommand.Next => session.TrySkipNextAsync(),
                _ => session.TryTogglePlayPauseAsync(),
            };
            return await action.AsTask().WaitAsync(TimeSpan.FromSeconds(2), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TimeoutException or InvalidOperationException) { return false; }
    }
}
