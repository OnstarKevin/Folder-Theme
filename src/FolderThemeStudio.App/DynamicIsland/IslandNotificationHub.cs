namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>原生系统通知与可选本地消息汇合，关闭模块就关闭网络监听。</summary>
public sealed class IslandNotificationHub : IIslandNotificationSource, IIslandSettingsAware, IIslandNotificationBatchSource
{
    private readonly WindowsNotificationSource native = new();
    private LocalNotificationBridge? bridge;
    private IslandSettings settings;
    public string Status => bridge is null ? native.Status : bridge.Status + "；" + native.Status;
    public IslandNotificationHub(IslandSettings settings) { this.settings = settings; ApplySettings(settings); }
    public Task<string> RequestAccessAsync() => native.RequestAccessAsync();
    public async Task<IslandNotification?> ReadLatestAsync(CancellationToken token)
        => (await ReadRecentAsync(token)).LastOrDefault();
    public async Task<IReadOnlyList<IslandNotification>> ReadRecentAsync(CancellationToken token)
    {
        var readingBridge = bridge;
        var system = await native.ReadRecentAsync(token);
        var local = readingBridge?.ReadRecent() ?? [];
        return system.Concat(local).OrderBy(item => item.CreatedAt).ToArray();
    }
    public void ApplySettings(IslandSettings next)
    {
        var restart = bridge is null || settings.NotificationPort != next.NotificationPort || settings.NotificationToken != next.NotificationToken;
        settings = next;
        if (!next.ShowNotifications || !next.LocalNotifications) { bridge?.Dispose(); bridge = null; return; }
        if (restart) { bridge?.Dispose(); bridge = new(next.NotificationPort, next.NotificationToken); bridge.Start(); }
    }
    public void Dispose() { bridge?.Dispose(); bridge = null; native.Dispose(); }
}
