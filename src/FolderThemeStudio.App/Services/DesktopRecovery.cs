using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FolderThemeStudio.App.Services;

public sealed record DesktopRecoveryView(nint IconList, IReadOnlyList<nint> LegacyHosts);

public interface IDesktopRecoveryPlatform
{
    IReadOnlyList<DesktopRecoveryView> FindDesktopViews();
    bool RequestLegacyHostStop(nint host);
    bool ShowNativeIcons(nint list);
}

/// <summary>One-time recovery from the retired desktop layers; never manages desktop files or input.</summary>
public sealed class DesktopRecovery(IDesktopRecoveryPlatform? platform = null, string? settingsPath = null)
{
    private readonly IDesktopRecoveryPlatform platform = platform ?? new WindowsDesktopRecoveryPlatform();
    private readonly string settingsPath = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderThemeStudio", "desktop-organizer.json");

    public void Restore(bool force = false)
    {
        JsonObject? legacy = null;
        try
        {
            if (File.Exists(settingsPath)) legacy = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        var wasEnabled = legacy?["Enabled"] is JsonValue value && value.TryGetValue<bool>(out var enabled) && enabled;
        var restored = false;
        var succeeded = true;
        foreach (var view in platform.FindDesktopViews())
        {
            if (!force && !wasEnabled && view.LegacyHosts.Count == 0) continue;
            foreach (var host in view.LegacyHosts) succeeded &= platform.RequestLegacyHostStop(host);
            succeeded &= platform.ShowNativeIcons(view.IconList);
            restored = true;
        }
        // Preserve the complete old document, but stop a stale enabled setting from forcing visibility on later starts.
        if (!restored || !succeeded || !wasEnabled) return;
        try
        {
            legacy!["Enabled"] = false;
            File.WriteAllText(settingsPath + ".recovery.tmp", legacy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(settingsPath + ".recovery.tmp", settingsPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

internal sealed class WindowsDesktopRecoveryPlatform : IDesktopRecoveryPlatform
{
    private static readonly uint RestoreMessage = RegisterWindowMessage("FolderThemeStudio.RestoreDesktopLayers");

    public IReadOnlyList<DesktopRecoveryView> FindDesktopViews()
    {
        var result = new List<DesktopRecoveryView>();
        GetWindowThreadProcessId(GetShellWindow(), out var shellProcess);
        if (shellProcess == 0) return result;
        EnumWindows((top, _) =>
        {
            GetWindowThreadProcessId(top, out var owner);
            if (owner != shellProcess) return true;
            var view = FindWindowEx(top, 0, "SHELLDLL_DefView", null);
            var list = view == 0 ? 0 : FindWindowEx(view, 0, "SysListView32", null);
            if (list == 0) return true;
            var hosts = new List<nint>();
            EnumChildWindows(view, (child, _) =>
            {
                var title = new StringBuilder(128);
                GetWindowText(child, title, title.Capacity);
                if (title.ToString() == "FolderThemeStudio.DesktopLayers") hosts.Add(child);
                return true;
            }, 0);
            result.Add(new(list, hosts));
            return true;
        }, 0);
        return result;
    }

    public bool RequestLegacyHostStop(nint host) => RestoreMessage != 0 && PostMessage(host, RestoreMessage, 0, 0);
    public bool ShowNativeIcons(nint list) => SetWindowPos(list, 0, 0, 0, 0, 0,
        0x1 | 0x2 | 0x4 | 0x10 | 0x40 | 0x4000); // no move, size, z-order or focus change; show asynchronously

    private delegate bool EnumProc(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumProc callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint handle, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
}
