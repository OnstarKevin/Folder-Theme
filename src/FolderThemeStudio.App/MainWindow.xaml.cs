using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using FolderThemeStudio.App.Controls;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.App.Localization;
using FolderThemeStudio.App.Monitoring;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.App.Settings;
using FolderThemeStudio.App.ViewModels;
using FolderThemeStudio.Core.Rendering;
using FolderThemeStudio.Core.Themes;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace FolderThemeStudio.App;

public partial class MainWindow : RoundedWindow
{
    private const double StackedLayoutWidth = 900;
    private readonly MainViewModel viewModel;
    private readonly IAppSettingsStore settingsStore;
    private readonly ILocalizationService localization;
    private readonly IStartupRegistrationService? startupRegistration;
    private readonly Action? backgrounded;
    private readonly IMonitoringRuleStore monitoringRuleStore;
    private readonly IFolderMonitoringCoordinator folderMonitoring;
    private readonly IMonitoringIconAssetService monitoringAssets;
    private readonly IMainViewModelDialogs ruleDialogs;
    private readonly IFolderCategoryCatalog folderCategoryCatalog;
    private readonly IFolderHoverMonitorService folderHoverMonitor;
    private readonly Action<double>? updateCardOpacity;
    private AppSettings currentSettings;
    private bool explicitExit;
    private bool isDraggingSticker;
    private FolderQuickNoteWindow? quickCategoryDialog;
    private FolderCategoriesViewModel? quickCategoryViewModel;
    private int quickNoteRequest;
    private readonly Action<FolderThemeStudio.App.Services.FolderEditChord>? updateEditChord;
    private IDynamicIslandController? islandController;
    private SettingsWorkspace? settingsWorkspace;
    private readonly Action<bool>? updateQuickNotes;

    public MainWindow(MainViewModel viewModel) : this(
        viewModel,
        new JsonAppSettingsStore(),
        new LocalizationService(AppSettings.Default.Language),
        AppSettings.Default with { CloseToTray = false },
        monitoringRuleStore: new JsonMonitoringRuleStore(),
        folderMonitoring: new NullFolderMonitoringCoordinator(),
        monitoringAssets: new MonitoringIconAssetService())
    {
    }

    public MainWindow(
        MainViewModel viewModel,
        IAppSettingsStore settingsStore,
        ILocalizationService localization,
        AppSettings currentSettings,
        IStartupRegistrationService? startupRegistration = null,
        Action? backgrounded = null,
        IMonitoringRuleStore? monitoringRuleStore = null,
        IFolderMonitoringCoordinator? folderMonitoring = null,
        IMonitoringIconAssetService? monitoringAssets = null,
        IMainViewModelDialogs? ruleDialogs = null,
        IFolderCategoryCatalog? folderCategoryCatalog = null,
        IFolderHoverMonitorService? folderHoverMonitor = null,
        Action<double>? updateCardOpacity = null,
        Action<FolderThemeStudio.App.Services.FolderEditChord>? updateEditChord = null,
        IDynamicIslandController? islandController = null, Action<bool>? updateQuickNotes = null)
    {
        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
        this.currentSettings = currentSettings ?? throw new ArgumentNullException(nameof(currentSettings));
        this.startupRegistration = startupRegistration;
        this.backgrounded = backgrounded;
        this.monitoringRuleStore = monitoringRuleStore ?? new JsonMonitoringRuleStore();
        this.folderMonitoring = folderMonitoring ?? new NullFolderMonitoringCoordinator();
        this.monitoringAssets = monitoringAssets ?? new MonitoringIconAssetService();
        this.ruleDialogs = ruleDialogs ?? new DialogService(() => this);
        this.folderCategoryCatalog = folderCategoryCatalog ?? new JsonFolderCategoryCatalog();
        this.folderHoverMonitor = folderHoverMonitor ?? new NullFolderHoverMonitor();
        this.updateCardOpacity = updateCardOpacity;
        this.updateEditChord = updateEditChord;
        this.islandController = islandController;
        this.islandController?.SetSettingsNavigator(NavigateSettingsFromOutsideAsync);
        this.updateQuickNotes = updateQuickNotes;
        UpdateFeatureEntrypoints();
        DataContext = viewModel;
        IsVisibleChanged += (_, _) => UpdateWindowResources();
        StateChanged += (_, _) => UpdateWindowResources();
        Closing += Window_Closing;
        Closed += (_, _) =>
        {
            settingsWorkspace?.Release();
            this.folderHoverMonitor.Dispose();
            this.islandController?.Dispose();
            viewModel.Dispose();
        };
        UpdateResponsiveLayout(Width);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveLayout(e.NewSize.Width);

    private void MainScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, MainScrollViewer);
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is ScrollViewer) return;
        }
        MainScrollViewer.ScrollToVerticalOffset(MainWindowWheelPolicy.NextOffset(
            MainScrollViewer.VerticalOffset, e.Delta, SystemParameters.WheelScrollLines, 16));
        e.Handled = true;
    }

    public async Task ShowFirstRunTutorialAsync()
    {
        if (!currentSettings.TutorialCompleted) await OpenTutorialAsync();
    }

    private async void Tutorial_Click(object sender, RoutedEventArgs e) => await OpenTutorialAsync();

    public bool StartupEnabled => currentSettings.StartWithWindows;
    public bool HoverCardsEnabled => currentSettings.HoverCardsEnabled;
    public async Task SetStartupEnabledAsync(bool enabled)
    {
        var previous = currentSettings.StartWithWindows;
        startupRegistration?.SetEnabled(enabled);
        try
        {
            var next = currentSettings with { StartWithWindows = enabled };
            await settingsStore.SaveAsync(next);
            currentSettings = next;
        }
        catch { startupRegistration?.SetEnabled(previous); throw; }
    }
    private async void DynamicIsland_Click(object sender, RoutedEventArgs e)
    {
        await NavigateSettingsAsync("island");
    }
    private async void BundledIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || viewModel.IsBusy) return;
        try { await viewModel.ImportImagePathAsync(BundledFolderIcons.Materialize(id)); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { ruleDialogs.ShowError("内置图标暂不可用", error.Message); }
    }


    private void Home_Click(object sender, RoutedEventArgs e) => ShowSection(HomePanel, "Home.Nav", "Home.Subtitle");
    private void Designer_Click(object sender, RoutedEventArgs e) { if (currentSettings.ThemeEditingEnabled) ShowSection(EditorWorkspace, "Home.Design", "Studio.Subtitle"); }
    private void Recovery_Click(object sender, RoutedEventArgs e) => ShowSection(RecoveryPanel, "Home.Recovery", "Home.RecoveryBody");

    private void ShowSection(FrameworkElement selected, string titleKey, string subtitleKey)
    {
        if (!ReferenceEquals(selected, SettingsPanel) && settingsWorkspace?.IsBusy == true) return;
        foreach (var section in new FrameworkElement[] { HomePanel, EditorWorkspace, RecoveryPanel, SettingsPanel })
            section.Visibility = ReferenceEquals(section, selected) ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.SetResourceReference(TextBlock.TextProperty, titleKey);
        PageSubtitle.SetResourceReference(TextBlock.TextProperty, subtitleKey);
        HomeNavButton.Background = ReferenceEquals(selected, HomePanel) ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 234, 250)) : System.Windows.Media.Brushes.Transparent;
        DesignNavButton.Background = ReferenceEquals(selected, EditorWorkspace) ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 234, 250)) : System.Windows.Media.Brushes.Transparent;
        RecoveryNavButton.Background = ReferenceEquals(selected, RecoveryPanel) ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 234, 250)) : System.Windows.Media.Brushes.Transparent;
        if (!ReferenceEquals(selected, SettingsPanel)) PluginsNavButton.Background = System.Windows.Media.Brushes.Transparent;
        MainScrollViewer.ScrollToTop();
        if (SystemParameters.ClientAreaAnimation)
            selected.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop });
    }

    private void AppleColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (viewModel is null || e.AddedItems.Count == 0 || e.AddedItems[0] is not AppleColorPreset color) return;
        if (string.Equals(viewModel.GradientStart, color.Hex, StringComparison.OrdinalIgnoreCase)) return;
        viewModel.IsBuiltInEditor = true;
        viewModel.ApplyPalette(color.Palette);
    }

    private async void AutoStyleRules_Click(object sender, RoutedEventArgs e)
    {
        if (!currentSettings.AutomaticRulesEnabled) return;
        var viewModel = new AutoStyleRulesViewModel(
            monitoringRuleStore,
            folderMonitoring,
            monitoringAssets,
            ruleDialogs,
            localization,
            new WpfViewModelDispatcher(),
            this.viewModel.CurrentTheme);
        await viewModel.LoadAsync();
        var dialog = new AutoStyleRulesWindow(viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private async void FolderCategories_Click(object sender, RoutedEventArgs e)
    {
        var categories = new FolderCategoriesViewModel(
            folderCategoryCatalog,
            folderHoverMonitor,
            ruleDialogs,
            localization,
            new WpfViewModelDispatcher());
        await categories.LoadAsync();
        var dialog = new FolderCategoriesWindow(categories) { Owner = this };
        dialog.ShowDialog();
    }

    public async Task OpenFolderCategoryEditorAsync(string folderPath)
    {
        if (!currentSettings.QuickNotesEnabled) return;
        if (string.IsNullOrWhiteSpace(folderPath) || !System.IO.Directory.Exists(folderPath)) return;
        var request = ++quickNoteRequest;
        if (quickCategoryDialog is { IsVisible: true } && quickCategoryViewModel is not null)
        {
            if (quickCategoryViewModel.IsBusy) return;
            quickCategoryViewModel.SelectFolderForQuickNote(folderPath);
            quickCategoryDialog.Activate();
            quickCategoryDialog.FocusNote();
            return;
        }
        var categories = new FolderCategoriesViewModel(
            folderCategoryCatalog, folderHoverMonitor, ruleDialogs, localization, new WpfViewModelDispatcher());
        await categories.LoadAsync();
        if (request != quickNoteRequest) { categories.Dispose(); return; }
        categories.SelectFolderForQuickNote(folderPath);
        var dialog = new FolderQuickNoteWindow(categories);
        if (IsVisible) dialog.Owner = this;
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(quickCategoryDialog, dialog))
            {
                quickCategoryDialog = null;
                quickCategoryViewModel = null;
            }
        };
        quickCategoryDialog = dialog;
        quickCategoryViewModel = categories;
        dialog.Show();
        dialog.Activate();
    }

    public async Task StartBackgroundServicesAsync(CancellationToken token = default)
    {
        viewModel.SetFeatureSwitches(currentSettings.ThemeEditingEnabled, currentSettings.AutomaticRulesEnabled);
        await folderMonitoring.SetFeatureEnabledAsync(currentSettings.AutomaticRulesEnabled, token);
        if (currentSettings.HoverCardsEnabled) await folderHoverMonitor.StartAsync(token);
        else await folderHoverMonitor.StopAsync(token);
        updateQuickNotes?.Invoke(currentSettings.QuickNotesEnabled);
        try
        {
            if (islandController is not null) await islandController.SetFeatureEnabledAsync(currentSettings.DynamicIslandEnabled);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { ruleDialogs.ShowError("无法更新灵动岛", "请检查本地配置目录权限。"); }
        UpdateFeatureEntrypoints();
    }

    private void UpdateFeatureEntrypoints()
    {
        DesignNavButton.IsEnabled = HomeStartButton.IsEnabled = currentSettings.ThemeEditingEnabled;
        AutoStyleRulesButton.IsEnabled = currentSettings.AutomaticRulesEnabled;
        DynamicIslandButton.IsEnabled = HomeIslandButton.IsEnabled = currentSettings.DynamicIslandEnabled;
        WorkspaceWallpaper.Visibility = WorkspaceReadabilityVeil.Visibility = currentSettings.ApplicationBackgroundEnabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateWindowResources();
        if (!currentSettings.ThemeEditingEnabled && EditorWorkspace.Visibility == Visibility.Visible)
            ShowSection(HomePanel, "Home.Nav", "Home.Subtitle");
    }

    private void UpdateWindowResources()
    {
        if (viewModel is null || currentSettings is null) return;
        var active = IsVisible && WindowState != WindowState.Minimized;
        viewModel.SetPreviewActivity(active);
        settingsWorkspace?.SetHostActivity(active);
        if (!active || !currentSettings.ApplicationBackgroundEnabled) { WorkspaceWallpaper.Background = null; return; }
        if (WorkspaceWallpaper.Background is not null) return;
        var image = new BitmapImage(); image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/FolderThemeStudio.App;component/Assets/DefaultWallpaper.png");
        image.CacheOption = BitmapCacheOption.OnLoad; image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.DecodePixelWidth = 1600; image.EndInit(); image.Freeze();
        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill }; brush.Freeze();
        WorkspaceWallpaper.Background = brush;
    }

    private async void Settings_Click(object sender, RoutedEventArgs e) => await NavigateSettingsAsync("overview");
    private async void PluginsNav_Click(object sender, RoutedEventArgs e) => await NavigateSettingsAsync("plugins");
    private Task NavigateSettingsFromOutsideAsync(string module)
    {
        ShowFromTray(); return NavigateSettingsAsync(module);
    }
    private async Task NavigateSettingsAsync(string module)
    {
        islandController ??= new DynamicIslandController(new IslandSettingsStore(), () => StartupEnabled,
            SetStartupEnabledAsync, () => { RequestExplicitExit(); System.Windows.Application.Current.Shutdown(); });
        islandController.SetSettingsNavigator(NavigateSettingsFromOutsideAsync);
        if (settingsWorkspace is null)
        {
            settingsWorkspace = new SettingsWorkspace(CreateGeneralSettingsView, islandController);
            settingsWorkspace.ModuleChanged += (_, selected) =>
            {
                PluginsNavButton.Background = selected == "plugins" ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(222, 234, 250)) : System.Windows.Media.Brushes.Transparent;
                if (selected == "plugins") { PageTitle.Text = "插件"; PageSubtitle.Text = "安装与管理你的扩展"; }
                else { PageTitle.SetResourceReference(TextBlock.TextProperty, "Top.Settings"); PageSubtitle.SetResourceReference(TextBlock.TextProperty, "Settings.FeaturesHint"); }
                MainScrollViewer.ScrollToTop();
            };
            SettingsPanel.Content = settingsWorkspace;
        }
        ShowSection(SettingsPanel, "Top.Settings", "Settings.FeaturesHint");
        await settingsWorkspace.NavigateAsync(module);
    }
    private SettingsView CreateGeneralSettingsView()
    {
        var draft = new SettingsViewModel(currentSettings, settingsStore, localization, viewModel.ApplyPalette, new FolderPreviewRenderer());
        return new SettingsView(draft, () => NavigateSettingsAsync("island")) { ApplySaved = ApplyGeneralSettingsAsync };
    }
    private async Task ApplyGeneralSettingsAsync()
    {
        currentSettings = await settingsStore.LoadAsync();
        updateCardOpacity?.Invoke(currentSettings.CardOpacity);
        updateEditChord?.Invoke(currentSettings.EditChord);
        if (!currentSettings.QuickNotesEnabled) { ++quickNoteRequest; quickCategoryDialog?.Close(); }
        await StartBackgroundServicesAsync();
        startupRegistration?.SetEnabled(currentSettings.StartWithWindows);
    }

    private async Task OpenTutorialAsync()
    {
        var tutorial = new TutorialViewModel(currentSettings, settingsStore, localization);
        var dialog = new TutorialWindow(tutorial) { Owner = this };
        dialog.ShowDialog();
        currentSettings = await settingsStore.LoadAsync();
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    public void RequestExplicitExit()
    {
        explicitExit = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (explicitExit || !currentSettings.CloseToTray) return;
        e.Cancel = true;
        Hide();
        backgrounded?.Invoke();
    }

    private void StickerPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (viewModel.Sticker == StickerStyle.None || !viewModel.IsBuiltInEditor || sender is not Image image) return;
        isDraggingSticker = image.CaptureMouse();
        UpdateStickerPosition(image, e.GetPosition(image));
        e.Handled = true;
    }

    private void StickerPreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (isDraggingSticker && sender is Image image)
            UpdateStickerPosition(image, e.GetPosition(image));
    }

    private void StickerPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Image image && isDraggingSticker)
        {
            UpdateStickerPosition(image, e.GetPosition(image));
            image.ReleaseMouseCapture();
            isDraggingSticker = false;
            e.Handled = true;
        }
    }

    private void StickerPreview_LostMouseCapture(object sender, MouseEventArgs e) => isDraggingSticker = false;

    private void UpdateStickerPosition(Image image, Point pointer)
    {
        if (image.ActualWidth <= 0 || image.ActualHeight <= 0) return;
        var face = new Rect(.09, .30, .82, .55);
        var position = new Point(
            (pointer.X / image.ActualWidth - face.Left) / face.Width,
            (pointer.Y / image.ActualHeight - face.Top) / face.Height);
        var clamped = StickerPainter.ClampPosition(position, viewModel.StickerScale, face);
        viewModel.StickerX = clamped.X;
        viewModel.StickerY = clamped.Y;
    }

    private void UpdateResponsiveLayout(double width)
    {
        if (SidebarColumn is null || EditorColumn is null) return;
        SidebarColumn.Width = new GridLength(width < 800 ? 156 : 196);
        var stacked = width - SidebarColumn.Width.Value < StackedLayoutWidth;
        HomeFeaturesGrid.Columns = width < 920 ? 1 : 2;
        HomeHeroIcon.Visibility = width < 880 ? Visibility.Collapsed : Visibility.Visible;
        ((Grid)HomeHeroIcon.Parent).ColumnDefinitions[1].Width = width < 880 ? new GridLength(0) : new GridLength(180);
        EditorColumn.Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(1.05, GridUnitType.Star);
        ApplicationColumn.Width = stacked ? new GridLength(0) : new GridLength(.95, GridUnitType.Star);
        TopRow.Height = GridLength.Auto;
        StackedRow.Height = stacked ? GridLength.Auto : new GridLength(0);

        Grid.SetColumn(EditorPanel, 0);
        Grid.SetRow(EditorPanel, 0);
        Grid.SetColumn(ApplicationPanel, stacked ? 0 : 1);
        Grid.SetRow(ApplicationPanel, stacked ? 1 : 0);
        EditorPanel.Margin = stacked ? new Thickness(0) : new Thickness(0, 0, 10, 0);
        ApplicationPanel.Margin = stacked ? new Thickness(0, 8, 0, 0) : new Thickness(10, 0, 0, 0);
    }
}
