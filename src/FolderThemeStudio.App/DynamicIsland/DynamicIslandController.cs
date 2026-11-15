using System.IO;
using System.Windows.Threading;
using FolderThemeStudio.App.DynamicIsland.Plugins;

namespace FolderThemeStudio.App.DynamicIsland;

public interface IDynamicIslandController : IDisposable
{
    Task ShowAsync();
    Task SetFeatureEnabledAsync(bool enabled) => Task.CompletedTask;
    Task ConfigureAsync() => Task.CompletedTask;
    Task EditPositionAsync() => Task.CompletedTask;
    void SetSettingsNavigator(Func<string, Task> navigate) { }
    Task<DynamicIslandSettingsView?> CreateSettingsViewAsync() => Task.FromResult<DynamicIslandSettingsView?>(null);
    Task<IslandPluginManagerView?> CreatePluginViewAsync() => Task.FromResult<IslandPluginManagerView?>(null);
}

/// <summary>全应用仅创建一个灵动岛；位置/透明度合并保存，退出软件保留 Enabled，下次启动恢复。</summary>
public sealed class DynamicIslandController : IDynamicIslandController
{
    private readonly IIslandSettingsStore store;
    private readonly Func<bool> startup;
    private readonly Func<bool, Task> setStartup;
    private readonly Action exit;
    private readonly Func<IslandSettings, DynamicIslandWindow> createWindow;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private IslandSettings settings = new();
    private DynamicIslandWindow? window;
    private Func<string, Task>? navigateSettings;
    private bool loaded, disposed, applyingSettings, closingIsland;
    private IslandSettings? pendingClose;
    private bool featureEnabled = true;
    private IslandPluginManager? plugins;

    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Func<IslandPluginManager> createPlugins;
    public async Task EditPositionAsync()
    {
        await ShowAsync();
        if (window is null) return;
        window.SetEditMode(true);
    }
    private IslandPluginManager EnsurePlugins()
    {
        if (plugins is not null) return plugins;
        plugins = createPlugins(); plugins.ContentChanged += PluginContentChanged;
        return plugins;
    }
    private void PluginContentChanged(object? sender, IslandPluginContentChangedEventArgs e)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        var target = window;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (disposed || window is null || window != target) return;
            if (e.Content is null) window.Model.ClearExtensionContent(e.PluginId);
            else window.Model.SetExtensionContent(e.PluginId, string.IsNullOrWhiteSpace(e.Content.Icon) ? e.Content.Text : e.Content.Icon + " " + e.Content.Text,
                e.Content.Details, Math.Clamp((e.Content.TtlMs + 999) / 1000, 1, 60));
        }));
    }
    private async void ModelHookEvent(object? sender, IslandHookEvent e)
    {
        if (window is null || sender != window.Model) return;
        try { if (!disposed && plugins is not null) await plugins.SendEventAsync(e.Name, e.Payload); }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
    public void SetSettingsNavigator(Func<string, Task> navigate) => navigateSettings = navigate;
    public Task ConfigureAsync() => disposed ? Task.CompletedTask : navigateSettings?.Invoke("island") ?? Task.CompletedTask;
    private async void OpenSettings()
    {
        try { await ConfigureAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { window?.Model.ReportStatus("设置暂不可用：" + ex.Message); }
    }
    public async Task<IslandPluginManagerView?> CreatePluginViewAsync()
    {
        if (disposed) return null;
        var manager = EnsurePlugins();
        if (window is null) await manager.StopAsync();
        return disposed ? null : new IslandPluginManagerView(manager, () => !disposed && window is not null && featureEnabled, ReplayPluginDataAsync);
    }
    public async Task<DynamicIslandSettingsView?> CreateSettingsViewAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return null;
            if (!loaded) { settings = await store.LoadAsync(); loaded = true; }
            if (disposed) return null;
            var draft = window?.CaptureSettings() ?? settings;
            var view = new DynamicIslandSettingsView(draft, startup(), SaveInlineSettingsAsync,
                () => window?.Model.RequestNotificationAccessAsync() ?? Task.FromResult("请先启用灵动岛，再请求系统通知访问权限。"));
            view.ChangeVisibility = async visible =>
            {
                if (visible && !featureEnabled) throw new InvalidOperationException("请先在功能开关中启用灵动岛。");
                if (visible) await ShowAsync(); else await HideAsync();
            };
            view.PluginsRequested += async (_, _) => { if (navigateSettings is not null) await navigateSettings("plugins"); };
            view.EditPositionRequested += async (_, _) =>
            {
                try
                {
                    if (!featureEnabled) { view.ReportStatus("请先在功能开关中启用灵动岛。", true); return; }
                    await EditPositionAsync();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                { view.ReportStatus("无法编辑位置：" + ex.Message, true); }
            };
            return view;
        }
        finally { gate.Release(); }
    }
    private async Task SaveInlineSettingsAsync(IslandSettings next, bool unusedStartup)
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) throw new InvalidOperationException("应用正在退出。");
            var live = window?.CaptureSettings() ?? settings;
            // The page owns module options; runtime owns position and visibility.
            next = next with { Enabled = live.Enabled, Left = live.Left, Top = live.Top };
            applyingSettings = true; debounce.Stop();
            try
            {
                await store.SaveAsync(next);
                settings = next;
                window?.ApplySettings(next);
            }
            finally { applyingSettings = false; }
        }
        finally { gate.Release(); }
    }
    private async Task ReplayPluginDataAsync()
    {
        if (disposed || window is null || plugins is null) return;
        foreach (var item in window.Model.GetHookSnapshot()) await plugins.SendEventAsync(item.Name, item.Payload);
    }
    public async Task SetFeatureEnabledAsync(bool enabled)
    {
        await gate.WaitAsync();
        try
        {
            featureEnabled = enabled;
            if (!enabled)
            {
                debounce.Stop();
                var saveSnapshot = window is not null;
                if (window is not null)
                {
                    settings = window.CaptureSettings();
                }


                if (plugins is not null) await plugins.StopAsync();
                window?.CloseFromController();
                window = null;
                if (saveSnapshot) await store.SaveAsync(settings);
            }
        }
        finally { gate.Release(); }
        if (enabled) await RestoreAsync();
    }
    public DynamicIslandController(IIslandSettingsStore store, Func<bool> startup, Func<bool, Task> setStartup, Action exit,
        Func<IslandSettings, DynamicIslandWindow>? createWindow = null, Func<IslandPluginManager>? createPlugins = null)
    {
        this.store = store; this.startup = startup; this.setStartup = setStartup; this.exit = exit;
        this.createWindow = createWindow ?? (settings => new DynamicIslandWindow(settings));
        this.createPlugins = createPlugins ?? (() => new IslandPluginManager());
        debounce.Tick += SaveDebounced_Tick;
    }
    public async Task RestoreAsync()
    {
        settings = await store.LoadAsync(); loaded = true;
        if (settings.Enabled) await ShowAsync();
    }
    public async Task ShowAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed || !featureEnabled) return;
            if (!loaded) { settings = await store.LoadAsync(); loaded = true; }
            if (disposed) return;
            if (window is not null) { window.Show(); return; }
            settings = settings with { Enabled = true };
            await store.SaveAsync(settings);
            if (disposed) return;
            window = createWindow(settings);
            window.Model.HookEvent += ModelHookEvent;
            window.SnapshotChanged += (_, _) => { if (!applyingSettings) { debounce.Stop(); debounce.Start(); } };
            window.SettingsRequested += (_, _) => OpenSettings();
            window.CloseRequested += async (_, _) => await HideAsync();
            window.ExitRequested += (_, _) => exit();
            window.Show();
            try { await EnsurePlugins().StartEnabledAsync(); await ReplayPluginDataAsync(); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ObjectDisposedException) { window?.Model.ReportStatus("插件目录暂不可用"); }
        }
        finally { gate.Release(); }
    }
    private async void SaveDebounced_Tick(object? sender, EventArgs e)
    {
        debounce.Stop();
        var savingWindow = window;
        await gate.WaitAsync();
        try
        {
            if (savingWindow is null || window != savingWindow || disposed || applyingSettings) return;
            settings = savingWindow.CaptureSettings();
            try { await store.SaveAsync(settings); window?.Model.ReportStatus(string.Empty); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { window?.Model.ReportStatus("位置保存失败，请检查配置目录权限"); }
        }
        finally { gate.Release(); }
    }
    private async Task HideAsync()
    {
        // 与打开/设置保存共用生命周期锁，关闭开始后不能被待处理的设置写回 Enabled=true。
        await gate.WaitAsync();
        try
        {
            debounce.Stop();
            if (window is null || disposed) return;
            var closing = window;
            var next = closing.CaptureSettings() with { Enabled = false };
            closingIsland = true; pendingClose = next;
            try { await store.SaveAsync(next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { pendingClose = null; closingIsland = false; if (!disposed) closing.Model.ReportStatus("关闭设置保存失败，请重试"); return; }
            if (window != closing || disposed) return;
            settings = next;


            if (plugins is not null) await plugins.StopAsync();
            if (disposed || window != closing) return;
            closing.CloseFromController(); window = null;
        }
        finally { pendingClose = null; closingIsland = false; gate.Release(); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        debounce.Stop(); debounce.Tick -= SaveDebounced_Tick;

        if (plugins is not null) { plugins.ContentChanged -= PluginContentChanged; plugins.Dispose(); plugins = null; }
        if (pendingClose is not null) settings = pendingClose;
        else if (window is not null && !closingIsland) settings = window.CaptureSettings();
        window?.CloseFromController(); window = null;
        // Store 的 I/O 使用 ConfigureAwait(false)，退出时可安全同步刷新最后的位置。
        if (loaded) try { store.SaveAsync(settings).GetAwaiter().GetResult(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
