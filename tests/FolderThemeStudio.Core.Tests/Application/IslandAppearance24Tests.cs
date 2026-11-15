using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandAppearance24Tests
{
    [Fact]
    public void ApprovedDimensionsAndDefaultTypefaceAreUsedForRealWindow()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = new DynamicIslandWindow(new() { ShowCpu = false, ShowMemory = false, ShowMusic = false, ShowQuotes = false }, clipboardFactory: (_, _) => new SilentClipboard());
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.InRange(window.Height, 36, 37); // Native size rounds to a physical pixel at fractional DPI.
                var text = (TextBlock)window.FindName("CompactMessage");
                Assert.Equal(14, text.FontSize); Assert.Equal(System.Windows.FontWeights.Bold, text.FontWeight);
                Assert.Contains("Microsoft YaHei", text.FontFamily.Source);
                Assert.NotNull(window.FindName("IslandImageBrush"));
                Assert.NotNull(((ImageBrush)window.FindName("IslandImageBrush")).ImageSource);
                window.SetEditMode(true); window.SetEditMode(false);
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.Controls.Grid)window.FindName("IslandBackgroundLayers")).Visibility);
                Assert.NotNull(((ImageBrush)window.FindName("IslandImageBrush")).ImageSource);
            }
            finally { window.CloseFromController(); }
        });
    }
    [Fact]
    public async Task LegacyTransparentPreferenceMigratesOnceThenBackgroundSwitchIsRespected()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "island.json");
        await File.WriteAllTextAsync(path, "{\"enabled\":true,\"imageBackground\":false,\"showMusic\":false}");
        var store = new IslandSettingsStore(path); var loaded = await store.LoadAsync();
        Assert.True(loaded.ImageBackground); Assert.False(loaded.ShowMusic);
        await store.SaveAsync(loaded with { ImageBackground = false });
        Assert.False((await store.LoadAsync()).ImageBackground);
    }
    [Fact]
    public void ImportControlsStayInlineAndSelectedTypefaceIsSaved()
    {
        WpfTestHost.Invoke(() =>
        {
            IslandSettings? saved = null;
            var settings = new IslandSettings();
            var customPath = typeof(IslandSettings).GetProperty("CustomFontPath");
            Assert.NotNull(customPath);
            Assert.NotNull(typeof(IslandSettings).GetProperty("CustomFontFamily"));
            var window = new DynamicIslandSettingsWindow(settings, false, (next, _) => { saved = next; return Task.CompletedTask; }, () => Task.FromResult(""));
            try
            {
                window.Show();
                Assert.NotNull(window.FindName("FontFamilyBox")); Assert.NotNull(window.FindName("ImportFontButton"));
                Assert.NotNull(window.FindName("FontPreview")); Assert.NotNull(window.FindName("ImageBackgroundCheck"));
                ((Button)window.FindName("SaveButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                Assert.NotNull(saved); Assert.True(saved.ImageBackground);
            }
            finally { window.Close(); }
        });
    }
    private sealed class SilentClipboard : IIslandClipboardListener { public void SetEnabled(bool enabled) { } public void Dispose() { } }
    [Fact]
    public async Task ImportedTypefaceSurvivesSettingsRoundTripAndCanBeResetInline()
    {
        using var temporary = new TemporaryDirectory();
        var fonts = new IslandFontStore(Path.Combine(temporary.Path, "fonts"));
        var imported = fonts.Import(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"));
        var store = new IslandSettingsStore(Path.Combine(temporary.Path, "settings.json"));
        await store.SaveAsync(new() { CustomFontPath = imported.FilePath, CustomFontFamily = imported.FamilyName });
        var loaded = await store.LoadAsync();
        Assert.Equal(imported.FilePath, loaded.CustomFontPath);
        WpfTestHost.Invoke(() =>
        {
            IslandSettings? saved = null;
            var window = new DynamicIslandSettingsWindow(loaded, false, (next, _) => { saved = next; return Task.CompletedTask; }, () => Task.FromResult(""), fonts);
            try
            {
                window.Show();
                Assert.Contains(imported.FamilyName, ((TextBlock)window.FindName("FontPreview")).FontFamily.Source);
                ((Button)window.FindName("ResetFontButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("Microsoft YaHei", ((TextBlock)window.FindName("FontPreview")).FontFamily.Source);
                ((Button)window.FindName("SaveButton")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                Assert.NotNull(saved); Assert.Empty(saved.CustomFontPath); Assert.Empty(saved.CustomFontFamily);
            }
            finally { window.Close(); }
        });
    }
}
