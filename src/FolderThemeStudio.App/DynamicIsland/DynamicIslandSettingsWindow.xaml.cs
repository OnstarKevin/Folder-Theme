using System.IO;
using System.Windows;

namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>编辑副本，只有保存成功才应用到窗口；取消不会修改正在运行的配置。</summary>
public partial class DynamicIslandSettingsWindow : Window
{
    private readonly IslandSettings initial;
    private readonly Func<IslandSettings, bool, Task> save;
    private readonly Func<Task<string>> requestAccess;
    private readonly IslandFontStore fonts;
    private bool closed;
    private sealed record FontChoice(string Label, string FilePath, string Family);
    public event EventHandler? PluginsRequested;
    public event EventHandler? EditPositionRequested;
    public DynamicIslandSettingsWindow(IslandSettings settings, bool startup, Func<IslandSettings, bool, Task> save, Func<Task<string>> requestAccess, IslandFontStore? fontStore = null)
    {
        fonts = fontStore ?? new IslandFontStore();
        InitializeComponent();
        initial = settings; this.save = save; this.requestAccess = requestAccess;
        TimeCheck.IsChecked = settings.ShowTime; CpuCheck.IsChecked = settings.ShowCpu; MemoryCheck.IsChecked = settings.ShowMemory;
        ClipboardCheck.IsChecked = settings.ShowClipboard; MusicCheck.IsChecked = settings.ShowMusic; NotificationsCheck.IsChecked = settings.ShowNotifications;
        StartupCheck.IsChecked = startup; OpacitySlider.Value = settings.Opacity * 100;
        IntervalBox.Text = settings.RefreshSeconds.ToString(); LyricsDirectoryBox.Text = settings.LyricsDirectory;
        LyricsCheck.IsChecked = settings.ShowLyrics;
        OnlineLyricsCheck.IsChecked = settings.OnlineLyrics;
        LocalNotificationsCheck.IsChecked = settings.LocalNotifications;
        NotificationPortBox.Text = settings.NotificationPort.ToString();
        BlockedSourcesBox.Text = settings.BlockedNotificationSources;
        DragMotionCheck.IsChecked = settings.DragMotion; SnapEdgesCheck.IsChecked = settings.SnapToEdges;
        QuotesCheck.IsChecked = settings.ShowQuotes; AutoQuotesCheck.IsChecked = settings.AutoRefreshQuotes;
        StretchMotionCheck.IsChecked = settings.StretchMotion;
        ClickThroughCheck.IsChecked = settings.ClickThrough; HoverDetailsCheck.IsChecked = settings.HoverDetails;
        LightTextCheck.IsChecked = settings.LightText;
        ImageBackgroundCheck.IsChecked = settings.ImageBackground;
        LoadFontChoices(settings.CustomFontPath, settings.CustomFontFamily);
        Closed += (_, _) => closed = true;
    }
    private void LoadFontChoices(string path, string family, IReadOnlyList<string>? available = null)
    {
        var choices = new List<FontChoice> { new("微软雅黑（内置默认）", "", "") };
        var resolved = fonts.Resolve(path, family);
        if (!string.IsNullOrWhiteSpace(path) && resolved.Source != IslandFontStore.DefaultFamily)
            foreach (var name in available ?? fonts.GetFamilies(path)) choices.Add(new(name, path, name));
        FontFamilyBox.ItemsSource = choices;
        FontFamilyBox.SelectedItem = choices.FirstOrDefault(c => c.Family == family && c.FilePath == path) ?? choices[0];
    }
    private void FontSelection_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (FontPreview is null || FontSourceText is null || FontFamilyBox.SelectedItem is not FontChoice choice) return;
        FontPreview.FontFamily = fonts.Resolve(choice.FilePath, choice.Family);
        FontSourceText.Text = choice.FilePath.Length == 0 ? "使用 Windows 自带微软雅黑；支持本地导入 TTF / OTF / TTC，最大 32 MiB。" : "已保存软件内副本；移动原文件不影响使用。";
    }
    private async void ImportFont_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "导入灵动岛字体", Filter = "字体文件|*.ttf;*.otf;*.ttc", Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        ImportFontButton.IsEnabled = ResetFontButton.IsEnabled = FontFamilyBox.IsEnabled = SaveButton.IsEnabled = false;
        FontSourceText.Text = "正在读取字体…";
        try
        {
            var imported = await Task.Run(() => fonts.Import(picker.FileName));
            if (closed) return;
            LoadFontChoices(imported.FilePath, imported.FamilyName, imported.Families);
            ErrorText.Text = "";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { if (!closed) { ErrorText.Text = "字体未导入：" + ex.Message; FontSelection_Changed(this, null!); } }
        finally { if (!closed) ImportFontButton.IsEnabled = ResetFontButton.IsEnabled = FontFamilyBox.IsEnabled = SaveButton.IsEnabled = true; }
    }
    private void ResetFont_Click(object sender, RoutedEventArgs e) => LoadFontChoices("", "");
    private void Plugins_Click(object sender, RoutedEventArgs e) => PluginsRequested?.Invoke(this, EventArgs.Empty);
    private void EditPosition_Click(object sender, RoutedEventArgs e) => EditPositionRequested?.Invoke(this, EventArgs.Empty);
    private void LyricsDirectory_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "选择 LRC 歌词文件夹" };
        if (picker.ShowDialog(this) == true) LyricsDirectoryBox.Text = picker.FolderName;
    }
    private async void NotificationAccess_Click(object sender, RoutedEventArgs e) => NotificationStatus.Text = await requestAccess();
    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(initial.NotificationToken); NotificationStatus.Text = "已复制本地接口令牌"; }
        catch (System.Runtime.InteropServices.ExternalException) { NotificationStatus.Text = "剪贴板被占用，请稍后重试"; }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out var seconds) || seconds is < 1 or > 60) { ErrorText.Text = "刷新间隔需为 1–60 的整数。"; return; }
        if (!int.TryParse(NotificationPortBox.Text, out var port) || port is < 1024 or > 65535) { ErrorText.Text = "消息端口需为 1024–65535 的整数。"; return; }
        var lyrics = LyricsDirectoryBox.Text.Trim();
        if (lyrics.Length > 0 && !Directory.Exists(lyrics)) { ErrorText.Text = "歌词文件夹不存在，请重新选择。"; return; }
        SaveButton.IsEnabled = false;
        try
        {
            await save(initial with { ShowTime = TimeCheck.IsChecked == true, ShowCpu = CpuCheck.IsChecked == true,
                ShowMemory = MemoryCheck.IsChecked == true, ShowClipboard = ClipboardCheck.IsChecked == true,
                ShowQuotes = QuotesCheck.IsChecked == true, AutoRefreshQuotes = AutoQuotesCheck.IsChecked == true,
                StretchMotion = StretchMotionCheck.IsChecked == true, BackgroundImagePath = "", ImageBackground = ImageBackgroundCheck.IsChecked == true,
                CustomFontPath = (FontFamilyBox.SelectedItem as FontChoice)?.FilePath ?? "",
                CustomFontFamily = (FontFamilyBox.SelectedItem as FontChoice)?.Family ?? "",
                ClickThrough = ClickThroughCheck.IsChecked == true, HoverDetails = HoverDetailsCheck.IsChecked == true,
                LightText = LightTextCheck.IsChecked == true,
                ShowLyrics = LyricsCheck.IsChecked == true, ShowMusic = MusicCheck.IsChecked == true, ShowNotifications = NotificationsCheck.IsChecked == true,
                Opacity = OpacitySlider.Value / 100, RefreshSeconds = seconds, LyricsDirectory = lyrics,
                OnlineLyrics = OnlineLyricsCheck.IsChecked == true, LocalNotifications = LocalNotificationsCheck.IsChecked == true,
                NotificationPort = port, BlockedNotificationSources = BlockedSourcesBox.Text,
                DragMotion = DragMotionCheck.IsChecked == true, SnapToEdges = SnapEdgesCheck.IsChecked == true }, StartupCheck.IsChecked == true);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { ErrorText.Text = "保存失败，请检查配置目录或开机启动权限。"; }
        finally { SaveButton.IsEnabled = true; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

