using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace FolderThemeStudio.App.DynamicIsland.Plugins;

public partial class IslandPluginManagerView : System.Windows.Controls.UserControl
{
    private readonly IslandPluginManager manager;
    private readonly Func<bool> canRun;
    private readonly Func<Task> refreshData;
    private bool busy, closed;
    private bool subscribed;
    private volatile bool active;
    public bool IsBusy => busy;
    private TaskCompletionSource<bool>? consent;
    private string? editingId;
    private readonly Dictionary<string, (string Type, FrameworkElement Control)> settingControls = new(StringComparer.Ordinal);
    public void Release()
    {
        closed = true; active = false;
        if (subscribed) { manager.ContentChanged -= ContentChanged; subscribed = false; }
        consent?.TrySetResult(false);
    }
    private Task<bool> ConfirmPermissionsAsync(string text)
    {
        ConsentText.Text = text; ConsentPanel.Visibility = Visibility.Visible;
        consent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return consent.Task;
    }
    private void Approve_Click(object sender, RoutedEventArgs e) { ConsentPanel.Visibility = Visibility.Collapsed; consent?.TrySetResult(true); }
    private void Decline_Click(object sender, RoutedEventArgs e) { ConsentPanel.Visibility = Visibility.Collapsed; consent?.TrySetResult(false); }
    private string? listFingerprint;
    public IslandPluginManagerView(IslandPluginManager manager, Func<bool> canRun, Func<Task>? refreshData = null)
    {
        InitializeComponent(); this.manager = manager; this.canRun = canRun; this.refreshData = refreshData ?? (() => Task.CompletedTask);
        PromptBox.Text = IslandPluginGuide.AiPrompt;
        IsVisibleChanged += (_, _) => UpdateSubscription();

        RefreshList();
    }
    private void UpdateSubscription()
    {
        active = !closed && IsVisible;
        if (active && !subscribed) { manager.ContentChanged += ContentChanged; subscribed = true; }
        else if (!active && subscribed) { manager.ContentChanged -= ContentChanged; subscribed = false; }
        if (active) RefreshList();
    }
    private void ContentChanged(object? sender, IslandPluginContentChangedEventArgs e)
    {
        if (closed || !active || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!closed && active) RefreshList(); }));
    }
    private void RefreshList()
    {
        var list = manager.List();
        var fingerprint = string.Join("\n", list.Select(p => $"{p.Manifest.Id}\0{p.Manifest.Name}\0{p.Manifest.Version}\0{p.Enabled}\0{p.Status}\0{string.Join(',', p.GrantedPermissions)}\0{PluginJson.Serialize(p.Manifest.Settings)}"));
        if (fingerprint == listFingerprint) return;
        listFingerprint = fingerprint;
        PluginList.ItemsSource = list.Select(p => new
        {
            Id = p.Manifest.Id, Name = p.Manifest.Name + " · " + p.Manifest.Version,
            Status = p.Status, Permissions = p.GrantedPermissions.Length == 0 ? "无额外数据权限" : string.Join(" · ", p.GrantedPermissions.Select(PermissionName)),
            ToggleLabel = p.Enabled ? "停用" : "启用",
            HasSettings = p.Manifest.Settings.Length > 0,
        }).ToArray();
        EmptyText.Visibility = manager.List().Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static string PermissionName(string permission) => permission switch
    { "media.read" => "音乐信息", "clipboard.read" => "剪贴板文本", "notifications.read" => "系统通知内容", _ => permission };
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not FrameworkElement { Tag: string id }) return;
        if (editingId is not null && editingId != id) { SettingsError.Text = "请先保存或取消当前插件的设置。"; return; }
        var plugin = manager.List().FirstOrDefault(p => p.Manifest.Id == id);
        if (plugin is null) return;
        editingId = id; SettingsFields.Children.Clear(); settingControls.Clear(); SettingsError.Text = "";
        SettingsTitle.Text = plugin.Manifest.Name + " · 设置";
        var values = manager.GetSettings(id);
        foreach (var definition in plugin.Manifest.Settings)
        {
            var value = values[definition.Key];
            FrameworkElement control;
            if (definition.Type == "boolean")
                control = new System.Windows.Controls.CheckBox { Content = definition.Label, IsChecked = value.GetBoolean(), Margin = new Thickness(0, 6, 0, 8) };
            else
            {
                SettingsFields.Children.Add(new System.Windows.Controls.TextBlock { Text = definition.Label, Margin = new Thickness(0, 6, 0, 5), TextWrapping = TextWrapping.Wrap });
                control = new System.Windows.Controls.TextBox { Text = definition.Type == "number" ? value.GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture) : value.GetString() ?? "", MaxLength = 4096, Margin = new Thickness(0, 0, 0, 8) };
            }
            SettingsFields.Children.Add(control); settingControls.Add(definition.Key, (definition.Type, control));
        }
        SettingsPanel.Visibility = Visibility.Visible;
    }
    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (editingId is null || busy) return;
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, field) in settingControls)
        {
            if (field.Control is System.Windows.Controls.CheckBox check) values[key] = JsonSerializer.SerializeToElement(check.IsChecked == true);
            else if (field.Control is System.Windows.Controls.TextBox box && field.Type == "number")
            {
                if (!double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                { SettingsError.Text = "请输入有效数字（小数使用英文点）。"; return; }
                values[key] = JsonSerializer.SerializeToElement(number);
            }
            else if (field.Control is System.Windows.Controls.TextBox text) values[key] = JsonSerializer.SerializeToElement(text.Text);
        }
        var id = editingId;
        await PerformAsync(async () => { await manager.UpdateSettingsAsync(id, values); CloseSettings(); StatusText.Text = "插件设置已保存。"; });
    }
    private void CancelSettings_Click(object sender, RoutedEventArgs e) { if (!busy) CloseSettings(); }
    private void CloseSettings() { editingId = null; settingControls.Clear(); SettingsFields.Children.Clear(); SettingsPanel.Visibility = Visibility.Collapsed; }
    private async Task PerformAsync(Func<Task> action)
    {
        if (busy) return; busy = true; InstallButton.IsEnabled = false; SettingsPanel.IsEnabled = false;
        try { await action(); if (!closed) RefreshList(); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or KeyNotFoundException)
        { if (!closed) { StatusText.Text = "未完成：" + e.Message; if (editingId is not null) SettingsError.Text = StatusText.Text; } }
        finally { busy = false; if (!closed) { RefreshList(); InstallButton.IsEnabled = true; SettingsPanel.IsEnabled = true; } }
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "安装灵动岛插件", Filter = "灵动岛插件包|*.islandpkg", Multiselect = false };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await PerformAsync(() => InstallAsync(picker.FileName));
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
        if (added.Length > 0 && !await ConfirmPermissionsAsync($"“{manifest.Name}”需要读取：{string.Join("、", added.Select(PermissionName))}。\n仅在对应宿主模块开启时提供数据。是否授权安装？")) { StatusText.Text = "已取消安装，未授予权限。"; return; }
        if (closed) return;
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
        if (sender is FrameworkElement { Tag: string id }) await PerformAsync(async () => { await manager.UninstallAsync(id); if (editingId == id) CloseSettings(); StatusText.Text = "插件已移除。"; });
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
        CopyEmbedded(zip, "fts.example.settings.manifest", "manifest.json");
        CopyEmbedded(zip, "fts.example.settings.script", "main.js");
    }
    private void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(PromptBox.Text); StatusText.Text = "已复制 AI 提示词。"; }
        catch (System.Runtime.InteropServices.ExternalException) { StatusText.Text = "剪贴板被占用，请稍后重试。"; }
    }
    private void ExportSdk_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog { Title = "导出插件 SDK", FileName = "Island-Plugin-API-v1.zip", Filter = "ZIP 文件|*.zip" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            using var zip = new ZipArchive(File.Create(picker.FileName), ZipArchiveMode.Create);
            using (var guide = new StreamWriter(zip.CreateEntry("AI-PROMPT.zh-CN.txt").Open())) guide.Write(IslandPluginGuide.AiPrompt);
            using (var script = new StreamWriter(zip.CreateEntry("example/main.js").Open())) script.Write(IslandPluginGuide.ExampleScript);
            using (var manifest = new StreamWriter(zip.CreateEntry("example/manifest.json").Open())) manifest.Write("""{"id":"studio.focus-timer","name":"专注倒计时","version":"1.0.0","apiVersion":1,"minHostVersion":"0.1.0-beta.23","entry":"main.js","permissions":[]}""");
            CopyEmbedded(zip, "fts.example.settings.manifest", "custom-settings/manifest.json");
            CopyEmbedded(zip, "fts.example.settings.script", "custom-settings/main.js");
            StatusText.Text = "SDK 已导出。将 example 内的两个文件放在 ZIP 根目录并改名为 .islandpkg 即可安装。";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { StatusText.Text = ex.Message; }
    }
    private static void CopyEmbedded(ZipArchive zip, string resource, string entry)
    {
        using var input = typeof(IslandPluginManagerView).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException("内置示例缺失。");
        using var output = zip.CreateEntry(entry).Open(); input.CopyTo(output);
    }
    private void Package_DragOver(object sender, System.Windows.DragEventArgs e) { e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None; e.Handled = true; }
    private async void Package_Drop(object sender, System.Windows.DragEventArgs e)
    { e.Handled = true; if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] { Length: 1 } files && Path.GetExtension(files[0]).Equals(".islandpkg", StringComparison.OrdinalIgnoreCase)) await PerformAsync(() => InstallAsync(files[0])); else StatusText.Text = "请选择一个 .islandpkg 插件包。"; }
}
