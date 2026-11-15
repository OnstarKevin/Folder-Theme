using System.Windows;
using FolderThemeStudio.App.Services;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class ExplorerInfoTipSuppressorTests
{
    private static readonly ExplorerHoverTarget Target = new(@"C:\Work\Folder", new Rect(10, 10, 80, 60), new nint(42));

    [Fact]
    public void DismissesExistingAndLaterTipsOnlyForHoveredFolder()
    {
        var platform = new TestPlatform();
        using var suppressor = new ExplorerInfoTipSuppressor(platform);
        suppressor.SetTarget(Target);
        Assert.Equal([new nint(7)], platform.Dismissed);
        platform.OnShown!(new nint(8)); // unrelated tooltip in another window
        Assert.Single(platform.Dismissed);
        platform.OnShown(new nint(7));
        Assert.Equal(2, platform.Dismissed.Count);
        platform.PointerOnFolder = false; // user moved into a toolbar before the next poll
        platform.OnShown(new nint(7));
        Assert.Equal(2, platform.Dismissed.Count);
    }

    [Fact]
    public void LeavingFolderOrDisposingStopsSuppression()
    {
        var platform = new TestPlatform();
        var suppressor = new ExplorerInfoTipSuppressor(platform);
        suppressor.SetTarget(Target);
        suppressor.SetTarget(null);
        platform.OnShown!(new nint(7));
        Assert.Single(platform.Dismissed);
        suppressor.Dispose();
        Assert.True(platform.Unsubscribed);
        suppressor.SetTarget(Target);
        platform.OnShown(new nint(7));
        Assert.Single(platform.Dismissed);
    }

    [Fact]
    public void UnverifiedRootDoesNotSuppressSystemTips()
    {
        var platform = new TestPlatform();
        using var suppressor = new ExplorerInfoTipSuppressor(platform);
        suppressor.SetTarget(Target with { RootWindowHandle = nint.Zero });
        platform.OnShown!(new nint(7));
        Assert.Empty(platform.Dismissed);
    }

    private sealed class TestPlatform : IExplorerInfoTipPlatform
    {
        internal Action<nint>? OnShown;
        internal List<nint> Dismissed = [];
        internal bool PointerOnFolder = true;
        internal bool Unsubscribed;
        public IDisposable WatchShown(Action<nint> callback) { OnShown = callback; return new Subscription(() => Unsubscribed = true); }
        public IEnumerable<nint> FindInfoTips(nint root) => [new nint(7), new nint(8)];
        public bool IsPointerOnTarget(ExplorerHoverTarget target) => PointerOnFolder;
        public bool BelongsToFolderView(nint tip, nint root) => tip == new nint(7) && root == new nint(42);
        public void Dismiss(nint tip) => Dismissed.Add(tip);
        private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}
