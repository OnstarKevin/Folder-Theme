using System.IO;
using System.Windows;

namespace FolderThemeStudio.App.DynamicIsland;

/// <summary>编辑副本，只有保存成功才应用到窗口；取消不会修改正在运行的配置。</summary>
public partial class DynamicIslandSettingsView : System.Windows.Controls.UserControl
{
    private readonly IslandSettings initial;
    private readonly Func<IslandSettings, bool, Task> save;
    private readonly Func<Task<string>> requestAccess;
    private readonly IslandFontStore fonts;
    private bool closed;
    private readonly IslandBackgroundStore backgrounds = new();
    private string backgroundPath = "";
    private string savedBackgroundPath = "";
    private System.Windows.Media.Imaging.BitmapSource? previewImage;
    private bool backgroundReady;
    private bool hostActive = true;
    private bool busy;
    public bool IsBusy => busy;
    private void SetBusy(bool value)
    {
        busy = value;
        SaveButton.IsEnabled = CancelButton.IsEnabled = QuickActions.IsEnabled =
            ImportFontButton.IsEnabled = ResetFontButton.IsEnabled = FontFamilyBox.IsEnabled =
            ImportBackgroundButton.IsEnabled = ResetBackgroundButton.IsEnabled = !value;
    }
    public void ReportStatus(string text, bool error = false)
    {
        ErrorText.Foreground = error ? System.Windows.Media.Brushes.Firebrick : System.Windows.Media.Brushes.DimGray;
        ErrorText.Text = text;
    }
    public event EventHandler? Cancelled;
    public Func<bool, Task>? ChangeVisibility { get; set; }
    private async void ShowIsland_Click(object sender, RoutedEventArgs e) => await ChangeVisibilityAsync(true);
    private async void HideIsland_Click(object sender, RoutedEventArgs e) => await ChangeVisibilityAsync(false);
    private async Task ChangeVisibilityAsync(bool visible)
    {
        if (IsBusy || ChangeVisibility is null) return;
        SetBusy(true);
        try { await ChangeVisibility(visible); ReportStatus(visible ? "灵动岛已显示。" : "灵动岛已关闭，设置仍可编辑。"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ReportStatus(ex.Message, true); }
        finally { SetBusy(false); }
    }
    public void Release() { closed = true; UpdateBackgroundPreviewActivity(); }
    public void SetHostActivity(bool active) { hostActive = active; UpdateBackgroundPreviewActivity(); }
    private void UpdateBackgroundPreviewActivity()
    {
        if (!closed && hostActive && IsVisible && AppearanceSection.Visibility == Visibility.Visible)
        {
            if (!backgroundReady) { LoadBackgroundPreview(); backgroundReady = true; UpdateBackgroundPreview(); }
        }
        else { backgroundReady = false; previewImage = null; BackgroundPreviewImage.Background = null; }
    }
    public void SelectModule(string module)
    {
        var basic = module == "island";
        IntroTitle.Visibility = IntroText.Visibility = QuickActions.Visibility = ClickThroughCheck.Visibility = HoverDetailsCheck.Visibility = basic ? Visibility.Visible : Visibility.Collapsed;
        ContentSection.Visibility = basic ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSection.Visibility = module == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        LyricsSection.Visibility = module == "music" ? Visibility.Visible : Visibility.Collapsed;
        NotificationsSection.Visibility = module == "messages" ? Visibility.Visible : Visibility.Collapsed;
        UpdateBackgroundPreviewActivity();
    }
    private sealed record FontChoice(string Label, string FilePath, string Family);
    public event EventHandler? PluginsRequested;
    public event EventHandler? EditPositionRequested;
    public DynamicIslandSettingsView(IslandSettings settings, bool startup, Func<IslandSettings, bool, Task> save, Func<Task<string>> requestAccess, IslandFontStore? fontStore = null)
    {
        fonts = fontStore ?? new IslandFontStore();
        InitializeComponent();
        initial = settings; this.save = save; this.requestAccess = requestAccess;
        IsVisibleChanged += (_, _) => UpdateBackgroundPreviewActivity();
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
        backgroundPath = settings.BackgroundImagePath;
        savedBackgroundPath = backgroundPath;
        BackgroundOpacitySlider.Value = settings.BackgroundOpacity; BackgroundLightenSlider.Value = settings.BackgroundLighten;
        BackgroundStrengthSlider.Value = settings.BackgroundFadeStrength; BackgroundStartSlider.Value = settings.BackgroundFadeStart;
        BackgroundDirectionBox.SelectedValue = settings.BackgroundFadeDirection; BackgroundProfileBox.SelectedValue = settings.BackgroundFadeProfile;
        BackgroundAlignmentBox.SelectedValue = settings.BackgroundAlignment;
        FlowBorderCheck.IsChecked = settings.FlowBorder; AnimateBorderCheck.IsChecked = settings.AnimateBorder;
        LoadFontChoices(settings.CustomFontPath, settings.CustomFontFamily);

    }
    private IslandSettings ReadBackgroundSettings() => initial with
    {
        BackgroundImagePath = backgroundPath, ImageBackground = ImageBackgroundCheck.IsChecked == true,
        BackgroundOpacity = BackgroundOpacitySlider.Value, BackgroundLighten = BackgroundLightenSlider.Value,
        BackgroundFadeStrength = BackgroundStrengthSlider.Value, BackgroundFadeStart = BackgroundStartSlider.Value,
        BackgroundFadeDirection = BackgroundDirectionBox.SelectedValue as string ?? "Down",
        BackgroundFadeProfile = BackgroundProfileBox.SelectedValue as string ?? "Smooth",
        BackgroundAlignment = BackgroundAlignmentBox.SelectedValue as string ?? "Center",
        FlowBorder = FlowBorderCheck.IsChecked == true, AnimateBorder = AnimateBorderCheck.IsChecked == true,
    };
    private void LoadBackgroundPreview()
    {
        previewImage = backgrounds.Resolve(backgroundPath);
        if (previewImage is null)
        {
            BackgroundSourceText.Text = backgroundPath.Length == 0 ? "当前：内置天色原图。" : "自定义图片已失效，当前回退内置天色原图。";
            previewImage = IslandSurfaceAppearance.Background;
        }
        else BackgroundSourceText.Text = $"当前：自定义图片 · {previewImage.PixelWidth}×{previewImage.PixelHeight}像素（软件内副本）";
    }
    private void BackgroundPreview_Changed(object sender, RoutedEventArgs e) { if (backgroundReady) UpdateBackgroundPreview(); }
    private void UpdateBackgroundPreview()
    {
        var s = ReadBackgroundSettings();
        BackgroundPreviewLayers.Visibility = s.ImageBackground ? Visibility.Visible : Visibility.Collapsed;
        BackgroundPreviewLayers.Opacity = s.BackgroundOpacity;
        BackgroundPreviewLayers.OpacityMask = IslandSurfaceAppearance.CreateFadeMask(s);
        BackgroundPreviewImage.Background = IslandSurfaceAppearance.CreateImageBrush(previewImage!, s, !ReferenceEquals(previewImage, IslandSurfaceAppearance.Background));
        BackgroundPreviewTint.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(255 * s.BackgroundLighten), 255, 255, 255));
    }
    private async void ImportBackground_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || closed) return;
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "选择灵动岛背景", Filter = "PNG / JPEG 图片|*.png;*.jpg;*.jpeg" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        SetBusy(true);
        try
        {
            var imported = await Task.Run(() => backgrounds.Import(picker.FileName, savedBackgroundPath));
            if (closed) return;
            backgroundPath = imported; backgroundReady = false; ImageBackgroundCheck.IsChecked = true; UpdateBackgroundPreviewActivity(); ReportStatus("背景已载入预览，保存后应用。");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or FormatException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { if (!closed) ReportStatus("图片未导入：" + ex.Message, true); }
        finally { if (!closed) SetBusy(false); }
    }
    private void ResetBackground_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || closed) return;
        backgroundPath = ""; backgroundReady = false; ImageBackgroundCheck.IsChecked = true; UpdateBackgroundPreviewActivity();
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
        if (IsBusy || closed) return;
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "导入灵动岛字体", Filter = "字体文件|*.ttf;*.otf;*.ttc", Multiselect = false };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        SetBusy(true);
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
        finally { if (!closed) SetBusy(false); }
    }
    private void ResetFont_Click(object sender, RoutedEventArgs e) => LoadFontChoices("", "");
    private void Plugins_Click(object sender, RoutedEventArgs e) => PluginsRequested?.Invoke(this, EventArgs.Empty);
    private void EditPosition_Click(object sender, RoutedEventArgs e) => EditPositionRequested?.Invoke(this, EventArgs.Empty);
    private void LyricsDirectory_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "选择 LRC 歌词文件夹" };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) LyricsDirectoryBox.Text = picker.FolderName;
    }
    private async void NotificationAccess_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || closed) return;
        SetBusy(true);
        try { NotificationStatus.Text = await requestAccess(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { NotificationStatus.Text = "无法请求通知权限：" + ex.Message; }
        finally { if (!closed) SetBusy(false); }
    }
    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(initial.NotificationToken); NotificationStatus.Text = "已复制本地接口令牌"; }
        catch (System.Runtime.InteropServices.ExternalException) { NotificationStatus.Text = "剪贴板被占用，请稍后重试"; }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || closed) return;
        ReportStatus("", true);
        if (!int.TryParse(IntervalBox.Text, out var seconds) || seconds is < 1 or > 60) { ErrorText.Text = "刷新间隔需为 1–60 的整数。"; return; }
        if (!int.TryParse(NotificationPortBox.Text, out var port) || port is < 1024 or > 65535) { ErrorText.Text = "消息端口需为 1024–65535 的整数。"; return; }
        var lyrics = LyricsDirectoryBox.Text.Trim();
        if (lyrics.Length > 0 && !Directory.Exists(lyrics)) { ErrorText.Text = "歌词文件夹不存在，请重新选择。"; return; }
        SetBusy(true);
        try
        {
            await save(ReadBackgroundSettings() with { ShowTime = TimeCheck.IsChecked == true, ShowCpu = CpuCheck.IsChecked == true,
                ShowMemory = MemoryCheck.IsChecked == true, ShowClipboard = ClipboardCheck.IsChecked == true,
                ShowQuotes = QuotesCheck.IsChecked == true, AutoRefreshQuotes = AutoQuotesCheck.IsChecked == true,
                StretchMotion = StretchMotionCheck.IsChecked == true,
                CustomFontPath = (FontFamilyBox.SelectedItem as FontChoice)?.FilePath ?? "",
                CustomFontFamily = (FontFamilyBox.SelectedItem as FontChoice)?.Family ?? "",
                ClickThrough = ClickThroughCheck.IsChecked == true, HoverDetails = HoverDetailsCheck.IsChecked == true,
                LightText = LightTextCheck.IsChecked == true,
                ShowLyrics = LyricsCheck.IsChecked == true, ShowMusic = MusicCheck.IsChecked == true, ShowNotifications = NotificationsCheck.IsChecked == true,
                Opacity = OpacitySlider.Value / 100, RefreshSeconds = seconds, LyricsDirectory = lyrics,
                OnlineLyrics = OnlineLyricsCheck.IsChecked == true, LocalNotifications = LocalNotificationsCheck.IsChecked == true,
                NotificationPort = port, BlockedNotificationSources = BlockedSourcesBox.Text,
                DragMotion = DragMotionCheck.IsChecked == true, SnapToEdges = SnapEdgesCheck.IsChecked == true }, StartupCheck.IsChecked == true);
            savedBackgroundPath = backgroundPath;
            ReportStatus("已保存并应用。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        { ErrorText.Text = "保存失败，请检查配置目录或开机启动权限。"; }
        finally { if (!closed) SetBusy(false); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!IsBusy) Cancelled?.Invoke(this, EventArgs.Empty);
    }
}

