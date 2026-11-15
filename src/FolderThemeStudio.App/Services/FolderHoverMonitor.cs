using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using FolderThemeStudio.App.ViewModels;
using Point = System.Windows.Point;

namespace FolderThemeStudio.App.Services;

public interface IFolderHoverMonitorService : IDisposable
{
    Task StartAsync(CancellationToken token = default);
    Task StopAsync(CancellationToken token = default) => Task.CompletedTask;
    Task ReloadCatalogAsync(CancellationToken token = default);
}

public interface ICursorPositionSource
{
    bool TryGetPosition(out Point screenPoint);
}

public interface IFolderHoverCardPresenter
{
    void Show(FolderCategoryEntry entry, Rect itemBounds);
    void Hide();
    bool IsPointerOverCard(Point screenPoint) => false;
    void SetDropInteractionEnabled(bool enabled) { }
    void SetInfoTipTarget(ExplorerHoverTarget? target) { }
}

public sealed class FolderHoverMonitor : IFolderHoverMonitorService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);
    private readonly IFolderCategoryCatalog catalog;
    private readonly IExplorerFolderHoverTargetResolver targetResolver;
    private readonly ICursorPositionSource cursor;
    private readonly IFolderHoverCardPresenter presenter;
    private readonly IViewModelDispatcher dispatcher;
    private readonly TimeProvider timeProvider;
    private readonly Func<Point, nint> rootAtPoint;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly NativeMouseInteraction nativeMouse = new();
    private IReadOnlyDictionary<string, FolderCategoryEntry> entries = new Dictionary<string, FolderCategoryEntry>(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? stopSource;
    private Task? pollingTask;
    private volatile bool disposed;
    private HoverTargetSnapshot? quickEditSnapshot;

    private sealed record HoverTargetSnapshot(ExplorerHoverTarget Target, DateTimeOffset Time);

    public ExplorerHoverTarget? TryGetRecentTarget(Point point, nint rootWindowHandle = default)
    {
        var snapshot = Volatile.Read(ref quickEditSnapshot);
        if (snapshot is null || timeProvider.GetUtcNow() - snapshot.Time > TimeSpan.FromSeconds(1) ||
            !snapshot.Target.ItemBounds.Contains(point) ||
            (snapshot.Target.RootWindowHandle != nint.Zero &&
             snapshot.Target.RootWindowHandle != rootWindowHandle)) return null;
        return snapshot.Target;
    }

    public void NotifyNativeMouseDown(Point point)
    {
        nativeMouse.OnDown(point, timeProvider.GetUtcNow());
        Volatile.Write(ref quickEditSnapshot, null);
        try { _ = dispatcher.InvokeAsync(SafeHide); }
        catch (InvalidOperationException) { SafeHide(); }
    }

    public void NotifyNativeMouseUp() => nativeMouse.OnUp(timeProvider.GetUtcNow());

    public Task<ExplorerHoverTarget?> ResolveQuickEditTargetAsync(Point point, nint rootWindowHandle) => Task.Run(() =>
    {
        if (disposed || rootWindowHandle == nint.Zero) return null;
        try
        {
            if (rootAtPoint(point) != rootWindowHandle) return null;
            var target = targetResolver.TryResolve(point);
            return !disposed && target is not null && target.ItemBounds.Contains(point) &&
                (target.RootWindowHandle == nint.Zero || target.RootWindowHandle == rootWindowHandle)
                ? target : null;
        }
        catch (Exception) { return null; }
    });

    public FolderHoverMonitor(
        IFolderCategoryCatalog catalog,
        IExplorerFolderHoverTargetResolver targetResolver,
        ICursorPositionSource cursor,
        IFolderHoverCardPresenter presenter,
        IViewModelDispatcher dispatcher,
        TimeProvider? timeProvider = null,
        Func<Point, nint>? rootAtPoint = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.targetResolver = targetResolver ?? throw new ArgumentNullException(nameof(targetResolver));
        this.cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        this.presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.rootAtPoint = rootAtPoint ?? WindowsRootAtPoint;
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        await lifecycleGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (pollingTask is not null) return;
            try { await ReloadCatalogCoreAsync(token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
                ArgumentException or System.Text.Json.JsonException)
            {
                Volatile.Write(ref entries, new Dictionary<string, FolderCategoryEntry>(StringComparer.OrdinalIgnoreCase));
            }
            stopSource = new CancellationTokenSource();
            pollingTask = PollAsync(stopSource.Token);
        }
        finally { lifecycleGate.Release(); }
    }

    public Task ReloadCatalogAsync(CancellationToken token = default) => ReloadCatalogCoreAsync(token);

    public async Task StopAsync(CancellationToken token = default)
    {
        await lifecycleGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            stopSource?.Cancel();
            if (pollingTask is not null) await pollingTask.ConfigureAwait(false);
            pollingTask = null;
            stopSource?.Dispose();
            stopSource = null;
            Volatile.Write(ref quickEditSnapshot, null);
            await dispatcher.InvokeAsync(SafeHide).ConfigureAwait(false);
        }
        finally { lifecycleGate.Release(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stopSource?.Cancel();
        Volatile.Write(ref quickEditSnapshot, null);
        try { _ = dispatcher.InvokeAsync(SafeHide); }
        catch (InvalidOperationException) { SafeHide(); }
        stopSource?.Dispose();
    }

    private async Task ReloadCatalogCoreAsync(CancellationToken token)
    {
        var loaded = await catalog.LoadAsync(token).ConfigureAwait(false);
        var next = loaded.ToDictionary(item => item.FolderPath, StringComparer.OrdinalIgnoreCase);
        Volatile.Write(ref entries, next);
    }

    private async Task PollAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);
        var decision = new FolderHoverDecision();
        ExplorerHoverTarget? cachedTarget = null;
        ExplorerHoverTarget? displayed = null;
        FolderCategoryEntry? displayedEntry = null;
        DateTimeOffset lastOnFolder = default;

        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var currentEntries = Volatile.Read(ref entries);
                if (!cursor.TryGetPosition(out var point))
                {
                    cachedTarget = null;
                    Volatile.Write(ref quickEditSnapshot, null);
                    decision.Reset();
                    await HideIfVisibleAsync(displayed).ConfigureAwait(false);
                    displayed = null;
                    displayedEntry = null;
                    continue;
                }

                if (nativeMouse.ShouldSuppress(point, timeProvider.GetUtcNow()))
                {
                    cachedTarget = null;
                    Volatile.Write(ref quickEditSnapshot, null);
                    decision.Reset();
                    await HideIfVisibleAsync(displayed).ConfigureAwait(false);
                    displayed = null;
                    displayedEntry = null;
                    continue;
                }

                if (displayed is not null && presenter.IsPointerOverCard(point))
                {
                    lastOnFolder = timeProvider.GetUtcNow();
                    continue;
                }

                var shouldResolve = cachedTarget is null
                    ? true
                    : !cachedTarget.ItemBounds.Contains(point) ||
                      (cachedTarget.RootWindowHandle != nint.Zero && rootAtPoint(point) != cachedTarget.RootWindowHandle);
                if (shouldResolve)
                {
                    try
                    {
                        cachedTarget = targetResolver.TryResolve(point);
                    }
                    catch (Exception)
                    {
                        cachedTarget = null;
                    }
                }
                Volatile.Write(ref quickEditSnapshot, cachedTarget is null ? null : new HoverTargetSnapshot(cachedTarget, timeProvider.GetUtcNow()));
                await dispatcher.InvokeAsync(() => presenter.SetInfoTipTarget(
                    !disposed && !nativeMouse.ShouldSuppress(point, timeProvider.GetUtcNow()) ? cachedTarget : null)).ConfigureAwait(false);

                var now = timeProvider.GetUtcNow();
                var visibleTarget = decision.UpdateTarget(cachedTarget, point, now);
                if (visibleTarget is null && displayed is not null && cachedTarget is not null &&
                    string.Equals(cachedTarget.FolderPath, displayed.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                    cachedTarget.ItemBounds.Contains(point))
                    visibleTarget = cachedTarget;
                if (visibleTarget is null)
                {
                    if (displayed is not null &&
                        (cachedTarget is null || string.Equals(cachedTarget.FolderPath, displayed.FolderPath, StringComparison.OrdinalIgnoreCase)) &&
                        now - lastOnFolder < TimeSpan.FromMilliseconds(500))
                        continue;
                    await HideIfVisibleAsync(displayed).ConfigureAwait(false);
                    displayed = null;
                    displayedEntry = null;
                    continue;
                }

                lastOnFolder = now;
                var visibleEntry = currentEntries.TryGetValue(visibleTarget.FolderPath, out var category)
                    ? category
                    : new FolderCategoryEntry(visibleTarget.FolderPath,
                        Path.GetFileName(visibleTarget.FolderPath), string.Empty, "#2868D7");
                if (!string.Equals(displayed?.FolderPath, visibleTarget.FolderPath, StringComparison.OrdinalIgnoreCase) ||
                    !Equals(displayedEntry, visibleEntry))
                {
                    var showed = false;
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (!disposed && !nativeMouse.ShouldSuppress(point, timeProvider.GetUtcNow()))
                        {
                            presenter.SetDropInteractionEnabled(nativeMouse.IsDragging(point));
                            presenter.Show(visibleEntry, cachedTarget!.ItemBounds);
                            showed = true;
                        }
                    }).ConfigureAwait(false);
                    if (showed)
                    {
                        displayed = visibleTarget;
                        displayedEntry = visibleEntry;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            await HideIfVisibleAsync(displayed).ConfigureAwait(false);
            displayed = null;
            if (!token.IsCancellationRequested)
            {
                stopSource?.Cancel();
            }
        }
        finally
        {
            await dispatcher.InvokeAsync(SafeHide).ConfigureAwait(false);
        }
    }

    private async Task HideIfVisibleAsync(ExplorerHoverTarget? displayed)
    {
        if (displayed is null) return;
        await dispatcher.InvokeAsync(SafeHide).ConfigureAwait(false);
    }

    private void SafeHide()
    {
        try { presenter.SetInfoTipTarget(null); }
        catch (Exception) { }
        try { presenter.Hide(); }
        catch (Exception) { }
    }

    private static nint WindowsRootAtPoint(Point point) =>
        NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(new NativeMethods.NativePoint((int)point.X, (int)point.Y)), 2);

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct NativePoint(int x, int y)
        {
            internal readonly int X = x;
            internal readonly int Y = y;
        }

        [DllImport("user32.dll")]
        internal static extern nint WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        internal static extern nint GetAncestor(nint window, uint flags);
    }
}

internal sealed class WindowsCursorPositionSource : ICursorPositionSource
{
    public bool TryGetPosition(out Point screenPoint)
    {
        if (NativeMethods.GetCursorPos(out var point))
        {
            screenPoint = new Point(point.X, point.Y);
            return true;
        }
        screenPoint = default;
        return false;
    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint { internal int X; internal int Y; }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out NativePoint point);
    }
}

public sealed class NullFolderHoverMonitor : IFolderHoverMonitorService
{
    public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;
    public Task ReloadCatalogAsync(CancellationToken token = default) => Task.CompletedTask;
    public void Dispose() { }
}
