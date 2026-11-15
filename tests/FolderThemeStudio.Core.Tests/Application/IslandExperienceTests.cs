using FolderThemeStudio.App.DynamicIsland;
using Xunit;
using FolderThemeStudio.Core.Tests.TestSupport;
using System.Reflection;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandExperienceTests
{
    [Fact]
    public void DragPhysics_ExposesBoundedSpringAndReturnsToRest()
    {
        var physics = new IslandDragPhysics();
        physics.Drag(5000d, -5000d);
        for (var i = 0; i < 60; i++) physics.Step(1d / 60);
        Assert.InRange((double)physics.ScaleX, .97, 1.03);
        Assert.InRange((double)physics.ScaleY, .97, 1.03);
        Assert.InRange((double)physics.Angle, -1.2, 1.2);
        physics.Release();
        for (var i = 0; i < 180; i++) physics.Step(1d / 60);
        Assert.InRange(Math.Abs((double)physics.ScaleX - 1), 0, .0001);
        Assert.InRange(Math.Abs((double)physics.ScaleY - 1), 0, .0001);
        Assert.InRange(Math.Abs((double)physics.Angle), 0, .0001);
    }

    [Fact]
    public async Task Notifications_DoNotReplayHistoryOrRepeatAfterDisplayExpires()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new Messages { Latest = new("旧消息", "旧正文") { CreatedAt = now.AddMinutes(-1) } };
        using var model = Create(source, () => now);
        await model.RefreshAsync();
        Assert.DoesNotContain("旧消息", model.CompactText);
        source.Latest = new("新邮件", "正文") { CreatedAt = now.AddSeconds(1) };
        now = now.AddSeconds(1);
        await model.RefreshAsync();
        Assert.Contains("新邮件", model.CompactText);
        now = now.AddSeconds(7);
        model.TickClock();
        await model.RefreshAsync();
        Assert.DoesNotContain("新邮件", model.CompactText);
    }

    [Fact]
    public async Task Notifications_QueueArrivalsWhileFirstMessageIsVisible()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new Messages();
        using var model = Create(source, () => now);
        source.Latest = new("第一条", "正文") { CreatedAt = now.AddSeconds(1) };
        now = now.AddSeconds(1);
        await model.RefreshAsync();
        source.Latest = new("第二条", "正文") { CreatedAt = now.AddSeconds(1) };
        now = now.AddSeconds(1);
        await model.RefreshAsync();
        Assert.Equal("第一条", model.NotificationTitle);
        now = now.AddSeconds(6);
        model.TickClock();
        Assert.Equal("第二条", model.NotificationTitle);
    }

    private static IslandViewModel Create(Messages source, Func<DateTimeOffset> now) => new(
        new() { ShowTime = false, ShowCpu = false, ShowMemory = false, ShowMusic = false, ShowNotifications = true },
        notifications: source, utcNow: now);

    [Fact]
    public void Queue_FiltersSourcesCapsBurstAndRejectsStaleFutureMessages()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = new IslandNotificationQueue(now);
        queue.Enqueue([new("拦截", "") { Source = "Mail", CreatedAt = now },
            new("太旧", "") { CreatedAt = now.AddSeconds(-1) },
            new("未来", "") { CreatedAt = now.AddDays(1) }], now, "mail;other");
        Assert.Null(queue.Current);
        queue.Enqueue(Enumerable.Range(1, 20).Select(i => new IslandNotification(i.ToString(), "")
            { CreatedAt = now, Id = i.ToString() }), now, "");
        Assert.Equal("13", queue.Current!.Title);
        Assert.Equal(7, queue.PendingCount);
        queue.Advance(now.AddMinutes(2));
        Assert.Null(queue.Current);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task DisablingNotificationsClearsQueueAndReenableDoesNotReplayOldMessages()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new Messages();
        using var model = Create(source, () => now);
        source.Latest = new("短暂提醒", "") { CreatedAt = now };
        await model.RefreshAsync();
        Assert.Contains("短暂提醒", model.CompactText);
        model.ApplySettings(model.Settings with { ShowNotifications = false });
        now = now.AddSeconds(10);
        model.ApplySettings(model.Settings with { ShowNotifications = true });
        await model.RefreshAsync();
        Assert.DoesNotContain("短暂提醒", model.CompactText);
    }

    [Fact]
    public void Snap_HandlesNegativeCoordinateMonitorWithoutMovingDistantWindow()
    {
        var area = new System.Windows.Rect(-1920, 0, 1920, 1080);
        var size = new System.Windows.Size(260, 64);
        Assert.Equal(new System.Windows.Point(-1920, 0), IslandWindowPolicy.SnapPosition(new(-1910, 10), area, size));
        Assert.Equal(new System.Windows.Point(-500, 400), IslandWindowPolicy.SnapPosition(new(-500, 400), area, size));
    }

    [Fact]
    public void ClosingWindowDetachesSpringRenderingAndRestoresVisuals()
    {
        WpfTestHost.Invoke(() =>
        {
            var source = new Messages();
            var settings = new IslandSettings { ShowCpu = false, ShowMemory = false, ShowMusic = false };
            using var model = new IslandViewModel(settings, notifications: source);
            var window = new DynamicIslandWindow(settings, model, (_, _) => new SilentClipboard());
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                window.Show();
                typeof(DynamicIslandWindow).GetMethod("StartDragRendering", flags)!.Invoke(window, null);
                Assert.True((bool)typeof(DynamicIslandWindow).GetField("rendering", flags)!.GetValue(window)!);
            }
            finally { window.Close(); }
            Assert.False((bool)typeof(DynamicIslandWindow).GetField("rendering", flags)!.GetValue(window)!);
            var scale = (System.Windows.Media.ScaleTransform)window.FindName("DragScale");
            Assert.Equal(1, scale.ScaleX); Assert.Equal(1, scale.ScaleY);
        });
    }

    private sealed class SilentClipboard : IIslandClipboardListener
    {
        public void SetEnabled(bool enabled) { }
        public void Dispose() { }
    }

    private sealed class Messages : IIslandNotificationSource
    {
        public IslandNotification? Latest { get; set; }
        public string Status => "等待新消息";
        public Task<string> RequestAccessAsync() => Task.FromResult(Status);
        public Task<IslandNotification?> ReadLatestAsync(CancellationToken token) => Task.FromResult(Latest);
        public void Dispose() { }
    }
}
