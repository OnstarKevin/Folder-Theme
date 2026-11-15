using System.Runtime.InteropServices;
using System.Text;
using Point = System.Windows.Point;

namespace FolderThemeStudio.App.Services;

public interface IExplorerInfoTipPlatform
{
    IDisposable WatchShown(Action<nint> onShown);
    IEnumerable<nint> FindInfoTips(nint root);
    bool IsPointerOnTarget(ExplorerHoverTarget target);
    bool BelongsToFolderView(nint infoTip, nint root);
    void Dismiss(nint infoTip);
}

// Dismiss only the current folder's transient tip; never change Explorer's persistent settings.
public sealed class ExplorerInfoTipSuppressor : IDisposable
{
    private readonly IExplorerInfoTipPlatform platform;
    private readonly IDisposable subscription;
    private ExplorerHoverTarget? target;
    private bool disposed;

    public ExplorerInfoTipSuppressor(IExplorerInfoTipPlatform? platform = null)
    {
        this.platform = platform ?? new WindowsExplorerInfoTipPlatform();
        subscription = this.platform.WatchShown(TryDismiss);
    }

    public void SetTarget(ExplorerHoverTarget? next)
    {
        if (disposed) return;
        target = next;
        if (next is null || next.RootWindowHandle == nint.Zero) return;
        foreach (var tip in platform.FindInfoTips(next.RootWindowHandle)) TryDismiss(tip);
    }

    private void TryDismiss(nint tip)
    {
        var current = target;
        if (!disposed && current is not null && current.RootWindowHandle != nint.Zero &&
            platform.IsPointerOnTarget(current) && platform.BelongsToFolderView(tip, current.RootWindowHandle))
            platform.Dismiss(tip);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        target = null;
        subscription.Dispose();
    }
}

internal sealed class WindowsExplorerInfoTipPlatform : IExplorerInfoTipPlatform
{
    public IDisposable WatchShown(Action<nint> onShown) => new ShowSubscription(onShown);

    public IEnumerable<nint> FindInfoTips(nint root)
    {
        var result = new HashSet<nint>();
        var thread = Native.GetWindowThreadProcessId(root, out _);
        if (thread == 0) return result;
        Native.EnumProc collect = (window, _) => { if (ClassName(window) == "tooltips_class32") result.Add(window); return true; };
        Native.EnumThreadWindows(thread, collect, nint.Zero);
        Native.EnumChildWindows(root, collect, nint.Zero);
        return result;
    }

    public bool IsPointerOnTarget(ExplorerHoverTarget target) =>
        Native.GetCursorPos(out var point) && target.ItemBounds.Contains(new Point(point.X, point.Y)) &&
        Native.GetAncestor(Native.WindowFromPoint(point), 2) == target.RootWindowHandle;

    public bool BelongsToFolderView(nint tip, nint root)
    {
        if (ClassName(tip) != "tooltips_class32" ||
            ClassName(root) is not ("CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW")) return false;
        Native.GetWindowThreadProcessId(root, out var rootProcess);
        Native.GetWindowThreadProcessId(tip, out var tipProcess);
        if (rootProcess == 0 || tipProcess != rootProcess) return false;
        // Tooltip owners are normally the shell list view or its containing view window.
        var owner = Native.GetWindow(tip, 4);
        if (owner == nint.Zero) owner = Native.GetParent(tip);
        return owner != nint.Zero && (owner == root || Native.GetAncestor(owner, 2) == root);
    }

    public void Dismiss(nint tip)
    {
        // TTM_POP has no pointer parameters and is safe across process boundaries.
        Native.SendMessageTimeout(tip, 0x0400 + 28, nint.Zero, nint.Zero, 0x0002 | 0x0020, 30, out _);
    }

    private static string ClassName(nint window)
    {
        var name = new StringBuilder(128);
        return Native.GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
    }

    private sealed class ShowSubscription : IDisposable
    {
        private readonly Native.WinEventProc callback;
        private GCHandle callbackRoot;
        private nint hook;
        public ShowSubscription(Action<nint> onShown)
        {
            callback = (_, _, window, objectId, childId, _, _) =>
            {
                if (objectId != 0 || childId != 0 || window == nint.Zero) return;
                try { onShown(window); } catch (Exception) { }
            };
            callbackRoot = GCHandle.Alloc(callback);
            hook = Native.SetWinEventHook(0x8002, 0x8002, nint.Zero, callback, 0, 0, 2);
            if (hook == nint.Zero) callbackRoot.Free();
        }
        public void Dispose()
        {
            if (hook == nint.Zero) return;
            Native.UnhookWinEvent(hook);
            hook = nint.Zero;
            if (callbackRoot.IsAllocated) callbackRoot.Free();
        }
    }

    private static class Native
    {
        internal delegate bool EnumProc(nint window, nint param);
        internal delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { internal int X; internal int Y; }
        [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWinEvent(nint hook);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumThreadWindows(uint thread, EnumProc callback, nint param);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumChildWindows(nint parent, EnumProc callback, nint param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder name, int count);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
        [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
        [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] internal static extern nint WindowFromPoint(NativePoint point);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] internal static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    }
}
