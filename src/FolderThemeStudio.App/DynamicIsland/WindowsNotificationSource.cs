using System.Runtime.InteropServices;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace FolderThemeStudio.App.DynamicIsland;

public sealed record IslandNotification(string Title, string Body)
{
    public DateTimeOffset CreatedAt { get; init; }
    public string Source { get; init; } = "Windows";
    public string SourceId { get; init; } = "";
    public string Id { get; init; } = "";
}
public interface IIslandNotificationSource : IDisposable
{
    string Status { get; }
    Task<string> RequestAccessAsync();
    Task<IslandNotification?> ReadLatestAsync(CancellationToken token);
}

/// <summary>完整 WinRT 适配器。MSIX 需声明 userNotificationListener，必须由用户在 UI 线程请求授权。</summary>
public sealed class WindowsNotificationSource : IIslandNotificationSource, IIslandNotificationBatchSource
{
    private readonly bool packaged = HasPackageIdentity();
    private UserNotificationListener? listener;
    private IReadOnlyList<IslandNotification> recent = [];
    private int dirty = 1;
    public string Status { get; private set; } = "通知读取需要 MSIX 身份与用户授权";
    public async Task<string> RequestAccessAsync()
    {
        try
        {
            var access = await UserNotificationListener.Current.RequestAccessAsync();
            return Status = access == UserNotificationListenerAccessStatus.Allowed ? "已获通知读取权限" : "未获权限，请在 Windows 设置中允许通知访问";
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or NotImplementedException)
        { return Status = packaged ? "无法获得通知权限，请检查 MSIX 能力声明" : "当前 Windows 不允许 EXE 读取通知，可使用本地消息接口或 MSIX 版本"; }
    }
    public async Task<IslandNotification?> ReadLatestAsync(CancellationToken token)
        => (await ReadRecentAsync(token)).LastOrDefault();
    public async Task<IReadOnlyList<IslandNotification>> ReadRecentAsync(CancellationToken token)
    {
        try
        {
            var current = UserNotificationListener.Current;
            if (current.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) { Status = "通知未授权，请点击设置中的读取权限按钮"; recent = []; Interlocked.Exchange(ref dirty, 1); return recent; }
            if (listener is null) { listener = current; listener.NotificationChanged += NotificationChanged; }
            if (Interlocked.Exchange(ref dirty, 0) == 0) return recent;
            var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask().WaitAsync(TimeSpan.FromSeconds(2), token);
            var messages = new List<IslandNotification>();
            foreach (var item in notifications.OrderByDescending(item => item.CreationTime).Take(32).Reverse())
            {
                var texts = item.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                if (texts is null || texts.Count == 0) continue;
                var source = "Windows"; var sourceId = "";
                try { source = item.AppInfo.DisplayInfo.DisplayName; sourceId = item.AppInfo.AppUserModelId; }
                // 某些旧通知的 AppInfo 会映射 E_NOTIMPL 为 .NET NotImplementedException。
                // 这只影响来源名称，不应丢弃正文或让计时器的 async void 回调崩溃。
                catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or NotImplementedException) { }
                messages.Add(new(ClipboardPreview.Create(texts[0].Text), ClipboardPreview.Create(string.Join(" ", texts.Skip(1).Select(text => text.Text))))
                    { CreatedAt = item.CreationTime, Source = source, SourceId = sourceId, Id = item.Id.ToString() });
            }
            Status = "已连接系统通知，等待新消息";
            return recent = messages;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or NotImplementedException or TimeoutException)
        { Interlocked.Exchange(ref dirty, 1); Status = packaged ? "通知读取暂不可用，请检查授权" : "系统通知暂不可用，可使用本地消息接口；MSIX 版本需授权"; return []; }
    }
    // WinRT 回调可能来自其他线程，只更新标记。WPF 的刷新任务在 UI 线程更新绑定。
    private void NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args) => Interlocked.Exchange(ref dirty, 1);
    public void Dispose() { if (listener is not null) listener.NotificationChanged -= NotificationChanged; listener = null; recent = []; Interlocked.Exchange(ref dirty, 1); }
    private static bool HasPackageIdentity() { uint length = 0; return GetCurrentPackageFullName(ref length, nint.Zero) == 122; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref uint length, nint name);
}
