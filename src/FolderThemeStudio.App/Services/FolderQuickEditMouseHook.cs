using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace FolderThemeStudio.App.Services;

public sealed class FolderQuickEditMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLeftDown = 0x0201;
    private const int WmLeftUp = 0x0202;
    private const int WmLeftDoubleClick = 0x0203;
    private const int WmRightDown = 0x0204;
    private const int WmRightUp = 0x0205;
    private const int WmRightDoubleClick = 0x0206;
    private const int WmMiddleDown = 0x0207;
    private const int WmMiddleUp = 0x0208;
    private const int WmMiddleDoubleClick = 0x0209;
    private const int GaRoot = 2;
    private readonly FolderHoverMonitor monitor;
    private readonly Action<string> openEditor;
    private readonly Dispatcher dispatcher;
    private readonly FolderQuickEditDecision decision;
    private readonly HookProc callback;
    private nint handle;
    private int latestEditRequest;
    private bool editingEnabled = true;

    public static bool IsRequired(bool hoverCardsEnabled, bool quickNotesEnabled) => hoverCardsEnabled || quickNotesEnabled;

    public void SetEditingEnabled(bool enabled)
    {
        editingEnabled = enabled;
        if (enabled) return;
        ++latestEditRequest;
        decision.OnMouseUp(FolderMouseButton.Right);
        decision.OnMouseUp(FolderMouseButton.Middle);
    }

    public FolderQuickEditMouseHook(FolderHoverMonitor monitor, Action<string> openEditor, FolderEditChord chord, Dispatcher dispatcher)
    {
        this.monitor = monitor;
        this.openEditor = openEditor;
        this.dispatcher = dispatcher;
        decision = new FolderQuickEditDecision(chord);
        callback = OnMouseEvent;
        handle = SetWindowsHookEx(WhMouseLl, callback, GetModuleHandle(null), 0);
        if (handle == nint.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install folder quick-edit mouse hook.");
    }

    public void UpdateChord(FolderEditChord chord) => decision.UpdateChord(chord);

    public void Dispose()
    {
        SetEditingEnabled(false);
        if (handle == nint.Zero) return;
        UnhookWindowsHookEx(handle);
        handle = nint.Zero;
    }

    private nint OnMouseEvent(int code, nint message, nint data)
    {
        if (code < 0 || handle == nint.Zero) return CallNextHookEx(handle, code, message, data);
        var kind = message.ToInt32();
        if (kind is WmLeftUp or WmRightUp or WmMiddleUp)
        {
            monitor.NotifyNativeMouseUp();
            if (editingEnabled && kind is WmRightUp or WmMiddleUp &&
                decision.OnMouseUp(kind == WmRightUp ? FolderMouseButton.Right : FolderMouseButton.Middle))
                return new nint(1);
            return CallNextHookEx(handle, code, message, data);
        }
        if (kind is not (WmLeftDown or WmLeftDoubleClick or WmRightDown or WmRightDoubleClick or WmMiddleDown or WmMiddleDoubleClick))
            return CallNextHookEx(handle, code, message, data);

        var mouse = Marshal.PtrToStructure<MouseHookData>(data);
        var point = new Point(mouse.Position.X, mouse.Position.Y);
        if (kind is WmLeftDown or WmLeftDoubleClick)
        {
            monitor.NotifyNativeMouseDown(point);
            return CallNextHookEx(handle, code, message, data);
        }

        var button = kind is WmRightDown or WmRightDoubleClick ? FolderMouseButton.Right : FolderMouseButton.Middle;
        monitor.NotifyNativeMouseDown(point);
        if (!editingEnabled) return CallNextHookEx(handle, code, message, data);
        var root = GetAncestor(WindowFromPoint(mouse.Position), GaRoot);
        if (!IsExplorerWindow(root) || !decision.TryBeginDeferredEdit(button, CurrentModifiers()))
            return CallNextHookEx(handle, code, message, data);
        var request = ++latestEditRequest;
        _ = dispatcher.BeginInvoke(() => _ = OpenResolvedEditorAsync(point, root, request));
        return new nint(1);
    }

    private async Task OpenResolvedEditorAsync(Point point, nint root, int request)
    {
        if (!editingEnabled || handle == nint.Zero || request != latestEditRequest) return;
        var target = await monitor.ResolveQuickEditTargetAsync(point, root);
        if (editingEnabled && handle != nint.Zero && request == latestEditRequest && target is not null) openEditor(target.FolderPath);
    }

    private static bool IsExplorerWindow(nint window)
    {
        var name = new StringBuilder(128);
        return GetClassName(window, name, name.Capacity) > 0 &&
            name.ToString() is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";
    }

    private static ModifierKeys CurrentModifiers()
    {
        var result = ModifierKeys.None;
        if (IsDown(0x12)) result |= ModifierKeys.Alt;
        if (IsDown(0x11)) result |= ModifierKeys.Control;
        if (IsDown(0x10)) result |= ModifierKeys.Shift;
        if (IsDown(0x5B) || IsDown(0x5C)) result |= ModifierKeys.Windows;
        return result;
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private delegate nint HookProc(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public NativePoint Position;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hook, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int maximumCount);
}
