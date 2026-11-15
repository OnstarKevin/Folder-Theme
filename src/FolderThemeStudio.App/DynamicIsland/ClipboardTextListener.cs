using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FolderThemeStudio.App.DynamicIsland;

public static class ClipboardPreview
{
    /// <summary>按文本元素截取，避免在第 80 个字符处切断中文组合字或 emoji。</summary>
    public static string Create(string text)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        var starts = StringInfo.ParseCombiningCharacters(normalized);
        return starts.Length > 80 ? normalized[..starts[80]] + "…" : normalized;
    }
}

/// <summary>仅在用户开启剪贴板模块时注册监听；最新预览只保存在内存，关闭模块即清空。</summary>
public interface IIslandClipboardListener : IDisposable { void SetEnabled(bool value); }
public sealed class ClipboardTextListener : IIslandClipboardListener
{
    private readonly HwndSource source;
    private readonly Action<string> changed;
    private readonly DispatcherTimer retry = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool enabled;
    private int attempts;
    public ClipboardTextListener(HwndSource source, Action<string> changed)
    {
        this.source = source;
        this.changed = changed;
        source.AddHook(WindowMessage);
        retry.Tick += Retry_Tick;
    }
    public void SetEnabled(bool value)
    {
        if (value == enabled) return;
        retry.Stop();
        if (value)
        {
            if (!AddClipboardFormatListener(source.Handle)) { changed("剪贴板监听不可用"); return; }
            enabled = true;
            Read();
        }
        else
        {
            RemoveClipboardFormatListener(source.Handle);
            enabled = false;
            changed(string.Empty);
        }
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (enabled && message == 0x031D) { attempts = 0; Read(); } // WM_CLIPBOARDUPDATE
        return nint.Zero;
    }
    private void Retry_Tick(object? sender, EventArgs e) { retry.Stop(); Read(); }
    private void Read()
    {
        if (!enabled) return;
        try
        {
            changed(System.Windows.Clipboard.ContainsText() ? ClipboardPreview.Create(System.Windows.Clipboard.GetText()) : string.Empty);
        }
        catch (ExternalException)
        {
            // 其他应用可能短暂占用剪贴板。限次重试，不在消息回调里阻塞 UI。
            if (++attempts <= 3) retry.Start();
        }
    }
    public void Dispose()
    {
        retry.Stop();
        retry.Tick -= Retry_Tick;
        if (enabled) RemoveClipboardFormatListener(source.Handle);
        enabled = false;
        source.RemoveHook(WindowMessage);
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(nint hwnd);
}
