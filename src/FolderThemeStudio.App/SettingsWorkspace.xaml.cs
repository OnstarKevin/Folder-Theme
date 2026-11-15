using System.IO;
using System.Windows;
using System.Windows.Controls;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.App.DynamicIsland.Plugins;

namespace FolderThemeStudio.App;

/// <summary>Single settings surface. Views are created on demand; module changes retain drafts.</summary>
public partial class SettingsWorkspace : System.Windows.Controls.UserControl
{
    private readonly Func<SettingsView> createGeneral;
    private readonly IDynamicIslandController island;
    private SettingsView? general;
    private DynamicIslandSettingsView? islandView;
    private IslandPluginManagerView? pluginView;
    private Performance.PerformanceView? performanceView;
    private bool navigating, released;
    private bool hostActive = true;
    private string currentModule = "general";
    public bool IsBusy => navigating || general?.IsBusy == true || islandView?.IsBusy == true || pluginView?.IsBusy == true;
    public void SetHostActivity(bool active) { hostActive = active; islandView?.SetHostActivity(active); }
    public event EventHandler<string>? ModuleChanged;

    public SettingsWorkspace(Func<SettingsView> createGeneral, IDynamicIslandController island)
    {
        InitializeComponent(); this.createGeneral = createGeneral; this.island = island;
    }
    private async void Module_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: string module }) await NavigateAsync(module); }
    private async void Back_Click(object sender, RoutedEventArgs e) => await NavigateAsync("overview");

    public async Task NavigateAsync(string module)
    {
        if (released) return;
        if (IsBusy) { WorkspaceStatus.Text = "请先完成当前保存、导入或插件权限确认。"; return; }
        if (module == "overview")
        {
            ModuleContent.Content = null; SettingsOverview.Visibility = Visibility.Visible; ModuleCard.Visibility = Visibility.Collapsed;
            BackToSettings.Visibility = Visibility.Collapsed; WorkspaceStatus.Text = ""; currentModule = module; ModuleChanged?.Invoke(this, module); return;
        }
        navigating = true;
        try
        {
            object? content;
            var (title, description) = module switch
            {
                "features" => ("功能开关", "按需启用，每个功能独立管理。"),
                "island" => ("灵动岛", "单行信息与交互，保持轻盈。"),
                "appearance" => ("外观与字体", "原图片背景、字体和自然动效。"),
                "music" => ("歌词与播放器", "管理歌曲信息、同步歌词与本地歌词来源。"),
                "messages" => ("消息与剪贴板", "选择要显示的信息，管理通知来源与本地接口。"),
                "plugins" => ("插件", "安装、启停和移除，在这里完成。"),
                "performance" => ("性能", "查看真实占用，按需启用功能。"),
                _ => ("常规", "语言、启动方式与日常操作偏好。")
            };
            if (module is "general" or "features")
            {
                if (general is null)
                {
                    general = createGeneral();
                    general.Cancelled += async (_, _) => { general = null; await NavigateAsync(currentModule); };
                }
                general.SelectSection(module == "features"); content = general;
            }
            else if (module == "performance")
            {
                performanceView ??= new Performance.PerformanceView(); content = performanceView;
            }
            else if (module == "plugins")
            {
                pluginView ??= await island.CreatePluginViewAsync(); content = pluginView;
            }
            else
            {
                if (islandView is null)
                {
                    islandView = await island.CreateSettingsViewAsync();
                    if (islandView is not null)
                        islandView.Cancelled += async (_, _) => { islandView.Release(); islandView = null; await NavigateAsync(currentModule); };
                }
                islandView?.SetHostActivity(hostActive);
                islandView?.SelectModule(module); content = islandView;
            }
            if (released) { islandView?.Release(); pluginView?.Release(); return; }
            ModuleContent.Content = content;
            SettingsOverview.Visibility = Visibility.Collapsed; ModuleCard.Visibility = Visibility.Visible;
            BackToSettings.Visibility = module == "plugins" ? Visibility.Collapsed : Visibility.Visible;
            ModuleTitle.Text = title; ModuleDescription.Text = description;
            WorkspaceStatus.Text = content is null ? "此模块暂不可用。" : module == "performance" ? "仅本页可见时采样，不自动上传数据。" : "切换模块保留草稿；保存后应用。放弃修改恢复上次保存的设置。";
            currentModule = module; ModuleChanged?.Invoke(this, module);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { WorkspaceStatus.Text = "无法打开模块：" + ex.Message; }
        finally { navigating = false; }
    }

    public void Release()
    {
        released = true; islandView?.Release(); pluginView?.Release(); performanceView?.Release();
        ModuleContent.Content = null; general = null; islandView = null; pluginView = null; performanceView = null;
    }
}
