using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Point = System.Windows.Point;

namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>Owns native pass-through styles and a read-only global mouse observer.</summary>
public sealed class IslandNativeInteraction : IDisposable
{
    private const long Transparent = 0x20, NoActivate = 0x08000000;
    private readonly HwndSource source;
    private readonly MouseHook callback;
    private nint mouseHook;
    private bool disposed, editing;
    public bool IsObserving => mouseHook != 0;
    public event Action<Point>? MouseMoved;

    public IslandNativeInteraction(HwndSource source)
    {
        this.source = source;
        callback = ObserveMouse;
        source.AddHook(WindowMessage);
    }

    /// <summary>Checks visual z-order without changing the native input target.</summary>
    public bool IsUnobscuredAt(Point screenPoint)
    {
        if (disposed) return false;
        // WindowFromPoint intentionally skips our transparent HWND. Walk visible
        // top-level z-order instead, ignoring other input-transparent overlays.
        var window = GetTopWindow(0);
        for (var count = 0; window != 0 && count < 512; count++, window = GetWindow(window, 2))
        {
            if (!IsWindowVisible(window) || !GetWindowRect(window, out var bounds) ||
                screenPoint.X < bounds.Left || screenPoint.X >= bounds.Right ||
                screenPoint.Y < bounds.Top || screenPoint.Y >= bounds.Bottom) continue;
            if (window == source.Handle) return true;
            if ((GetWindowLongPtr(window, -20).ToInt64() & Transparent) != 0) continue;
            if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            return false;
        }
        return false;
    }

    public void Configure(bool clickThrough, bool observeMouse, bool editMode = false)
    {
        if (disposed) return;
        editing = editMode;
        var style = GetWindowLongPtr(source.Handle, -20).ToInt64();
        style = editMode ? style & ~(Transparent | NoActivate) : (style | NoActivate) & ~Transparent;
        if (!editMode && clickThrough) style |= Transparent;
        SetWindowLongPtr(source.Handle, -20, new nint(style));
        // Editing must be reachable even if the previous click raised another
        // topmost window over the passive overlay. Normal updates preserve order.
        SetWindowPos(source.Handle, editMode ? new nint(-1) : 0, 0, 0, 0, 0, editMode ? 0x33u : 0x37u);
        if (observeMouse && !editMode && mouseHook == 0)
        {
            mouseHook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            if (mouseHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to observe Island hover.");
        }
        else if ((!observeMouse || editMode) && mouseHook != 0) StopObserving();
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21 && !editing) { handled = true; return 3; } // MA_NOACTIVATE
        return 0;
    }

    private nint ObserveMouse(int code, nint message, nint data)
    {
        // Never consume a click, double-click, wheel or movement from any process.
        if (code >= 0 && message == 0x200 && !disposed)
        {
            var location = Marshal.PtrToStructure<NativePoint>(data);
            try { MouseMoved?.Invoke(new Point(location.X, location.Y)); }
            catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception); }
        }
        return CallNextHookEx(mouseHook, code, message, data);
    }

    private void StopObserving()
    {
        var hook = mouseHook; mouseHook = 0;
        if (hook != 0) UnhookWindowsHookEx(hook);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; StopObserving(); source.RemoveHook(WindowMessage); MouseMoved = null;
    }

    private delegate nint MouseHook(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint parent);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect bounds);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, MouseHook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
}
