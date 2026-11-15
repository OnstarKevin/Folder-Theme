using System.IO;
using System.IO.Compression;
using System.Windows.Controls;
using FolderThemeStudio.App.DynamicIsland;
using FolderThemeStudio.App.DynamicIsland.Plugins;
using FolderThemeStudio.Core.Tests.TestSupport;
using Xunit;

namespace FolderThemeStudio.Core.Tests.Application;

public sealed class IslandPluginUiTests
{
    [Fact]
    public void BundledInstallableExampleMatchesPublishedApiAndRunsRealJavaScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "fts-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var package = Path.Combine(root, "example.islandpkg");
        try
        {
            IslandPluginManagerWindow.WriteExample(package);
            using var manager = new IslandPluginManager(Path.Combine(root, "plugins"));
            var manifest = manager.InspectPackage(package);
            Assert.Empty(manifest.Permissions); Assert.Equal("0.1.0-beta.23", manifest.MinHostVersion);
            using var zip = ZipFile.OpenRead(package); using var reader = new StreamReader(zip.GetEntry("main.js")!.Open());
            var runtime = new PluginScriptRuntime(reader.ReadToEnd(), manifest.Id);
            Assert.StartsWith("专注 25:", runtime.Invoke("init", null)!.Text);
            Assert.StartsWith("专注", runtime.Invoke("tick", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())!.Text);
            runtime.Invoke("dispose", null); Assert.Null(runtime.CurrentContent);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void PromptIsCollapsedAndNativeBehaviorHasIndependentSwitches()
    {
        var root = Path.Combine(Path.GetTempPath(), "fts-ui-" + Guid.NewGuid().ToString("N"));
        using var manager = new IslandPluginManager(root);
        try
        {
            WpfTestHost.Invoke(() =>
            {
                var settings = new DynamicIslandSettingsWindow(new(), false, (_, _) => Task.CompletedTask, () => Task.FromResult(""));
                var plugins = new IslandPluginManagerWindow(manager, () => false);
                try
                {
                    settings.Show(); plugins.Show();
                    Assert.True(((CheckBox)settings.FindName("ClickThroughCheck")).IsChecked);
                    Assert.True(((CheckBox)settings.FindName("HoverDetailsCheck")).IsChecked);
                    Assert.NotNull(settings.FindName("ImageBackgroundCheck"));
                    Assert.Contains("Island Plugin API v1", ((TextBox)plugins.FindName("PromptBox")).Text);
                    Assert.False(((TextBox)plugins.FindName("PromptBox")).IsVisible);
                }
                finally { settings.Close(); plugins.Close(); }
            });
        }
        finally { manager.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
