using System.Windows;
using FolderThemeStudio.App.Localization;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.App.Services;
using FolderThemeStudio.App.Settings;
using FolderThemeStudio.App.ViewModels;
using FolderThemeStudio.Core.Application;
using FolderThemeStudio.Core.Themes;
using FolderThemeStudio.App.Monitoring;
using FolderThemeStudio.Core.SystemIntegration;

namespace FolderThemeStudio.App;

public partial class App : System.Windows.Application
{
    private TrayIconService? trayIcon;
    private MainWindow? mainWindow;
    private FolderQuickEditMouseHook? quickEditHook;
    private DynamicIslandController? islandController;
    private bool exiting;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        // A plugin worker must never initialize WPF services, tray icons or desktop migration.
        if (DynamicIsland.Plugins.PluginWorker.RunIfRequested(e.Args)) { Shutdown(); return; }
        var restoreDesktop = e.Args.Contains("--restore-desktop", StringComparer.OrdinalIgnoreCase);
        new DesktopRecovery().Restore(force: restoreDesktop);
        if (restoreDesktop) { Shutdown(); return; }
        var settingsStore = new JsonAppSettingsStore();
        var settings = await settingsStore.LoadAsync();
        var localization = new LocalizationService(settings.Language);
        var dialogs = new DialogService(() => MainWindow);
        var monitoringStore = new JsonMonitoringRuleStore();
        var folderCategoryCatalog = new JsonFolderCategoryCatalog();
        var folderMonitoring = new FolderMonitoringCoordinator(
            monitoringStore,
            new MonitoredFolderIconService());
        var monitoringAssets = new MonitoringIconAssetService();
        var categoryPopup = new FolderCategoryPopupWindow();
        categoryPopup.SetCardOpacity(settings.CardOpacity);
        var folderHoverMonitor = new FolderHoverMonitor(
            folderCategoryCatalog,
            new ExplorerFolderHoverTargetResolver(),
            new WindowsCursorPositionSource(),
            categoryPopup,
            new WpfViewModelDispatcher());
        var viewModel = new MainViewModel(
            new ThemeApplicationCoordinator(new ThemeApplicationService()),
            dialogs,
            new ThemeCatalog(new ThemeStore()),
            new FolderPreviewRenderer(),
            new TaskPreviewDelay(),
            new WpfViewModelDispatcher(),
            folderMonitoring: folderMonitoring,
            monitoringAssets: monitoringAssets,
            activatePreviews: false);
        viewModel.Material = MaterialStyle.Satin;
        viewModel.Opacity = 1;
        viewModel.HighlightStrength = .28;
        viewModel.GlowStrength = .06;
        viewModel.StrokeOpacity = .45;
        viewModel.ShadowStrength = .22;
        viewModel.ShadowOffset = 4;
        viewModel.SelectedMode = ApplicationMode.Compatible;
        viewModel.ApplyPalette(settings.Palette);
        var startupRegistration = new StartupRegistrationService();
        try { startupRegistration.SetEnabled(settings.StartWithWindows); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Startup registration is optional; the main application remains usable.
        }
        trayIcon = new TrayIconService();
        islandController = new DynamicIslandController(new IslandSettingsStore(),
            () => mainWindow?.StartupEnabled ?? settings.StartWithWindows,
            enabled => mainWindow!.SetStartupEnabledAsync(enabled), ExitApplication);
        var editChord = settings.EditChord;
        var window = new MainWindow(
            viewModel,
            settingsStore,
            localization,
            settings,
            startupRegistration,
            () => trayIcon.ShowBackgroundNotice(),
            monitoringStore,
            folderMonitoring,
            monitoringAssets,
            dialogs,
            folderCategoryCatalog,
            folderHoverMonitor,
            categoryPopup.SetCardOpacity,
            chord => { editChord = chord; quickEditHook?.UpdateChord(chord); }, islandController, enabled =>
            {
                if (!FolderQuickEditMouseHook.IsRequired(mainWindow?.HoverCardsEnabled ?? settings.HoverCardsEnabled, enabled))
                { quickEditHook?.Dispose(); quickEditHook = null; return; }
                if (quickEditHook is not null) { quickEditHook.SetEditingEnabled(enabled); return; }
                try
                {
                    quickEditHook = new FolderQuickEditMouseHook(folderHoverMonitor,
                        path => _ = mainWindow!.OpenFolderCategoryEditorAsync(path), editChord, Dispatcher);
                    quickEditHook.SetEditingEnabled(enabled);
                }
                catch (System.ComponentModel.Win32Exception) { }
            });
        mainWindow = window;
        MainWindow = window;
        window.Closed += (_, _) => { mainWindow = null; ExitApplication(); };
        trayIcon.OpenRequested += (_, _) => window.Dispatcher.Invoke(window.ShowFromTray);
        trayIcon.ToggleMonitoringRequested += async (_, _) => await viewModel.ToggleMonitoringAsync();
        trayIcon.ExitRequested += (_, _) => window.Dispatcher.Invoke(ExitApplication);
        trayIcon.IslandSettingsRequested += async (_, _) => await islandController.ConfigureAsync();
        trayIcon.IslandEditRequested += async (_, _) => await islandController.EditPositionAsync();
        await folderMonitoring.SetFeatureEnabledAsync(settings.AutomaticRulesEnabled);
        viewModel.SetFeatureSwitches(settings.ThemeEditingEnabled, settings.AutomaticRulesEnabled);
        await viewModel.InitializeAsync();
        await window.StartBackgroundServicesAsync();
        var background = e.Args.Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        if (!background)
        {
            window.Show();
            await window.ShowFirstRunTutorialAsync();
        }
    }

    private void ExitApplication()
    {
        if (exiting) return;
        exiting = true;
        islandController?.Dispose();
        quickEditHook?.Dispose();
        trayIcon?.Dispose();
        trayIcon = null;
        mainWindow?.RequestExplicitExit();
        Shutdown();
    }
}
