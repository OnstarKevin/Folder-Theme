using FolderThemeStudio.App.DynamicIsland;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class InlineSettingsRoutingTests
{
    [Fact]
    public async Task CachedGeneralDraftPreservesNewTutorialStateAndRecentColors()
    {
        using var directory = new FolderThemeStudio.Core.Tests.TestSupport.TemporaryDirectory();
        var store = new FolderThemeStudio.App.Settings.JsonAppSettingsStore(System.IO.Path.Combine(directory.Path, "settings.json"));
        var fixture = new FolderThemeStudio.Core.Tests.TestSupport.MainViewModelFixture();
        var draft = new FolderThemeStudio.App.ViewModels.SettingsViewModel(FolderThemeStudio.App.Settings.AppSettings.Default, store,
            new FolderThemeStudio.App.Localization.LocalizationService(applyToApplication: false), _ => { }, fixture.Renderer);
        await store.SaveAsync(FolderThemeStudio.App.Settings.AppSettings.Default with { TutorialCompleted = true, MonitoringPaused = true, RecentColors = new[] { "#123456" } });
        Assert.True(await draft.SaveAsync());
        var saved = await store.LoadAsync();
        Assert.True(saved.TutorialCompleted); Assert.True(saved.MonitoringPaused);
        Assert.Contains("#123456", saved.RecentColors);
        draft.GradientStart.Hex = "#223344";
        Assert.True(await draft.SaveAsync());
        Assert.Contains("#123456", (await store.LoadAsync()).RecentColors);
        Assert.Contains("#223344", draft.RecentColors);
    }
    [Fact]
    public async Task ConfigureRoutesToMainWorkspaceWithoutLoadingOrShowingIsland()
    {
        var store = new UnusedStore();
        using var controller = new DynamicIslandController(store, () => false, _ => Task.CompletedTask, () => { },
            _ => throw new InvalidOperationException("Settings navigation must not create an Island window."));
        string? route = null;
        controller.SetSettingsNavigator(module => { route = module; return Task.CompletedTask; });
        await controller.ConfigureAsync();
        Assert.Equal("island", route);
        Assert.Equal(0, store.Loads);
    }

    [Fact]
    public async Task DisposedControllerDoesNotNavigate()
    {
        var controller = new DynamicIslandController(new UnusedStore(), () => false, _ => Task.CompletedTask, () => { });
        var calls = 0;
        controller.SetSettingsNavigator(_ => { calls++; return Task.CompletedTask; });
        controller.Dispose();
        await controller.ConfigureAsync();
        Assert.Equal(0, calls);
    }

    private sealed class UnusedStore : IIslandSettingsStore
    {
        public int Loads;
        public Task<IslandSettings> LoadAsync() { Loads++; throw new InvalidOperationException("Unexpected load"); }
        public Task SaveAsync(IslandSettings settings) => Task.CompletedTask;
    }
}
