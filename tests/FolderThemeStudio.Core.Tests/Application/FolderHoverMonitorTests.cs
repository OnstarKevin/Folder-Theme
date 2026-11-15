using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class FolderHoverMonitorTests
{
    [Fact]
    public async Task StopAsync_HidesCardStopsResolverAndAllowsRestart()
    {
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var cursor = new MutableCursor(new Point(20, 20));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(new RecordingCatalog([]), cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));
        await monitor.StopAsync();
        var reads = resolver.CallCount;
        Assert.False(presenter.IsVisible);
        Assert.Null(presenter.InfoTipTarget);
        Assert.Null(monitor.TryGetRecentTarget(new Point(20, 20)));
        await Task.Delay(350);
        Assert.Equal(reads, resolver.CallCount);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));
    }
    [Theory]
    [InlineData(0x0201)]
    [InlineData(0x0203)]
    [InlineData(0x0204)]
    [InlineData(0x0206)]
    [InlineData(0x0207)]
    [InlineData(0x0209)]
    public void HoverCardsWithoutQuickNotes_RetainsNativeClickSuppressionAndClearsConsumedGesture(int nativeDownMessage)
    {
        WpfTestHost.Invoke(() =>
        {
            var setEditing = typeof(FolderQuickEditMouseHook).GetMethod("SetEditingEnabled");
            Assert.NotNull(setEditing);
            var presenter = new RecordingPresenter();
            using var monitor = CreateMonitor(new RecordingCatalog([Project]), new MutableCursor(new Point(20, 20)),
                new MutableResolver(null), presenter);
            using var hook = new FolderQuickEditMouseHook(monitor, _ => throw new InvalidOperationException("Disabled shortcut opened an editor"),
                FolderEditChord.Default, System.Windows.Threading.Dispatcher.CurrentDispatcher);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var decision = (FolderQuickEditDecision)typeof(FolderQuickEditMouseHook).GetField("decision", flags)!.GetValue(hook)!;
            Assert.True(decision.TryBeginDeferredEdit(FolderMouseButton.Right, System.Windows.Input.ModifierKeys.Alt));
            setEditing.Invoke(hook, [false]);
            Assert.False(decision.OnMouseUp(FolderMouseButton.Right));
            presenter.Show(Project, new Rect(10, 10, 100, 50));
            var data = System.Runtime.InteropServices.Marshal.AllocHGlobal(32);
            try
            {
                for (var i = 0; i < 32; i++) System.Runtime.InteropServices.Marshal.WriteByte(data, i, 0);
                System.Runtime.InteropServices.Marshal.WriteInt32(data, 0, 20);
                System.Runtime.InteropServices.Marshal.WriteInt32(data, 4, 20);
                var onEvent = typeof(FolderQuickEditMouseHook).GetMethod("OnMouseEvent", flags)!;
                onEvent.Invoke(hook, [0, new nint(nativeDownMessage), data]);
                Assert.False(presenter.IsVisible);
                Assert.True(presenter.HideCount > 0);
                var nativeMouse = typeof(FolderHoverMonitor).GetField("nativeMouse", flags)!.GetValue(monitor)!;
                var isDragging = nativeMouse.GetType().GetMethod("IsDragging")!;
                Assert.Equal(true, isDragging.Invoke(nativeMouse, [new Point(40, 40)]));
                Assert.Equal(nint.Zero, onEvent.Invoke(hook, [0, new nint(0x0205), data]));
                Assert.Equal(false, isDragging.Invoke(nativeMouse, [new Point(40, 40)]));
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(data); }
        });
    }
    private static readonly FolderCategoryEntry Project = new(@"C:\Work\Project", "Work", "Current project", "#2868D7");

    [Fact]
    public async Task Poll_PreparesNativeInfoTipReplacementBeforeShowingCardAndClearsOnClick()
    {
        var cursor = new MutableCursor(new Point(20, 20));
        var target = new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50), new nint(456));
        var presenter = new RecordingPresenter();
        using var monitor = new FolderHoverMonitor(new RecordingCatalog([]), new MutableResolver(target), cursor,
            presenter, new ImmediateViewModelDispatcher(), rootAtPoint: _ => new nint(456));
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.InfoTipTarget is not null, TimeSpan.FromSeconds(2));
        Assert.False(presenter.IsVisible);
        Assert.Equal(target, presenter.InfoTipTarget);
        monitor.NotifyNativeMouseDown(new Point(20, 20));
        await WaitUntilAsync(() => presenter.InfoTipTarget is null, TimeSpan.FromSeconds(1));
        Assert.False(presenter.IsVisible);
    }

    [Fact]
    public async Task QuickEdit_ResolvesCapturedClickWithoutWaitingForHover()
    {
        var point = new Point(20, 20);
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50), new nint(456)));
        using var monitor = new FolderHoverMonitor(new RecordingCatalog([]), resolver, new MutableCursor(new Point(500, 500)),
            new RecordingPresenter(), new ImmediateViewModelDispatcher(), rootAtPoint: _ => new nint(456));
        var resolved = await monitor.ResolveQuickEditTargetAsync(point, new nint(456));
        Assert.Equal(Project.FolderPath, resolved?.FolderPath);
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public async Task QuickEdit_WindowChangedBeforeResolutionDoesNotEditAnotherFolder()
    {
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50), new nint(789)));
        using var monitor = new FolderHoverMonitor(new RecordingCatalog([]), resolver, new MutableCursor(new Point(20, 20)),
            new RecordingPresenter(), new ImmediateViewModelDispatcher(), rootAtPoint: _ => new nint(789));
        Assert.Null(await monitor.ResolveQuickEditTargetAsync(new Point(20, 20), new nint(456)));
        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task StartAsync_EmptyCatalogShowsLiveFolderCardAfterDwell()
    {
        var catalog = new RecordingCatalog([]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);

        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        Assert.True(resolver.CallCount > 0);
        Assert.Equal("Project", Assert.Single(presenter.Shown).Category);
        Assert.Equal(Project.FolderPath, monitor.TryGetRecentTarget(new Point(20, 20))?.FolderPath);
    }

    [Fact]
    public async Task StartAsync_UnreadableCatalogStillShowsLiveFolderCard()
    {
        var catalog = new RecordingCatalog([]) { ThrowOnLoad = true };
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);

        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        Assert.Equal("Project", presenter.Shown.Last().Category);
    }

    [Fact]
    public async Task StartAsync_OverCategorizedFolderShowsAfterDwell()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);

        await monitor.StartAsync();
        Assert.False(presenter.IsVisible);
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        Assert.Equal(Project, Assert.Single(presenter.Shown));
    }

    [Fact]
    public async Task Poll_LeavingFolderBoundsHidesCard()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        cursor.Position = new Point(300, 300);
        resolver.Target = null;
        await WaitUntilAsync(() => !presenter.IsVisible, TimeSpan.FromSeconds(2));

        Assert.True(presenter.HideCount > 0);
    }

    [Fact]
    public async Task Poll_WindowChangesUnderStationaryCursorHidesCard()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50), new nint(456)));
        var presenter = new RecordingPresenter();
        nint root = new nint(456);
        using var monitor = new FolderHoverMonitor(catalog, resolver, cursor, presenter,
            new ImmediateViewModelDispatcher(), rootAtPoint: _ => root);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        Assert.Null(monitor.TryGetRecentTarget(new Point(20, 20), nint.Zero));

        root = new nint(789);
        resolver.Target = null;
        await WaitUntilAsync(() => !presenter.IsVisible, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Poll_MovingInsideCategorizedFolderKeepsCardStationary()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        var initial = presenter.Position;
        cursor.Position = new Point(40, 30);
        await Task.Delay(400);

        Assert.Equal(initial, presenter.Position);
    }

    [Fact]
    public async Task ReloadCatalogAsync_RemovingVisibleCategoryShowsFolderName()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        catalog.Entries = [];
        await monitor.ReloadCatalogAsync();
        await WaitUntilAsync(() => presenter.Shown.Count >= 2, TimeSpan.FromSeconds(2));

        Assert.True(presenter.IsVisible);
        Assert.Equal("Project", presenter.Shown.Last().Category);
    }

    [Fact]
    public async Task Poll_PointerOverCardKeepsItVisibleWhileLeavingFolderIcon()
    {
        var catalog = new RecordingCatalog([]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter { CardBounds = new Rect(130, 10, 120, 100) };
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        cursor.Position = new Point(150, 30);
        resolver.Target = null;
        await Task.Delay(500);

        Assert.True(presenter.IsVisible);
    }

    [Fact]
    public async Task Poll_CrossingSmallGapToCardDoesNotHideIt()
    {
        var catalog = new RecordingCatalog([]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter { CardBounds = new Rect(130, 10, 120, 100) };
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        cursor.Position = new Point(120, 30);
        resolver.Target = null;
        await Task.Delay(180);
        cursor.Position = new Point(150, 30);
        await Task.Delay(350);

        Assert.True(presenter.IsVisible);
    }

    [Fact]
    public async Task Poll_ReturningFromCardToSameFolderKeepsCardVisible()
    {
        var catalog = new RecordingCatalog([]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter { CardBounds = new Rect(130, 10, 120, 100) };
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        cursor.Position = new Point(150, 30);
        await Task.Delay(180);
        cursor.Position = new Point(20, 20);
        await Task.Delay(180);

        Assert.True(presenter.IsVisible);
    }

    [Fact]
    public async Task NativeDoubleClick_HidesCardAndRestartsHoverAfterLastRelease()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        monitor.NotifyNativeMouseDown(new Point(20, 20));
        await WaitUntilAsync(() => !presenter.IsVisible, TimeSpan.FromSeconds(1));
        monitor.NotifyNativeMouseUp();
        await Task.Delay(180);
        monitor.NotifyNativeMouseDown(new Point(20, 20));
        monitor.NotifyNativeMouseUp();

        await Task.Delay(650);
        Assert.False(presenter.IsVisible);
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task NativeMouseHeldOnFolder_DoesNotShowHoverCard()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();

        monitor.NotifyNativeMouseDown(new Point(20, 20));
        await Task.Delay(900);
        Assert.False(presenter.IsVisible);
        monitor.NotifyNativeMouseUp();
    }

    [Fact]
    public async Task NativeFileDrag_CanStillRevealDropCard()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(0, 0));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();

        monitor.NotifyNativeMouseDown(new Point(0, 0));
        cursor.Position = new Point(20, 20);
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        Assert.True(presenter.DropInteractionEnabled);
        monitor.NotifyNativeMouseUp();
        await WaitUntilAsync(() => !presenter.IsVisible, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task StartAsync_IsIdempotentAndDisposeHidesTheCard()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        var monitor = CreateMonitor(catalog, cursor, resolver, presenter);

        await monitor.StartAsync();
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));
        monitor.Dispose();
        var callsAfterDispose = resolver.CallCount;
        await Task.Delay(400);

        Assert.False(presenter.IsVisible);
        Assert.Equal(callsAfterDispose, resolver.CallCount);
    }

    [Fact]
    public async Task Poll_ResolverFailureHidesCardAndKeepsMonitorAlive()
    {
        var catalog = new RecordingCatalog([Project]);
        var cursor = new MutableCursor(new Point(20, 20));
        var resolver = new MutableResolver(new ExplorerHoverTarget(Project.FolderPath, new Rect(10, 10, 100, 50)));
        var presenter = new RecordingPresenter();
        using var monitor = CreateMonitor(catalog, cursor, resolver, presenter);
        await monitor.StartAsync();
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));

        resolver.Throw = true;
        cursor.Position = new Point(300, 300);
        await WaitUntilAsync(() => !presenter.IsVisible, TimeSpan.FromSeconds(2));
        resolver.Throw = false;
        cursor.Position = new Point(20, 20);
        await WaitUntilAsync(() => presenter.IsVisible, TimeSpan.FromSeconds(2));
    }

    private static FolderHoverMonitor CreateMonitor(RecordingCatalog catalog, MutableCursor cursor, MutableResolver resolver, RecordingPresenter presenter) =>
        new(catalog, resolver, cursor, presenter, new ImmediateViewModelDispatcher());

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The expected hover-monitor state was not reached.");
            await Task.Delay(40);
        }
    }

    private sealed class RecordingCatalog(IReadOnlyList<FolderCategoryEntry> entries) : IFolderCategoryCatalog
    {
        internal IReadOnlyList<FolderCategoryEntry> Entries { get; set; } = entries;
        internal bool ThrowOnLoad { get; set; }
        public Task<IReadOnlyList<FolderCategoryEntry>> LoadAsync(CancellationToken token = default) =>
            ThrowOnLoad ? throw new IOException("Catalog unavailable") : Task.FromResult(Entries);
        public Task UpsertAsync(FolderCategoryEntry entry, CancellationToken token = default) => Task.CompletedTask;
        public Task RemoveAsync(string folderPath, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class MutableCursor(Point position) : ICursorPositionSource
    {
        private Point position = position;
        private readonly object sync = new();
        internal Point Position { get { lock (sync) return position; } set { lock (sync) position = value; } }
        public bool TryGetPosition(out Point screenPoint) { screenPoint = Position; return true; }
    }

    private sealed class MutableResolver(ExplorerHoverTarget? target) : IExplorerFolderHoverTargetResolver
    {
        private ExplorerHoverTarget? target = target;
        private int callCount;
        private int throwFlag;
        internal ExplorerHoverTarget? Target { get => Volatile.Read(ref target); set => Volatile.Write(ref target, value); }
        internal int CallCount => Volatile.Read(ref callCount);
        internal bool Throw { get => Volatile.Read(ref throwFlag) != 0; set => Volatile.Write(ref throwFlag, value ? 1 : 0); }
        public ExplorerHoverTarget? TryResolve(Point screenPoint)
        {
            Interlocked.Increment(ref callCount);
            if (Throw) throw new InvalidOperationException("UI Automation provider disconnected.");
            return Target;
        }
    }

    private sealed class RecordingPresenter : IFolderHoverCardPresenter
    {
        private ExplorerHoverTarget? infoTipTarget;
        internal ExplorerHoverTarget? InfoTipTarget => Volatile.Read(ref infoTipTarget);
        public void SetInfoTipTarget(ExplorerHoverTarget? target) => Volatile.Write(ref infoTipTarget, target);
        private readonly ConcurrentQueue<FolderCategoryEntry> shown = new();
        private int visible;
        private int hideCount;
        private int dropInteractionEnabled;
        private Rect position;
        internal IReadOnlyList<FolderCategoryEntry> Shown => shown.ToArray();
        internal bool IsVisible => Volatile.Read(ref visible) != 0;
        internal int HideCount => Volatile.Read(ref hideCount);
        internal bool DropInteractionEnabled => Volatile.Read(ref dropInteractionEnabled) != 0;
        internal Rect Position { get { lock (shown) return position; } }
        internal Rect CardBounds { get; set; }
        public bool IsPointerOverCard(Point point) => CardBounds.Contains(point);
        public void SetDropInteractionEnabled(bool enabled) =>
            Volatile.Write(ref dropInteractionEnabled, enabled ? 1 : 0);
        public void Show(FolderCategoryEntry entry, Rect itemBounds)
        {
            shown.Enqueue(entry);
            lock (shown) position = itemBounds;
            Volatile.Write(ref visible, 1);
        }
        public void Hide()
        {
            Interlocked.Increment(ref hideCount);
            Volatile.Write(ref visible, 0);
        }
    }
}
