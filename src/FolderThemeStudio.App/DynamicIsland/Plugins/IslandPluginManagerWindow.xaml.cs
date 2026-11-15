using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace FolderThemeStudio.App.DynamicIsland.Plugins;

public partial class IslandPluginManagerWindow : Window
{
    private readonly IslandPluginManager manager;
    private readonly Func<bool> canRun;
    private readonly Func<Task> refreshData;
    private bool busy, closed;
    private string? listFingerprint;
    public IslandPluginManagerWindow(IslandPluginManager manager, Func<bool> canRun, Func<Task>? refreshData = null)
    {
        InitializeComponent(); this.manager = manager; this.canRun = canRun; this.refreshData = refreshData ?? (() => Task.CompletedTask);
        PromptBox.Text = IslandPluginGuide.AiPrompt;
        manager.ContentChanged += ContentChanged;
        Closed += (_, _) => { closed = true; manager.ContentChanged -= ContentChanged; };
        RefreshList();
    }
    private void ContentChanged(object? sender, IslandPluginContentChangedEventArgs e)
    {
        if (closed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!closed) RefreshList(); }));
    }
    private void RefreshList()
    {
        var list = manager.List();
        var fingerprint = string.Join("\n", list.Select(p => $"{p.Manifest.Id}\0{p.Manifest.Version}\0{p.Enabled}\0{p.Status}\0{string.Join(',', p.GrantedPermissions)}"));
        if (fingerprint == listFingerprint) return;
        listFingerprint = fingerprint;
        PluginList.ItemsSource = list.Select(p => new
        {
            Id = p.Manifest.Id, Name = p.Manifest.Name + " · " + p.Manifest.Version,
            Status = p.Status, Permissions = p.GrantedPermissions.Length == 0 ? "无额外数据权限" : string.Join(" · ", p.GrantedPermissions.Select(PermissionName)),
            ToggleLabel = p.Enabled ? "停用" : "启用",
        }).ToArray();
        EmptyText.Visibility = manager.List().Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static string PermissionName(string permission) => permission switch
    { "media.read" => "音乐信息", "clipboard.read" => "剪贴板文本", "notifications.read" => "系统通知内容", _ => permission };
    private async Task PerformAsync(Func<Task> action)
    {
        if (busy) return; busy = true; InstallButton.IsEnabled = false;
        try { await action(); if (!closed) RefreshList(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or KeyNotFoundException)
        { if (!closed) StatusText.Text = "未完成：" + e.Message; }
        finally { busy = false; if (!closed) { RefreshList(); InstallButton.IsEnabled = true; } }
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "安装灵动岛插件", Filter = "灵动岛插件包|*.islandpkg", Multiselect = false };
        if (picker.ShowDialog(this) == true) await PerformAsync(() => InstallAsync(picker.FileName));
    }
    private async Task InstallAsync(string path)
    {
        // Consent and installation must refer to the same bytes even if a download/sync replaces the source.
        var snapshot = Path.Combine(Path.GetTempPath(), "fts-plugin-" + Guid.NewGuid().ToString("N") + ".islandpkg");
        try
        {
            using (var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (original.Length > 8 * 1024 * 1024) throw new InvalidDataException("插件包超过 8 MiB。");
                using var copy = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await original.CopyToAsync(copy);
            }
            using var immutable = new FileStream(snapshot, FileMode.Open, FileAccess.Read, FileShare.Read);
            await InstallSnapshotAsync(snapshot);
        }
        finally { if (File.Exists(snapshot)) File.Delete(snapshot); }
    }
    private async Task InstallSnapshotAsync(string path)
    {
        var manifest = manager.InspectPackage(path);
        var previous = manager.List().FirstOrDefault(p => p.Manifest.Id == manifest.Id);
        var added = manifest.Permissions.Except(previous?.GrantedPermissions ?? []).ToArray();
        if (added.Length > 0 && MessageBox.Show(this, $"“{manifest.Name}”需要读取：{string.Join("、", added.Select(PermissionName))}。\n仅在对应宿主模块开启时提供数据。是否授权安装？", "插件数据权限", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await manager.InstallAsync(path, manifest.Permissions);
        if (!canRun()) await manager.StopAsync();
        await manager.EnableAsync(manifest.Id, true);
        if (canRun()) await refreshData();
        StatusText.Text = canRun() ? "插件已安装并启用。" : "插件已安装；开启灵动岛后运行。";
    }
    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        await PerformAsync(async () => { var plugin = manager.List().FirstOrDefault(p => p.Manifest.Id == id); if (plugin is null) return; if (!canRun()) await manager.StopAsync(); await manager.EnableAsync(id, !plugin.Enabled); if (!plugin.Enabled && canRun()) await refreshData(); StatusText.Text = plugin.Enabled ? "插件已停用，数据订阅与内容已清除。" : canRun() ? "插件已启用。" : "开启灵动岛后运行。"; });
    }
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await PerformAsync(async () => { await manager.UninstallAsync(id); StatusText.Text = "插件已移除。"; });
    }
    private async void Example_Click(object sender, RoutedEventArgs e)
    {
        await PerformAsync(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "fts-focus-" + Guid.NewGuid().ToString("N") + ".islandpkg");
            try { WriteExample(path); await InstallAsync(path); }
            finally { if (File.Exists(path)) File.Delete(path); }
        });
    }
    public static void WriteExample(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write(JsonSerializer.Serialize(new IslandPluginManifest { Id = "studio.focus-timer", Name = "专注倒计时", Description = "25 分钟轻量专注计时", Version = "1.0.0", ApiVersion = 1, MinHostVersion = "0.1.0-beta.23", Entry = "main.js" }, PluginJson.Options));
        using var script = new StreamWriter(zip.CreateEntry("main.js").Open()); script.Write(IslandPluginGuide.ExampleScript);
    }
    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(PromptBox.Text); StatusText.Text = "已复制 AI 提示词。"; }
        catch (System.Runtime.InteropServices.ExternalException) { StatusText.Text = "剪贴板被占用，请稍后重试。"; }
    }
    private void ExportSdk_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog { Title = "导出插件 SDK", FileName = "Island-Plugin-API-v1.zip", Filter = "ZIP 文件|*.zip" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            using var zip = new ZipArchive(File.Create(picker.FileName), ZipArchiveMode.Create);
            using (var guide = new StreamWriter(zip.CreateEntry("AI-PROMPT.zh-CN.txt").Open())) guide.Write(IslandPluginGuide.AiPrompt);
            using (var script = new StreamWriter(zip.CreateEntry("example/main.js").Open())) script.Write(IslandPluginGuide.ExampleScript);
            using (var manifest = new StreamWriter(zip.CreateEntry("example/manifest.json").Open())) manifest.Write("""{"id":"studio.focus-timer","name":"专注倒计时","version":"1.0.0","apiVersion":1,"minHostVersion":"0.1.0-beta.23","entry":"main.js","permissions":[]}""");
            StatusText.Text = "SDK 已导出。将 example 内的两个文件放在 ZIP 根目录并改名为 .islandpkg 即可安装。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = ex.Message; }
    }
    private void Package_DragOver(object sender, System.Windows.DragEventArgs e) { e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None; e.Handled = true; }
    private async void Package_Drop(object sender, System.Windows.DragEventArgs e)
    { if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] { Length: 1 } files && Path.GetExtension(files[0]).Equals(".islandpkg", StringComparison.OrdinalIgnoreCase)) await PerformAsync(() => InstallAsync(files[0])); else StatusText.Text = "请选择一个 .islandpkg 插件包。"; }
}
